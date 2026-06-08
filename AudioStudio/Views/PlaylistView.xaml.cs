using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using System.Windows.Shapes;
using AudioStudio.Models;

namespace AudioStudio.Views
{
    public partial class PlaylistView : UserControl
    {
        public event EventHandler? ScrollUpdated;
        public event Action<string, double, int>? FileDropped;
        public event Action<double>? SeekRequested;
        public event Action<TrackItemViewModel>? ClipSelected;
        public event Action<TrackItemViewModel>? ClipContextMenuRequested;
        public event Action<TrackItemViewModel, double, double>? ClipRangeSelected;
        public event Action<TrackItemViewModel, double, int, double, int>? ClipMoved;
        public event Action<TrackItemViewModel, double, double>? ClipResized;
        /// <summary>trackIndex, tick position, isRightButton</summary>
        public event Action<int, double, bool>? EmptyAreaInteracted;

        public double HorizontalScrollOffset => MainScroller.HorizontalOffset;
        public bool IsDraggingPlayhead => _isDraggingPlayhead;

        private PlaylistViewModel _model = new();
        private readonly Dictionary<Guid, Border> _clipVisuals = new();
        private readonly Dictionary<Guid, float[]> _peaksCache = new();
        private bool _needsRebuild = true;
        private bool _showSpectrogramInClip;
        private double _lastGridOffset = double.NaN;
        private double _lastGridVpW = double.NaN;
        private double _lastGridTotalH = double.NaN;
        private double _lastGridZoom = double.NaN;
        private readonly List<Line> _gridLines = new();
        private readonly List<Rectangle> _trackBgRects = new();
        private Line? _playheadLine;
        private Rectangle? _playheadHitArea;
        private readonly TranslateTransform _playheadTransform = new();
        private double _playheadSeconds = -1;
        private double _lastPlayheadX = double.NaN;
        private bool _isDraggingPlayhead;
        private double _playheadDragStartX;
        private double _playheadDragStartSeconds;
        private Guid? _selectedClipId;

        private enum DragMode { None, Move, ResizeRight, SelectRange }
        private double _selectStartLocalX;
        private double _selectEndLocalX;
        private Rectangle? _rangeSelectionRect;
        private Guid? _rangeSelClipId;
        private double _rangeSelStartSec = -1;
        private double _rangeSelEndSec = -1;
        private DragMode _dragMode = DragMode.None;
        private TrackItemViewModel? _activeClip;
        private Point _dragStartPoint;
        private double _dragStartTick;
        private int _dragStartTrack;
        private double _dragStartDuration;
        private bool _isDragging;
        private DispatcherTimer? _zoomWaveformTimer;

        private static readonly SolidColorBrush GridBarBrush = Freeze(Color.FromArgb(50, 120, 129, 255));
        private static readonly SolidColorBrush GridMajorBrush = Freeze(Color.FromArgb(32, 160, 160, 160));
        private static readonly SolidColorBrush GridMinorBrush = Freeze(Color.FromArgb(12, 130, 130, 130));

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public PlaylistView()
        {
            InitializeComponent();
            _model.AudioClips.CollectionChanged += OnClipsChanged;
            AllowDrop = true;
            Drop += OnDrop;
            SizeChanged += (s, e) => DrawGridLines();

            var playheadBrush = new SolidColorBrush(Color.FromRgb(255, 80, 80));
            playheadBrush.Freeze();
            _playheadLine = new Line
            {
                Stroke = playheadBrush,
                StrokeThickness = 2,
                X1 = 0,
                X2 = 0,
                Y1 = 0,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                RenderTransform = _playheadTransform
            };
            RenderOptions.SetEdgeMode(_playheadLine, EdgeMode.Unspecified);
            RenderOptions.SetBitmapScalingMode(_playheadLine, BitmapScalingMode.HighQuality);

            _playheadHitArea = new Rectangle
            {
                Width = 14,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeWE,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = true
            };
            _playheadHitArea.MouseLeftButtonDown += PlayheadHit_MouseLeftButtonDown;
            _playheadHitArea.MouseMove += PlayheadHit_MouseMove;
            _playheadHitArea.MouseLeftButtonUp += PlayheadHit_MouseLeftButtonUp;
            _playheadHitArea.MouseLeave += PlayheadHit_MouseLeave;

            PlayheadLayer.Children.Add(_playheadLine);
            PlayheadLayer.Children.Add(_playheadHitArea);
        }

        public PlaylistViewModel Model
        {
            get => _model;
            set
            {
                if (_model != null)
                    _model.AudioClips.CollectionChanged -= OnClipsChanged;
                _model = value ?? new PlaylistViewModel();
                _model.AudioClips.CollectionChanged += OnClipsChanged;
                _needsRebuild = true;
                InvalidateVisual();
            }
        }

        public void InvalidateAll()
        {
            _needsRebuild = true;
            RebuildIfNeeded();
        }

        public void InsertClip(TrackItemViewModel clip, float[] peaks)
        {
            _peaksCache[clip.Id] = peaks;
            _model.AudioClips.Add(clip);
        }

        public void UpdateClipLayout(TrackItemViewModel clip, bool layoutOnly = false)
        {
            UpdateClipVisual(clip, layoutOnly);
        }

        public void SetViewMode(bool spectrogram)
        {
            _showSpectrogramInClip = spectrogram;
            RedrawClipWaveforms();
        }

        public void AddTrack()
        {
            _model.NumTracks++;
            _needsRebuild = true;
            RebuildIfNeeded();
        }

        public void RemoveTrack()
        {
            if (_model.NumTracks <= 1) return;
            var toRemove = _model.AudioClips.Where(c => c.TrackIndex >= _model.NumTracks - 1).ToList();
            foreach (var c in toRemove)
            {
                if (_selectedClipId == c.Id) _selectedClipId = null;
                _peaksCache.Remove(c.Id);
                _model.AudioClips.Remove(c);
            }
            _model.NumTracks--;
            _needsRebuild = true;
            RebuildIfNeeded();
        }

        public Guid? SelectedClipId => _selectedClipId;

        public TrackItemViewModel? GetSelectedClip() =>
            _selectedClipId.HasValue
                ? _model.AudioClips.FirstOrDefault(c => c.Id == _selectedClipId.Value)
                : null;

        public void SelectClip(Guid? clipId, bool raiseEvent = true)
        {
            _selectedClipId = clipId;
            UpdateSelectionVisuals();
            if (raiseEvent && clipId.HasValue)
            {
                var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId.Value);
                if (clip != null) ClipSelected?.Invoke(clip);
            }
        }

        public void RemoveClip(Guid clipId)
        {
            var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return;
            if (_selectedClipId == clipId) _selectedClipId = null;
            _peaksCache.Remove(clipId);
            _model.AudioClips.Remove(clip);
        }

        public void RemoveClipsOnTrack(int trackIndex)
        {
            var toRemove = _model.AudioClips.Where(c => c.TrackIndex == trackIndex).ToList();
            foreach (var c in toRemove)
                RemoveClip(c.Id);
        }

        public void RefreshClipWaveform(Guid clipId)
        {
            var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (clip != null) UpdateClipVisual(clip);
        }

        public void UpdatePeaks(Guid clipId, float[] peaks)
        {
            _peaksCache[clipId] = peaks;
            RefreshClipWaveform(clipId);
        }

        #region Rebuild

        private void OnClipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _needsRebuild = true;
            RebuildIfNeeded();
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            InvalidateGridCache();
            DrawGridLines(force: true);
            ScrollUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void RebuildIfNeeded()
        {
            if (!_needsRebuild) return;

            _needsRebuild = false;
            TrackBgLayer.Children.Clear();
            ClipLayer.Children.Clear();
            _clipVisuals.Clear();

            UpdateContentSize();
            DrawTrackBackgrounds();

            foreach (var clip in _model.AudioClips)
            {
                var border = CreateClipVisual(clip);
                _clipVisuals[clip.Id] = border;
                ClipLayer.Children.Add(border);
            }

            DrawGridLines();
        }

        private void UpdateContentSize()
        {
            double maxTicks = 0;
            foreach (var clip in _model.AudioClips)
            {
                double end = clip.EndTick;
                if (end > maxTicks) maxTicks = end;
            }
            double vpW = MainScroller.ViewportWidth;
            if (double.IsNaN(vpW)) vpW = 0;
            double vpH = MainScroller.ViewportHeight;
            if (double.IsNaN(vpH)) vpH = 0;
            double contentW = Math.Max(maxTicks * _model.ZoomX + 200, vpW);
            double contentH = Math.Max(_model.TotalHeight, vpH);
            if (double.IsNaN(contentW) || double.IsInfinity(contentW)) contentW = 200;
            if (double.IsNaN(contentH) || double.IsInfinity(contentH)) contentH = 200;
            ContentGrid.Width = contentW;
            ContentGrid.Height = contentH;
            GridLayer.Width = contentW;
            GridLayer.Height = contentH;
            TrackBgLayer.Width = contentW;
            TrackBgLayer.Height = contentH;
            ClipLayer.Width = contentW;
            ClipLayer.Height = contentH;
            SelectionLayer.Width = contentW;
            SelectionLayer.Height = contentH;
            PlayheadLayer.Width = contentW;
            PlayheadLayer.Height = contentH;
            if (_playheadLine != null)
                _playheadLine.Y2 = contentH;
        }

        public void SetPlayheadTime(double seconds)
        {
            _playheadSeconds = seconds;
            UpdatePlayheadPosition();
        }

        public void HidePlayhead()
        {
            _playheadSeconds = -1;
            _lastPlayheadX = double.NaN;
            if (_playheadLine != null)
                _playheadLine.Visibility = Visibility.Collapsed;
            if (_playheadHitArea != null)
                _playheadHitArea.Visibility = Visibility.Collapsed;
        }

        private void UpdatePlayheadPosition()
        {
            if (_playheadLine == null || _playheadSeconds < 0) return;

            double x = _model.SecondsToTick(_playheadSeconds) * _model.ZoomX;
            if (!_isDraggingPlayhead
                && !double.IsNaN(_lastPlayheadX)
                && Math.Abs(x - _lastPlayheadX) < 0.01
                && _playheadLine.Visibility == Visibility.Visible)
                return;

            _lastPlayheadX = x;
            _playheadTransform.X = x;
            _playheadLine.Visibility = Visibility.Visible;

            if (_playheadHitArea != null)
            {
                double h = ContentGrid.Height;
                if (h <= 0 || double.IsNaN(h)) h = Math.Max(_model.TotalHeight, 100);
                _playheadLine.Y2 = h;
                Canvas.SetLeft(_playheadHitArea, x - _playheadHitArea.Width / 2);
                Canvas.SetTop(_playheadHitArea, 0);
                _playheadHitArea.Height = h;
                _playheadHitArea.Visibility = Visibility.Visible;
            }
        }

        private double PixelXToSeconds(double pixelX)
        {
            double pps = PixelsPerSecond;
            if (pps <= 1e-6) return 0;
            return Math.Max(0, pixelX / pps);
        }

        private void PlayheadHit_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_playheadSeconds < 0) return;
            _isDraggingPlayhead = true;
            _playheadDragStartX = e.GetPosition(PlayheadLayer).X;
            _playheadDragStartSeconds = _playheadSeconds;
            _playheadHitArea?.CaptureMouse();
            e.Handled = true;
        }

        private void PlayheadHit_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingPlayhead || e.LeftButton != MouseButtonState.Pressed) return;
            double x = e.GetPosition(PlayheadLayer).X;
            double seconds = PixelXToSeconds(_playheadDragStartSeconds * PixelsPerSecond + (x - _playheadDragStartX));
            _lastPlayheadX = double.NaN;
            _playheadSeconds = seconds;
            UpdatePlayheadPosition();
            SeekRequested?.Invoke(seconds);
            e.Handled = true;
        }

        private void PlayheadHit_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndPlayheadDrag();
            e.Handled = true;
        }

        private void PlayheadHit_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDraggingPlayhead && e.LeftButton != MouseButtonState.Pressed)
                EndPlayheadDrag();
        }

        private void EndPlayheadDrag()
        {
            if (!_isDraggingPlayhead) return;
            _isDraggingPlayhead = false;
            _playheadHitArea?.ReleaseMouseCapture();
        }

        private void InvalidateGridCache()
        {
            _lastGridOffset = double.NaN;
            _lastGridVpW = double.NaN;
            _lastGridTotalH = double.NaN;
            _lastGridZoom = double.NaN;
        }

        private void DrawTrackBackgrounds()
        {
            TrackBgLayer.Children.Clear();
            _trackBgRects.Clear();
            double w = ContentGrid.Width;
            if (w <= 0 || double.IsNaN(w)) w = 200;

            for (int i = 0; i < _model.NumTracks; i++)
            {
                var rect = new Rectangle
                {
                    Width = w,
                    Height = _model.TrackHeight,
                    Fill = new SolidColorBrush(i % 2 == 0
                        ? Color.FromRgb(28, 28, 32)
                        : Color.FromRgb(22, 22, 26)),
                    IsHitTestVisible = false
                };
                Canvas.SetTop(rect, i * _model.TrackHeight);
                _trackBgRects.Add(rect);
                TrackBgLayer.Children.Add(rect);
            }
        }

        private void UpdateTrackBackgroundWidths()
        {
            double w = ContentGrid.Width;
            if (w <= 0 || double.IsNaN(w)) return;
            foreach (var rect in _trackBgRects)
                rect.Width = w;
        }

        private double PixelsPerSecond => _model.ZoomX * _model.TicksPerSecond;

        private void DrawGridLines(bool force = false)
        {
            double scrollX = MainScroller.HorizontalOffset;
            double vpW = MainScroller.ViewportWidth;
            if (vpW <= 0 || double.IsNaN(vpW)) vpW = Math.Max(ActualWidth, MainScroller.ActualWidth);
            if (vpW <= 0 || double.IsNaN(vpW)) vpW = 100;

            double totalH = ContentGrid.Height;
            if (totalH <= 0 || double.IsNaN(totalH)) totalH = Math.Max(_model.TotalHeight, 100);

            double pps = PixelsPerSecond;

            if (!force &&
                Math.Abs(scrollX - _lastGridOffset) < 0.5 &&
                Math.Abs(vpW - _lastGridVpW) < 0.5 &&
                Math.Abs(totalH - _lastGridTotalH) < 0.5 &&
                Math.Abs(_model.ZoomX - _lastGridZoom) < 1e-9)
                return;

            _lastGridOffset = scrollX;
            _lastGridVpW = vpW;
            _lastGridTotalH = totalH;
            _lastGridZoom = _model.ZoomX;

            var grid = TimelineGrid.Compute(pps, _model.Bpm);
            double visibleStartSec = scrollX / pps;
            double visibleEndSec = (scrollX + vpW) / pps;

            int i0 = TimelineGrid.StartIndex(visibleStartSec, grid.MinorStepSeconds);
            int i1 = TimelineGrid.EndIndex(visibleEndSec, grid.MinorStepSeconds);

            var lines = new List<(double x, double thickness, SolidColorBrush brush)>();
            for (int i = i0; i <= i1; i++)
            {
                double t = i * grid.MinorStepSeconds;
                if (t < 0) continue;

                double x = t * pps;
                bool isBar = TimelineGrid.IsBarLine(i, grid.MinorStepSeconds, grid.BarStepSeconds);
                bool isMajor = grid.MinorPerMajor > 0 && i % grid.MinorPerMajor == 0;
                SolidColorBrush brush = isBar ? GridBarBrush
                    : isMajor ? GridMajorBrush
                    : GridMinorBrush;
                lines.Add((x, isBar ? 1.0 : 0.5, brush));
            }

            // Reuse Line elements
            for (int i = 0; i < Math.Max(lines.Count, _gridLines.Count); i++)
            {
                if (i < lines.Count)
                {
                    if (i >= _gridLines.Count)
                    {
                        var line = new Line { IsHitTestVisible = false };
                        _gridLines.Add(line);
                        GridLayer.Children.Add(line);
                    }
                    _gridLines[i].X1 = _gridLines[i].X2 = lines[i].x;
                    _gridLines[i].Y1 = 0;
                    _gridLines[i].Y2 = totalH;
                    _gridLines[i].Stroke = lines[i].brush;
                    _gridLines[i].StrokeThickness = lines[i].thickness;
                    _gridLines[i].Visibility = Visibility.Visible;
                }
                else
                {
                    _gridLines[i].Visibility = Visibility.Collapsed;
                }
            }
        }

        private Border CreateClipVisual(TrackItemViewModel clip)
        {
            double x = clip.StartTick * _model.ZoomX;
            double w = clip.DurationTicks * _model.ZoomX;
            double y = clip.TrackIndex * _model.TrackHeight + 3;
            double h = _model.TrackHeight - 6;

            Color clipColor;
            try { clipColor = (Color)ColorConverter.ConvertFromString(clip.Color); }
            catch { clipColor = Color.FromRgb(120, 129, 255); }

            var border = new Border
            {
                Width = Math.Max(4, w),
                Height = Math.Max(4, h),
                CornerRadius = new CornerRadius(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 180, 180, 180)),
                BorderThickness = new Thickness(0.5),
                Background = new SolidColorBrush(Color.FromArgb(50, clipColor.R, clipColor.G, clipColor.B)),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Tag = clip.Id
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });

            var contentCanvas = new Canvas
            {
                Width = Math.Max(4, w),
                Height = h,
                ClipToBounds = true,
                IsHitTestVisible = false
            };
            DrawClipContent(contentCanvas, clip, w, h);
            Grid.SetColumnSpan(contentCanvas, 2);
            inner.Children.Add(contentCanvas);

            var text = new TextBlock
            {
                Text = clip.Name,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 0,
                    ShadowDepth = 1,
                    Opacity = 0.6,
                    BlurRadius = 1
                }
            };
            Grid.SetColumn(text, 0);

            var grip = new Rectangle
            {
                Width = 5,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeWE,
                IsHitTestVisible = true,
                ToolTip = "Изменить длину"
            };
            Grid.SetColumn(grip, 1);

            inner.Children.Add(text);
            inner.Children.Add(grip);
            border.Child = inner;

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);

            border.MouseLeftButtonDown += Clip_MouseLeftButtonDown;
            border.MouseRightButtonDown += Clip_MouseRightButtonDown;
            ApplySelectionStyle(border, clip.Id == _selectedClipId);
            return border;
        }

        private static readonly SolidColorBrush SelectedBorderBrush =
            new(Color.FromRgb(120, 129, 255));
        private static readonly SolidColorBrush NormalBorderBrush =
            new(Color.FromArgb(60, 180, 180, 180));

        private void ApplySelectionStyle(Border border, bool selected)
        {
            border.BorderBrush = selected ? SelectedBorderBrush : NormalBorderBrush;
            border.BorderThickness = selected ? new Thickness(2) : new Thickness(0.5);
        }

        private void UpdateSelectionVisuals()
        {
            foreach (var kvp in _clipVisuals)
                ApplySelectionStyle(kvp.Value, kvp.Key == _selectedClipId);
        }

        private void DrawClipContent(Canvas canvas, TrackItemViewModel clip, double width, double height)
        {
            canvas.Children.Clear();
            if (_peaksCache.TryGetValue(clip.Id, out var peaks) && peaks.Length > 0)
            {
                if (_showSpectrogramInClip)
                    DrawSpectrogramGraph(canvas, peaks, width, height);
                else
                    DrawWaveformInClip(canvas, peaks, width, height);
            }
        }

        private static double ComputePeakScale(float[] peaks, double centerY, double fillRatio = 0.88)
        {
            float maxPeak = 0;
            for (int i = 0; i < peaks.Length; i++)
                if (peaks[i] > maxPeak) maxPeak = peaks[i];
            double amplitude = centerY * fillRatio;
            return maxPeak > 1e-6f ? amplitude / maxPeak : amplitude;
        }

        private void DrawWaveformInClip(Canvas canvas, float[] peaks, double width, double height)
        {
            canvas.Children.Clear();
            if (peaks.Length == 0 || width < 2 || height < 2) return;

            int n = Math.Min(peaks.Length, Math.Max(10, (int)width));
            double xStep = width / n;
            double centerY = height / 2;
            double scale = ComputePeakScale(peaks, centerY);

            var color = Color.FromArgb(220, 255, 255, 255);

            var geom = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(0, centerY) };
            for (int i = 0; i < n; i++)
            {
                int idx = (int)((double)i / n * peaks.Length);
                float peak = peaks[Math.Min(idx, peaks.Length - 1)];
                double x = i * xStep;
                double yVal = centerY - peak * scale;
                fig.Segments.Add(new LineSegment(new Point(x, yVal), true));
            }
            fig.Segments.Add(new LineSegment(new Point(width, centerY), true));
            geom.Figures.Add(fig);

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geom,
                Fill = new SolidColorBrush(color),
                Opacity = 0.75
            });

            var geom2 = new PathGeometry();
            var fig2 = new PathFigure { StartPoint = new Point(0, centerY) };
            for (int i = 0; i < n; i++)
            {
                int idx = (int)((double)i / n * peaks.Length);
                float peak = peaks[Math.Min(idx, peaks.Length - 1)];
                double x = i * xStep;
                double yVal = centerY + peak * scale;
                fig2.Segments.Add(new LineSegment(new Point(x, yVal), true));
            }
            fig2.Segments.Add(new LineSegment(new Point(width, centerY), true));
            geom2.Figures.Add(fig2);

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geom2,
                Fill = new SolidColorBrush(color),
                Opacity = 0.45
            });
        }

        private void DrawSpectrogramGraph(Canvas canvas, float[] peaks, double width, double height)
        {
            canvas.Children.Clear();
            if (peaks.Length == 0 || width < 2 || height < 2) return;

            int n = Math.Min(peaks.Length, Math.Max(10, (int)width));
            double xStep = width / n;
            double scale = ComputePeakScale(peaks, height / 2, 0.92) * 2;

            var geom = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(0, height) };
            for (int i = 0; i < n; i++)
            {
                int idx = (int)((double)i / n * peaks.Length);
                float peak = peaks[Math.Min(idx, peaks.Length - 1)];
                double x = i * xStep;
                double yVal = height - peak * scale;
                fig.Segments.Add(new LineSegment(new Point(x, yVal), true));
            }
            fig.Segments.Add(new LineSegment(new Point(width, height), true));
            geom.Figures.Add(fig);

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geom,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0xE0, 0xE4, 0xFF), 0.0),
                        new GradientStop(Color.FromRgb(0x78, 0x81, 0xFF), 0.25),
                        new GradientStop(Color.FromRgb(0x4A, 0x51, 0xCC), 0.5),
                        new GradientStop(Color.FromRgb(0x24, 0x28, 0x70), 0.75),
                        new GradientStop(Color.FromRgb(0x0E, 0x10, 0x2E), 1.0)
                    }
                },
                Opacity = 0.85
            });
        }

        private void UpdateClipVisual(TrackItemViewModel clip, bool layoutOnly = false)
        {
            if (!_clipVisuals.TryGetValue(clip.Id, out var border)) return;
            double w = Math.Max(4, clip.DurationTicks * _model.ZoomX);
            double h = _model.TrackHeight - 6;
            border.Width = w;
            border.Height = Math.Max(4, h);
            Canvas.SetLeft(border, clip.StartTick * _model.ZoomX);
            Canvas.SetTop(border, clip.TrackIndex * _model.TrackHeight + 3);

            if (border.Child is Grid inner && inner.Children.Count > 0 && inner.Children[0] is Canvas contentCanvas)
            {
                contentCanvas.Width = w;
                contentCanvas.Height = h;
                if (!layoutOnly)
                    DrawClipContent(contentCanvas, clip, w, h);
            }
        }

        private void UpdateAllClipLayouts(bool layoutOnly = false)
        {
            foreach (var clip in _model.AudioClips)
                UpdateClipVisual(clip, layoutOnly);
        }

        private void ScheduleZoomWaveformRedraw()
        {
            if (_zoomWaveformTimer == null)
            {
                _zoomWaveformTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _zoomWaveformTimer.Tick += (_, _) =>
                {
                    _zoomWaveformTimer!.Stop();
                    foreach (var clip in _model.AudioClips)
                        UpdateClipVisual(clip, layoutOnly: false);
                };
            }
            _zoomWaveformTimer.Stop();
            _zoomWaveformTimer.Start();
        }

        private void RefreshTrackBackgrounds()
        {
            DrawTrackBackgrounds();
        }

        private void RedrawClipWaveforms()
        {
            UpdateAllClipLayouts();
        }

        #endregion

        #region Drag / Resize

        private (int track, double tick) GetEmptyAreaHit(Point posInClipLayer)
        {
            int track = (int)(posInClipLayer.Y / _model.TrackHeight);
            track = Math.Max(0, Math.Min(_model.NumTracks - 1, track));
            double tick = Math.Max(0, posInClipLayer.X / _model.ZoomX);
            return (track, tick);
        }

        private void ClipLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not Canvas) return;
            Point pos = e.GetPosition(ClipLayer);
            var (track, tick) = GetEmptyAreaHit(pos);
            EmptyAreaInteracted?.Invoke(track, tick, false);
            if (Keyboard.Modifiers == ModifierKeys.Control) return;
            double seconds = _model.TickToSeconds(tick);
            SeekRequested?.Invoke(seconds);
            e.Handled = true;
        }

        private void ClipLayer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not Canvas) return;
            Point pos = e.GetPosition(ClipLayer);
            var (track, tick) = GetEmptyAreaHit(pos);
            EmptyAreaInteracted?.Invoke(track, tick, true);
            e.Handled = true;
        }

        private void Clip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = (Border)sender;
            if (border.Tag is not Guid clipId) return;
            if (!_model.AudioClips.Any(c => c.Id == clipId)) return;
            _activeClip = _model.AudioClips.First(c => c.Id == clipId);

            Point pos = e.GetPosition(ClipLayer);
            double clipLeftPx = _activeClip.StartTick * _model.ZoomX;
            double clipRightPx = clipLeftPx + _activeClip.DurationTicks * _model.ZoomX;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                SelectClip(clipId, raiseEvent: true);
                _dragMode = DragMode.SelectRange;
                _selectStartLocalX = Math.Clamp(pos.X - clipLeftPx, 0, clipRightPx - clipLeftPx);
                _selectEndLocalX = _selectStartLocalX;
                _dragStartPoint = pos;
                _isDragging = false;
                UpdateRangeSelectionVisual();
                ClipLayer.CaptureMouse();
                e.Handled = true;
                return;
            }

            _dragMode = Math.Abs(pos.X - clipRightPx) < 8 ? DragMode.ResizeRight : DragMode.Move;
            _dragStartPoint = pos;
            _dragStartTick = _activeClip.StartTick;
            _dragStartTrack = _activeClip.TrackIndex;
            _dragStartDuration = _activeClip.DurationTicks;
            _isDragging = false;
            ClipLayer.CaptureMouse();
            e.Handled = true;
        }

        private void Clip_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = (Border)sender;
            if (border.Tag is not Guid clipId) return;
            var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return;

            SelectClip(clipId, raiseEvent: true);
            ClipContextMenuRequested?.Invoke(clip);
            e.Handled = true;
        }

        private void ClipLayer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_activeClip == null || _dragMode == DragMode.None) return;

            Point pos = e.GetPosition(ClipLayer);
            double dx = pos.X - _dragStartPoint.X;
            double dy = pos.Y - _dragStartPoint.Y;

            if (_dragMode == DragMode.SelectRange)
            {
                if (!_isDragging && (Math.Abs(dx) > 2 || Math.Abs(dy) > 2))
                    _isDragging = true;
                double clipLeftPx = _activeClip.StartTick * _model.ZoomX;
                double clipW = _activeClip.DurationTicks * _model.ZoomX;
                _selectEndLocalX = Math.Clamp(pos.X - clipLeftPx, 0, clipW);
                UpdateRangeSelectionVisual();
                return;
            }

            if (!_isDragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
                _isDragging = true;

            if (!_isDragging) return;

            switch (_dragMode)
            {
                case DragMode.Move:
                    double newTick = _dragStartTick + dx / _model.ZoomX;
                    int newTrack = _dragStartTrack + (int)Math.Round(dy / _model.TrackHeight);
                    _activeClip.StartTick = Math.Max(0, newTick);
                    _activeClip.TrackIndex = Math.Max(0, Math.Min(_model.NumTracks - 1, newTrack));
                    UpdateClipVisual(_activeClip, layoutOnly: true);
                    break;

                case DragMode.ResizeRight:
                    double newDuration = _dragStartDuration + dx / _model.ZoomX;
                    _activeClip.DurationTicks = Math.Max(TrackItemViewModel.PPQN / 4, newDuration);
                    UpdateClipVisual(_activeClip, layoutOnly: true);
                    break;
            }
        }

        private void ClipLayer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                EndDrag();
        }

        private void ClipLayer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDragging)
                EndDrag();
        }

        private void EndDrag()
        {
            if (_activeClip == null || _dragMode == DragMode.None) return;

            if (_dragMode == DragMode.SelectRange && _activeClip != null)
            {
                double clipLeftPx = _activeClip.StartTick * _model.ZoomX;
                double leftPx = clipLeftPx + Math.Min(_selectStartLocalX, _selectEndLocalX);
                double rightPx = clipLeftPx + Math.Max(_selectStartLocalX, _selectEndLocalX);
                double startSec = _model.TickToSeconds(leftPx / _model.ZoomX);
                double endSec = _model.TickToSeconds(rightPx / _model.ZoomX);
                if (endSec - startSec >= 0.05)
                {
                    _rangeSelClipId = _activeClip.Id;
                    _rangeSelStartSec = startSec;
                    _rangeSelEndSec = endSec;
                    ClipRangeSelected?.Invoke(_activeClip, startSec, endSec);
                }
            }
            else if (_dragMode == DragMode.Move)
            {
                _activeClip.StartTick = _model.SnapToGrid(_activeClip.StartTick);
                _activeClip.StartTick = Math.Max(0, _activeClip.StartTick);
                UpdateClipVisual(_activeClip);
                UpdateContentSize();
                RefreshTrackBackgrounds();
                InvalidateGridCache();
                DrawGridLines(force: true);

                bool moved = Math.Abs(_activeClip.StartTick - _dragStartTick) > 0.01
                    || _activeClip.TrackIndex != _dragStartTrack;
                if (moved)
                {
                    ClipMoved?.Invoke(_activeClip, _dragStartTick, _dragStartTrack,
                        _activeClip.StartTick, _activeClip.TrackIndex);
                }
                else if (!_isDragging)
                {
                    SelectClip(_activeClip.Id, raiseEvent: true);
                }
            }
            else if (_isDragging && _dragMode == DragMode.ResizeRight)
            {
                double snappedEnd = _model.SnapToGrid(_activeClip.StartTick + _activeClip.DurationTicks);
                double newDur = snappedEnd - _activeClip.StartTick;
                _activeClip.DurationTicks = Math.Max(TrackItemViewModel.PPQN / 4, newDur);
                UpdateClipVisual(_activeClip);
                UpdateContentSize();
                RefreshTrackBackgrounds();
                InvalidateGridCache();
                DrawGridLines(force: true);
                if (Math.Abs(_activeClip.DurationTicks - _dragStartDuration) > 0.01)
                    ClipResized?.Invoke(_activeClip, _dragStartDuration, _activeClip.DurationTicks);
            }

            _dragMode = DragMode.None;
            _activeClip = null;
            _isDragging = false;
            ClipLayer.ReleaseMouseCapture();
        }

        public void SetTimeSelection(Guid? clipId, double startSec, double endSec)
        {
            _rangeSelClipId = clipId;
            _rangeSelStartSec = startSec;
            _rangeSelEndSec = endSec;
            DrawTimeSelection();
        }

        public void ClearTimeSelection()
        {
            _rangeSelClipId = null;
            _rangeSelStartSec = -1;
            _rangeSelEndSec = -1;
            SelectionLayer.Children.Clear();
            _rangeSelectionRect = null;
        }

        private void UpdateRangeSelectionVisual()
        {
            if (_activeClip == null) return;
            double clipLeftPx = _activeClip.StartTick * _model.ZoomX;
            double y = _activeClip.TrackIndex * _model.TrackHeight + 3;
            double h = _model.TrackHeight - 6;
            double left = clipLeftPx + Math.Min(_selectStartLocalX, _selectEndLocalX);
            double width = Math.Abs(_selectEndLocalX - _selectStartLocalX);
            EnsureRangeRect();
            _rangeSelectionRect!.Width = Math.Max(2, width);
            _rangeSelectionRect.Height = h;
            Canvas.SetLeft(_rangeSelectionRect, left);
            Canvas.SetTop(_rangeSelectionRect, y);
            _rangeSelectionRect.Visibility = Visibility.Visible;
        }

        private void EnsureRangeRect()
        {
            if (_rangeSelectionRect != null) return;
            _rangeSelectionRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(60, 120, 129, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(180, 120, 129, 255)),
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2,
                IsHitTestVisible = false
            };
            SelectionLayer.Children.Add(_rangeSelectionRect);
        }

        private void DrawTimeSelection()
        {
            SelectionLayer.Children.Clear();
            _rangeSelectionRect = null;
            if (!_rangeSelClipId.HasValue || _rangeSelEndSec <= _rangeSelStartSec) return;

            var clip = _model.AudioClips.FirstOrDefault(c => c.Id == _rangeSelClipId.Value);
            if (clip == null) return;

            double leftPx = _model.SecondsToTick(_rangeSelStartSec) * _model.ZoomX;
            double rightPx = _model.SecondsToTick(_rangeSelEndSec) * _model.ZoomX;
            double y = clip.TrackIndex * _model.TrackHeight + 3;
            double h = _model.TrackHeight - 6;

            _rangeSelectionRect = new Rectangle
            {
                Width = Math.Max(2, rightPx - leftPx),
                Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(60, 120, 129, 255)),
                Stroke = new SolidColorBrush(Color.FromArgb(180, 120, 129, 255)),
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_rangeSelectionRect, leftPx);
            Canvas.SetTop(_rangeSelectionRect, y);
            SelectionLayer.Children.Add(_rangeSelectionRect);
        }

        public void SetZoom(double pixelsPerSecond)
        {
            double factor = 60.0 / (_model.Bpm * PlaylistViewModel.PPQN);
            _model.ZoomX = Math.Max(0.001, pixelsPerSecond * factor);

            InvalidateGridCache();
            _lastPlayheadX = double.NaN;
            UpdateContentSize();
            UpdateAllClipLayouts(layoutOnly: true);
            ScheduleZoomWaveformRedraw();
            if (_trackBgRects.Count == _model.NumTracks)
                UpdateTrackBackgroundWidths();
            else
                DrawTrackBackgrounds();
            DrawGridLines(force: true);
            UpdatePlayheadPosition();
            DrawTimeSelection();
        }

        #endregion

        #region Drop

        private void OnDrop(object sender, DragEventArgs e)
        {
            Point pos = e.GetPosition(this);
            double scrollX = MainScroller.HorizontalOffset;
            double tickPos = (pos.X + scrollX) / _model.ZoomX;
            int trackIndex = (int)(pos.Y / _model.TrackHeight);
            trackIndex = Math.Max(0, Math.Min(_model.NumTracks - 1, trackIndex));

            string? filePath = null;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                filePath = files?.FirstOrDefault();
            }
            else if (e.Data.GetDataPresent(typeof(AudioStudio.FileItem)))
            {
                var item = e.Data.GetData(typeof(AudioStudio.FileItem)) as AudioStudio.FileItem;
                filePath = item?.FullPath;
            }

            if (filePath != null)
            {
                var ext = System.IO.Path.GetExtension(filePath).ToLower();
                if (ext is ".wav" or ".mp3" or ".flac" or ".ogg" or ".aiff" or ".m4a")
                    FileDropped?.Invoke(filePath, tickPos, trackIndex);
            }
            e.Handled = true;
        }

        public TrackItemViewModel? AddClipFromFile(string filePath, double tickPos, int trackIndex)
        {
            try
            {
                using var reader = new NAudio.Wave.AudioFileReader(filePath);
                double durationSec = reader.TotalTime.TotalSeconds;
                double durationTicks = durationSec * _model.TicksPerSecond;

                int sampleRate = reader.WaveFormat.SampleRate;
                int channels = reader.WaveFormat.Channels;
                var peaks = ComputePeaksFromReader(reader, 5000);

                var clip = new TrackItemViewModel
                {
                    Name = System.IO.Path.GetFileName(filePath),
                    FilePath = filePath,
                    SampleRate = sampleRate,
                    Channels = channels,
                    SourceDurationSeconds = durationSec,
                    StartTick = _model.SnapToGrid(tickPos),
                    DurationTicks = Math.Max(TrackItemViewModel.PPQN / 4, durationTicks),
                    TrackIndex = trackIndex
                };

                _peaksCache[clip.Id] = peaks;
                _model.AudioClips.Add(clip);
                return clip;
            }
            catch { return null; }
        }

        private static float[] ComputePeaksFromReader(NAudio.Wave.AudioFileReader reader, int targetCount)
        {
            long totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
            if (totalSamples <= 0) return Array.Empty<float>();

            var peaks = new float[targetCount];
            var counts = new int[targetCount];
            var buf = new float[8192];
            int read;
            long sampleIndex = 0;

            while ((read = reader.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    int bucket = (int)((double)sampleIndex / totalSamples * targetCount);
                    if (bucket >= targetCount) bucket = targetCount - 1;
                    float abs = Math.Abs(buf[i]);
                    if (abs > peaks[bucket]) peaks[bucket] = abs;
                    counts[bucket]++;
                    sampleIndex++;
                }
            }
            return peaks;
        }

        #endregion
    }
}
