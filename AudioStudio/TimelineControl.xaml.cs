using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AudioStudio;

public partial class TimelineControl : UserControl
{
    public static float PixelsPerSecond = 100f;
    public static float TrackHeight = 80f;
    
    private List<AudioClipModel> _clips = new();
    private readonly Dictionary<AudioClipModel, ClipControl> _clipControls = new();
    
    public float CurrentTime 
    { 
        get => _currentTime;
        set
        {
            _currentTime = value;
            UpdatePlayheadOnly();
        }
    }
    private float _currentTime;
    
    public event Action<float>? SeekRequested;
    public event Action<AudioClipModel>? ClipMoved;
    public event Action<AudioClipModel>? ClipSelected;
    
    // Snap settings
    public static bool SnapEnabled = true;
    public static float SnapGridSize = 0.1f;
    
    // Zoom
    private float _zoom = 1.0f;
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Max(0.1f, Math.Min(10f, value));
            PixelsPerSecond = 100f * _zoom;
            RefreshAllClips();
        }
    }
    
    // Playhead - создаётся ОДИН раз
    private readonly Line _playheadLine;
    
    private AudioClipModel? _selectedClip;
    
    public TimelineControl()
    {
        InitializeComponent();
        
        // Создаём playhead ОДИН раз
        _playheadLine = new Line
        {
            Stroke = new SolidColorBrush(Color.FromRgb(255, 50, 50)),
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        // Playhead в отдельный Canvas (не в CanvasRoot!)
        PlayheadCanvas.Children.Add(_playheadLine);
        Panel.SetZIndex(PlayheadCanvas, 1000);
        
        // Render loop - НЕ трогаем Children
        CompositionTarget.Rendering += (s, e) => UpdateClipPositions();
        
        MouseWheel += OnMouseWheel;
    }
    
    public void SetClips(List<AudioClipModel> clips)
    {
        _clips = clips;
        RebuildAll();
    }
    
    public void AddClip(AudioClipModel clip)
    {
        _clips.Add(clip);
        CreateClipControl(clip);
        InvalidateVisual();
    }
    
    public void RemoveClip(AudioClipModel clip)
    {
        _clips.Remove(clip);
        if (_clipControls.TryGetValue(clip, out var control))
        {
            CanvasRoot.Children.Remove(control);
            _clipControls.Remove(clip);
        }
    }
    
    public void ClearClips()
    {
        _clips.Clear();
        _clipControls.Clear();
        CanvasRoot.Children.Clear();
        // Пересоздаём playhead
        CanvasRoot.Children.Add(_playheadLine);
    }
    
    private void RebuildAll()
    {
        // НЕ очищаем всё!
        // Удаляем только клипы
        foreach (var ctrl in _clipControls.Values)
        {
            CanvasRoot.Children.Remove(ctrl);
        }
        _clipControls.Clear();
        
        // Создаём клипы заново
        foreach (var clip in _clips)
        {
            CreateClipControl(clip);
        }
        
        // Пересоздаём playhead
        if (!CanvasRoot.Children.Contains(_playheadLine))
        {
            CanvasRoot.Children.Add(_playheadLine);
        }
        
        InvalidateVisual();
    }
    
    private void CreateClipControl(AudioClipModel clip)
    {
        var control = new ClipControl(clip);
        control.ClipMoved += OnClipMoved;
        control.ClipSelected += OnClipSelected;
        control.TrackChanged += OnTrackChanged;
        
        _clipControls[clip] = control;
        CanvasRoot.Children.Add(control);
    }
    
    private void OnClipMoved(AudioClipModel clip)
    {
        // Snap to grid
        if (SnapEnabled && SnapGridSize > 0)
        {
            clip.StartTime = MathF.Round(clip.StartTime / SnapGridSize) * SnapGridSize;
        }
        
        // Auto-snap: prevent overlap on same track
        FixOverlap(clip);
        
        ClipMoved?.Invoke(clip);
    }
    
    private void FixOverlap(AudioClipModel clip)
    {
        foreach (var other in _clips)
        {
            if (other == clip) continue;
            if (other.TrackIndex != clip.TrackIndex) continue;
            
            bool overlap =
                clip.StartTime < other.StartTime + other.Duration &&
                clip.StartTime + clip.Duration > other.StartTime;
            
            if (overlap)
            {
                // Place after the overlapping clip
                clip.StartTime = other.StartTime + other.Duration;
            }
        }
    }
    
    private void OnClipSelected(AudioClipModel clip)
    {
        SelectClip(clip);
        ClipSelected?.Invoke(clip);
    }
    
    private void OnTrackChanged(AudioClipModel clip)
    {
        // При смене дорожки обновляем позиции
        UpdateClipPositions();
        ClipMoved?.Invoke(clip);
    }
    
    public void SelectClip(AudioClipModel? clip)
    {
        if (_selectedClip != null && _clipControls.TryGetValue(_selectedClip, out var prev))
        {
            prev.SetSelected(false);
        }
        
        _selectedClip = clip;
        
        if (clip != null && _clipControls.TryGetValue(clip, out var curr))
        {
            curr.SetSelected(true);
        }
    }
    
    public void RefreshAllClips()
    {
        foreach (var kvp in _clipControls)
        {
            kvp.Value.UpdateSize();
            kvp.Value.RefreshWaveform();
        }
        UpdateClipPositions();
    }
    
    // Обновляем только позиции клипов (НЕ трогаем Children)
    private void UpdateClipPositions()
    {
        foreach (var clip in _clips)
        {
            if (_clipControls.TryGetValue(clip, out var control))
            {
                Canvas.SetLeft(control, clip.StartTime * PixelsPerSecond);
                Canvas.SetTop(control, clip.TrackIndex * TrackHeight);
                
                // Минимальная ширина 50px
                control.Width = Math.Max(50, clip.Duration * PixelsPerSecond);
            }
        }
        
        UpdatePlayheadOnly();
    }
    
        // Обновляем ТОЛЬКО позицию playhead
    private void UpdatePlayheadOnly()
    {
        double x = _currentTime * PixelsPerSecond;
        _playheadLine.X1 = x;
        _playheadLine.X2 = x;
        _playheadLine.Y1 = 0;
        _playheadLine.Y2 = Math.Max(200, ActualHeight);
    }
    
    public new void InvalidateVisual()
    {
        // Принудительная перерисовка ruler
        DrawRuler();
        UpdatePlayheadOnly();
    }
    
    private void DrawRuler()
    {
        RulerCanvas.Children.Clear();
        
        double width = Math.Max(1000, ActualWidth);
        
        // Определяем интервал делений
        float tickInterval = 1f;
        if (_zoom < 0.5f) tickInterval = 5f;
        else if (_zoom < 2f) tickInterval = 1f;
        else if (_zoom < 5f) tickInterval = 0.5f;
        else tickInterval = 0.1f;
        
        float endTime = (float)(width / PixelsPerSecond) + 1;
        
        for (float t = 0; t <= endTime; t += tickInterval)
        {
            double x = t * PixelsPerSecond;
            
            // Основная риска
            var tick = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 20,
                Y2 = 30,
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };
            RulerCanvas.Children.Add(tick);
            
            // Малые риски
            if (tickInterval >= 1f)
            {
                for (float sub = 0.25f; sub < 1f; sub += 0.25f)
                {
                    double sx = (t + sub) * PixelsPerSecond;
                    var smallTick = new Line
                    {
                        X1 = sx,
                        X2 = sx,
                        Y1 = 25,
                        Y2 = 30,
                        Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                        StrokeThickness = 1
                    };
                    RulerCanvas.Children.Add(smallTick);
                }
            }
            
            // Время
            var label = new TextBlock
            {
                Text = FormatTime(t),
                Foreground = Brushes.Gray,
                FontSize = 9
            };
            Canvas.SetLeft(label, x + 2);
            Canvas.SetTop(label, 4);
            RulerCanvas.Children.Add(label);
        }
        
        // Playhead на ruler
        var rulerPlayhead = new Line
        {
            X1 = _currentTime * PixelsPerSecond,
            X2 = _currentTime * PixelsPerSecond,
            Y1 = 0,
            Y2 = 30,
            Stroke = Brushes.Red,
            StrokeThickness = 1
        };
        RulerCanvas.Children.Add(rulerPlayhead);
    }
    
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == CanvasRoot)
        {
            var pos = e.GetPosition(CanvasRoot);
            float time = Math.Max(0, (float)(pos.X / PixelsPerSecond));
            SeekRequested?.Invoke(time);
            SelectClip(null);
        }
    }
    
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            Zoom *= e.Delta > 0 ? 1.1f : 0.9f;
            e.Handled = true;
        }
    }
    
    public float ZoomIn() { Zoom *= 1.2f; return Zoom; }
    public float ZoomOut() { Zoom /= 1.2f; return Zoom; }
    
    public float GetTotalDuration()
    {
        float max = 10f;
        foreach (var clip in _clips)
        {
            float end = clip.StartTime + clip.Duration;
            if (end > max) max = end;
        }
        return max + 10f;
    }
    
    private string FormatTime(float seconds)
    {
        int min = (int)(seconds / 60);
        int sec = (int)(seconds % 60);
        return $"{min}:{sec:D2}";
    }
}
