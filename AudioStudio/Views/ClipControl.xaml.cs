using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioStudio;

public partial class ClipControl : UserControl
{
    public new AudioClipModel Clip { get; }
    
    private bool _dragging;
    private Point _startMouse;
    private float _startTime;
    private float _startTrackY;
    
    public event Action<AudioClipModel>? ClipMoved;
    public event Action<AudioClipModel>? ClipSelected;
    public event Action<AudioClipModel>? TrackChanged;
    
    // Snap settings
    public static float SnapGrid = 0.1f;
    public static bool SnapEnabled = true;
    
    public ClipControl(AudioClipModel clip)
    {
        InitializeComponent();
        Clip = clip;
        DataContext = clip;
        
        Focusable = true;
        
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseMove += OnMove;
        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(clip.Color);
            ClipBorder.Background = new SolidColorBrush(color);
        }
        catch { }
        
        Loaded += (s, e) => 
        {
            UpdateSize();
            RenderWaveform();
        };
        
        SizeChanged += (s, e) => 
        {
            if (ActualWidth > 0 && ActualHeight > 0)
            {
                RenderWaveform();
            }
        };
    }
    
    public void UpdateSize()
    {
        Width = Math.Max(50, Clip.Duration * TimelineControl.PixelsPerSecond);
    }
    
    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        UpdateCursor();
    }
    
    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        Cursor = Cursors.Arrow;
        Opacity = 1.0;
    }
    
    private void UpdateCursor()
    {
        Cursor = Cursors.SizeAll;
        Opacity = 0.85;
    }
    
    private void RenderWaveform()
    {
        if (Clip.Samples == null || Clip.Samples.Length == 0)
            return;
        
        // КЛЮЧЕВОЕ: используем ActualWidth, не Duration
        int width = Math.Max(1, (int)ActualWidth);
        int height = Math.Max(1, (int)ActualHeight);
        
        if (width <= 1 || height <= 1) return;
        
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        
        // Цвета (фиолетовый акцент)
        byte r = 200, g = 209, b = 255;
        
        int samplesPerPixel = Math.Max(1, Clip.Samples.Length / width);
        
        // Тёмный фон
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x1E; pixels[i + 1] = 0x1E; pixels[i + 2] = 0x1E; pixels[i + 3] = 255;
        }
        
        // Рисуем waveform СНИЗУ ВВЕРХ (как в DAW)
        for (int x = 0; x < width; x++)
        {
            int start = x * samplesPerPixel;
            int end = Math.Min(start + samplesPerPixel, Clip.Samples.Length);
            
            float max = 0;
            for (int i = start; i < end; i++)
                max = Math.Max(max, Math.Abs(Clip.Samples[i]));
            
            // Высота столбика от низа
            int barHeight = Math.Max(1, (int)(max * height));
            
            // Рисуем столбик от низа до barHeight
            for (int yy = height - barHeight; yy < height; yy++)
            {
                if (yy >= 0 && yy < height)
                {
                    int idx = yy * stride + x * 4;
                    pixels[idx] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = 255;
                }
            }
        }
        
        try
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            WaveformImage.Source = bmp;
            WaveformImage.Width = width;
            WaveformImage.Height = height;
        }
        catch { }
    }
    
    public void RefreshWaveform() => RenderWaveform();
    
    public void SetSelected(bool selected)
    {
        SelectedIndicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        ClipBorder.BorderBrush = selected 
            ? new SolidColorBrush(Color.FromRgb(120, 129, 255))
            : new SolidColorBrush(Color.FromRgb(90, 90, 90));
    }
    
    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        
        // КЛЮЧЕВОЕ: координаты относительно Canvas!
        var canvas = Parent as Canvas;
        if (canvas == null) return;
        
        _startMouse = e.GetPosition(canvas);
        _startTime = Clip.StartTime;
        _startTrackY = Clip.TrackIndex * TimelineControl.TrackHeight;
        
        CaptureMouse();
        e.Handled = true;
        
        ClipSelected?.Invoke(Clip);
    }
    
    private void OnMove(object sender, MouseEventArgs e)
    {
        UpdateCursor();
        
        if (!_dragging) return;
        
        // КЛЮЧЕВОЕ: координаты относительно Canvas!
        var canvas = Parent as Canvas;
        if (canvas == null) return;
        
        // Проверка на валидность _startMouse
        if (_startMouse.X < -10000 || _startMouse.Y < -10000) return;
        
        var pos = e.GetPosition(canvas);
        
        // Горизонтальное перемещение (время)
        float dx = (float)(pos.X - _startMouse.X);
        float newTime = _startTime + dx / TimelineControl.PixelsPerSecond;
        
        if (SnapEnabled && SnapGrid > 0)
            newTime = MathF.Round(newTime / SnapGrid) * SnapGrid;
        
        Clip.StartTime = Math.Max(0, newTime);
        
        // Вертикальное перемещение (дорожка)
        float dy = (float)(pos.Y - _startMouse.Y);
        int newTrack = (int)((_startTrackY + dy) / TimelineControl.TrackHeight);
        newTrack = Math.Max(0, newTrack);
        
        if (newTrack != Clip.TrackIndex)
        {
            Clip.TrackIndex = newTrack;
            TrackChanged?.Invoke(Clip);
        }
        
        ClipMoved?.Invoke(Clip);
    }
    
    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }
}
