using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        public event Action<IReadOnlyList<(TrackItemViewModel Clip, double OldTick, int OldTrack, double NewTick, int NewTrack)>>? ClipsMoved;
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
        private readonly Dictionary<int, Rectangle> _trackBgByIndex = new();
        private readonly Dictionary<(Guid Id, int WidthKey), (Geometry Top, Geometry Bottom)> _waveformGeometryCache = new();
        private DispatcherTimer? _scrollSyncTimer;
        private const double ViewportMargin = 120;
        private const int TrackBgVirtualizeThreshold = 24;
        private Line? _playheadLine;
        private Rectangle? _playheadHitArea;
        private readonly TranslateTransform _playheadTransform = new();
        private double _playheadSeconds = 0;
        private double _lastPlayheadX = double.NaN;
        private bool _isDraggingPlayhead;
        private double _playheadDragStartX;
        private double _playheadDragStartSeconds;
        private Guid? _selectedClipId;
        private readonly HashSet<Guid> _selectedClipIds = new();

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
        private readonly HashSet<Guid> _moveClipIds = new();
        private readonly Dictionary<Guid, (double Tick, int Track)> _dragStartPositions = new();
        private bool _initialTrackLayoutDone;
        private DispatcherTimer? _zoomWaveformTimer;
        private double _stableViewportWidth = 800;
        private double _fixedViewportMinWidth = 800;
        private bool _deferredLayoutPending;
        private const double MinTimelineSeconds = 120;

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
            SizeChanged += (_, _) => RefreshViewportLayout();
            MainScroller.SizeChanged += (_, _) => RefreshViewportLayout();

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
                Width = 20,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeWE,
                IsHitTestVisible = true
            };
            _playheadHitArea.MouseLeftButtonDown += PlayheadHit_MouseLeftButtonDown;
            _playheadHitArea.MouseMove += PlayheadHit_MouseMove;
            _playheadHitArea.MouseLeftButtonUp += PlayheadHit_MouseLeftButtonUp;
            _playheadHitArea.MouseLeave += PlayheadHit_MouseLeave;

            PlayheadLayer.Children.Add(_playheadLine);
            PlayheadLayer.Children.Add(_playheadHitArea);

            Loaded += (_, _) =>
            {
                UpdateContentSize();
                SetPlayheadTime(0);
                TryEnsureInitialTrackCount();
            };

            ClipLayer.MouseMove += ClipLayer_PlayheadMouseMove;
            ClipLayer.MouseLeftButtonUp += ClipLayer_PlayheadMouseUp;
            ClipLayer.PreviewMouseLeftButtonDown += ClipLayer_PreviewMouseLeftButtonDown;
            ClipLayer.PreviewMouseRightButtonDown += ClipLayer_PreviewMouseRightButtonDown;
            MainScroller.PreviewMouseLeftButtonDown += MainScroller_PreviewMouseLeftButtonDown;
            MainScroller.PreviewMouseLeftButtonUp += MainScroller_PreviewMouseLeftButtonUp;
            MainScroller.MouseMove += ClipLayer_PlayheadMouseMove;
            MainScroller.MouseLeftButtonUp += ClipLayer_PlayheadMouseUp;
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

        /// <summary>Пересчёт ширины контента и сетки при изменении области просмотра (сплиттер, ресайз окна).</summary>
        public void RefreshViewportLayout()
        {
            TryEnsureInitialTrackCount();
            _fixedViewportMinWidth = Math.Max(_fixedViewportMinWidth, GetViewportWidth());
            RefreshContentLayout();
            ScheduleDeferredLayoutRefresh();
        }

        private void ScheduleDeferredLayoutRefresh()
        {
            if (_deferredLayoutPending) return;
            _deferredLayoutPending = true;
            Dispatcher.BeginInvoke(() =>
            {
                _deferredLayoutPending = false;
                if (!IsLoaded) return;
                RefreshContentLayout();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Синхронизация ширины таймлайна, фонов дорожек и визуалов клипов.</summary>
        public void RefreshContentLayout(IReadOnlySet<Guid>? fullRedrawIds = null)
        {
            fullRedrawIds ??= new HashSet<Guid>();
            UpdateContentSize();
            DrawTrackBackgrounds();
            DrawGridLines(force: true);
            foreach (var clip in _model.AudioClips)
            {
                bool full = fullRedrawIds.Contains(clip.Id);
                UpdateClipVisual(clip, layoutOnly: !full);
            }
            SyncVisibleClips();
            UpdatePlayheadPosition();
        }

        public void InsertClip(TrackItemViewModel clip, float[] peaks)
        {
            _peaksCache[clip.Id] = peaks;
            _model.AudioClips.Add(clip);
            // Layout после Add: иначе при drop в начало ширина «залипает» по клипу.
            RefreshContentLayout(new HashSet<Guid> { clip.Id });
            ScheduleDeferredLayoutRefresh();
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
                if (_selectedClipId == c.Id) { _selectedClipId = null; _selectedClipIds.Remove(c.Id); }
                _peaksCache.Remove(c.Id);
                _model.AudioClips.Remove(c);
            }
            _model.NumTracks--;
            _needsRebuild = true;
            RebuildIfNeeded();
        }

        public Guid? SelectedClipId => _selectedClipId;

        public IReadOnlyList<TrackItemViewModel> GetSelectedClips()
        {
            PruneClipSelection();
            return _model.AudioClips.Where(c => _selectedClipIds.Contains(c.Id))
                .OrderBy(c => c.StartTick)
                .ToList();
        }

        public int SelectedClipCount => GetSelectedClips().Count;

        public TrackItemViewModel? GetSelectedClip()
        {
            if (_selectedClipId.HasValue)
            {
                var primary = _model.AudioClips.FirstOrDefault(c => c.Id == _selectedClipId.Value);
                if (primary != null) return primary;
            }

            return _model.AudioClips.FirstOrDefault(c => _selectedClipIds.Contains(c.Id));
        }

        private void PruneClipSelection()
        {
            _selectedClipIds.RemoveWhere(id => _model.AudioClips.All(c => c.Id != id));
            if (_selectedClipId.HasValue && _model.AudioClips.All(c => c.Id != _selectedClipId.Value))
                _selectedClipId = _selectedClipIds.FirstOrDefault();
            UpdateSelectionVisuals();
        }

        public void SelectClip(Guid? clipId, bool raiseEvent = true, bool additive = false, bool preserveMultiSelection = false)
        {
            if (!clipId.HasValue)
            {
                _selectedClipIds.Clear();
                _selectedClipId = null;
                UpdateSelectionVisuals();
                return;
            }

            if (preserveMultiSelection && _selectedClipIds.Contains(clipId.Value) && _selectedClipIds.Count > 1)
            {
                _selectedClipId = clipId.Value;
                UpdateSelectionVisuals();
                if (raiseEvent)
                {
                    var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId.Value);
                    if (clip != null) ClipSelected?.Invoke(clip);
                }
                return;
            }

            if (!additive || !clipId.HasValue)
            {
                _selectedClipIds.Clear();
                if (clipId.HasValue)
                    _selectedClipIds.Add(clipId.Value);
            }
            else if (_selectedClipIds.Contains(clipId.Value))
            {
                _selectedClipIds.Remove(clipId.Value);
                _selectedClipId = _selectedClipIds.Count > 0 ? _selectedClipIds.First() : null;
                UpdateSelectionVisuals();
                return;
            }
            else
            {
                _selectedClipIds.Add(clipId.Value);
            }

            _selectedClipId = clipId;
            UpdateSelectionVisuals();
            if (raiseEvent && clipId.HasValue)
            {
                var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId.Value);
                if (clip != null) ClipSelected?.Invoke(clip);
            }
        }

        public void SelectClips(IEnumerable<Guid> clipIds, bool raiseEvent = false)
        {
            _selectedClipIds.Clear();
            foreach (var id in clipIds)
                _selectedClipIds.Add(id);
            _selectedClipId = _selectedClipIds.FirstOrDefault();
            UpdateSelectionVisuals();
            if (raiseEvent && _selectedClipId.HasValue)
            {
                var clip = _model.AudioClips.FirstOrDefault(c => c.Id == _selectedClipId.Value);
                if (clip != null) ClipSelected?.Invoke(clip);
            }
        }

        public void ClearClipSelection()
        {
            _selectedClipIds.Clear();
            _selectedClipId = null;
            UpdateSelectionVisuals();
        }

        public void RemoveClip(Guid clipId)
        {
            var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (clip == null)
            {
                DetachAllClipVisuals(clipId);
                PurgeOrphanClipVisuals();
                SyncVisibleClips();
                return;
            }

            if (_activeClip?.Id == clipId)
                CancelActiveInteraction(revertChanges: true);

            _selectedClipIds.Remove(clipId);
            if (_selectedClipId == clipId)
                _selectedClipId = _selectedClipIds.FirstOrDefault();
            if (_rangeSelClipId == clipId)
                ClearTimeSelection();
            _peaksCache.Remove(clipId);
            ClearWaveformCache(clipId);

            DetachAllClipVisuals(clipId);
            _model.AudioClips.Remove(clip);

            if (_activeClip?.Id == clipId)
            {
                _activeClip = null;
                _dragMode = DragMode.None;
                _isDragging = false;
                if (ClipLayer.IsMouseCaptured)
                    ClipLayer.ReleaseMouseCapture();
            }

            PurgeOrphanClipVisuals();
            SyncVisibleClips();
            DrawTimeSelection();
            UpdateContentSize();
            DrawTrackBackgrounds();
            DrawGridLines();
            ScheduleDeferredLayoutRefresh();
        }

        private void DetachAllClipVisuals(Guid clipId)
        {
            _clipVisuals.Remove(clipId);
            foreach (var child in ClipLayer.Children.OfType<Border>().ToList())
            {
                if (child.Tag is Guid id && id == clipId)
                    ClipLayer.Children.Remove(child);
            }
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
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (TrackItemViewModel clip in e.NewItems)
                    InsertClipVisual(clip);
                return;
            }

            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TrackItemViewModel clip in e.OldItems)
                    DetachAllClipVisuals(clip.Id);
                PurgeOrphanClipVisuals();
                SyncVisibleClips();
                DrawTimeSelection();
                UpdateContentSize();
                DrawTrackBackgrounds();
                DrawGridLines();
                ScheduleDeferredLayoutRefresh();
                return;
            }

            _needsRebuild = true;
            RebuildIfNeeded();
        }

        private void RemoveClipVisual(Guid clipId) => DetachAllClipVisuals(clipId);

        private void PurgeOrphanClipVisuals()
        {
            foreach (var child in ClipLayer.Children.OfType<Border>().ToList())
            {
                if (child.Tag is not Guid id || _model.AudioClips.Any(c => c.Id == id))
                    continue;
                ClipLayer.Children.Remove(child);
                _clipVisuals.Remove(id);
            }
        }

        public void InsertClipVisual(TrackItemViewModel clip)
        {
            if (_clipVisuals.ContainsKey(clip.Id))
                return;

            if (IsClipInViewport(clip))
            {
                var border = CreateClipVisual(clip);
                _clipVisuals[clip.Id] = border;
                ClipLayer.Children.Add(border);
                EnsurePlayheadHitAreaOnTop();
            }
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            ScrollUpdated?.Invoke(this, EventArgs.Empty);
            if (e.VerticalChange != 0 && _model.NumTracks > TrackBgVirtualizeThreshold)
                SyncVisibleTrackBackgrounds();
            ScheduleScrollSync();
        }

        private void ScheduleScrollSync()
        {
            if (_scrollSyncTimer == null)
            {
                _scrollSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _scrollSyncTimer.Tick += (_, _) =>
                {
                    _scrollSyncTimer!.Stop();
                    DrawGridLines();
                    SyncVisibleClips();
                };
            }
            if (!_scrollSyncTimer.IsEnabled)
                _scrollSyncTimer.Start();
        }

        private (double minX, double maxX, double minY, double maxY) GetVisibleViewportPx()
        {
            double scrollX = MainScroller.HorizontalOffset;
            double scrollY = MainScroller.VerticalOffset;
            double vpW = MainScroller.ViewportWidth;
            double vpH = MainScroller.ViewportHeight;
            if (vpW <= 0 || double.IsNaN(vpW)) vpW = ActualWidth;
            if (vpH <= 0 || double.IsNaN(vpH)) vpH = ActualHeight;
            return (scrollX - ViewportMargin, scrollX + vpW + ViewportMargin,
                scrollY - ViewportMargin, scrollY + vpH + ViewportMargin);
        }

        private bool IsClipInViewport(TrackItemViewModel clip)
        {
            var (minX, maxX, minY, maxY) = GetVisibleViewportPx();
            double left = clip.StartTick * _model.ZoomX;
            double right = left + clip.DurationTicks * _model.ZoomX;
            double top = clip.TrackIndex * _model.TrackHeight;
            double bottom = top + _model.TrackHeight;
            return right >= minX && left <= maxX && bottom >= minY && top <= maxY;
        }

        private bool ShouldKeepClipVisual(Guid clipId)
        {
            var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return false;
            if (_activeClip?.Id == clipId && _isDragging) return true;
            if (_moveClipIds.Contains(clipId) && _isDragging) return true;
            return IsClipInViewport(clip);
        }

        private void SyncVisibleClips()
        {
            var toRemove = _clipVisuals.Keys.Where(id => !ShouldKeepClipVisual(id)).ToList();
            foreach (var id in toRemove)
            {
                if (_clipVisuals.TryGetValue(id, out var border))
                {
                    ClipLayer.Children.Remove(border);
                    _clipVisuals.Remove(id);
                }
            }

            foreach (var clip in _model.AudioClips)
            {
                if (_clipVisuals.ContainsKey(clip.Id)) continue;
                if (!IsClipInViewport(clip)) continue;
                var border = CreateClipVisual(clip);
                _clipVisuals[clip.Id] = border;
                ClipLayer.Children.Add(border);
            }

            EnsurePlayheadHitAreaOnTop();
        }

        private void EnsurePlayheadHitAreaOnTop()
        {
            if (_playheadHitArea == null || !PlayheadLayer.Children.Contains(_playheadHitArea)) return;
            PlayheadLayer.Children.Remove(_playheadHitArea);
            PlayheadLayer.Children.Add(_playheadHitArea);
        }

        private void TryEnsureInitialTrackCount()
        {
            if (_initialTrackLayoutDone) return;
            if (_model.AudioClips.Count > 0)
            {
                _initialTrackLayoutDone = true;
                return;
            }

            double vpH = MainScroller.ViewportHeight;
            if (vpH <= 0 || double.IsNaN(vpH))
                vpH = MainScroller.ActualHeight;
            if (vpH <= 0 || double.IsNaN(vpH)) return;

            const int defaultTracks = 8;
            int target = Math.Max(defaultTracks, (int)Math.Ceiling(vpH / _model.TrackHeight));
            if (_model.NumTracks != target)
            {
                _model.NumTracks = target;
                _needsRebuild = true;
                RebuildIfNeeded();
            }

            _initialTrackLayoutDone = true;
        }

        private static bool IsScrollBarPart(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ScrollBar) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void RebuildIfNeeded()
        {
            if (!_needsRebuild) return;

            CancelActiveInteraction(revertChanges: true);

            _needsRebuild = false;
            TrackBgLayer.Children.Clear();
            _trackBgByIndex.Clear();
            ClipLayer.Children.Clear();
            _clipVisuals.Clear();
            if (_playheadHitArea != null && !PlayheadLayer.Children.Contains(_playheadHitArea))
                PlayheadLayer.Children.Add(_playheadHitArea);

            UpdateContentSize();
            DrawTrackBackgrounds();

            SyncVisibleClips();

            DrawGridLines();
            if (_playheadSeconds >= 0)
                UpdatePlayheadPosition();
            RefreshTimeSelectionAfterRebuild();
            ScheduleDeferredLayoutRefresh();
        }

        private void RefreshTimeSelectionAfterRebuild()
        {
            if (_rangeSelClipId.HasValue
                && _model.AudioClips.All(c => c.Id != _rangeSelClipId.Value))
            {
                ClearTimeSelection();
                return;
            }

            DrawTimeSelection();
        }

        private static bool IsClipBorder(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Border { Tag: Guid })
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private double GetViewportWidth()
        {
            double vpW = MainScroller.ViewportWidth;
            if (vpW <= 0 || double.IsNaN(vpW))
                vpW = MainScroller.ActualWidth;
            if (vpW <= 0 || double.IsNaN(vpW))
                vpW = ActualWidth;
            if (vpW <= 0 || double.IsNaN(vpW))
                vpW = _stableViewportWidth;

            if (vpW > 100 && !double.IsNaN(vpW) && !double.IsInfinity(vpW))
            {
                _stableViewportWidth = vpW;
                _fixedViewportMinWidth = Math.Max(_fixedViewportMinWidth, vpW);
            }

            return Math.Max(vpW, _fixedViewportMinWidth);
        }

        private double GetEffectiveZoom()
        {
            double zoom = _model.ZoomX;
            if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom < 0.001)
                zoom = 0.001;
            return zoom;
        }

        private double GetContentWidthPx()
        {
            double zoom = GetEffectiveZoom();
            double vpW = Math.Max(GetViewportWidth(), _fixedViewportMinWidth);
            double timelineMinW = Math.Max(400, MinTimelineSeconds * _model.TicksPerSecond * zoom);

            // Ширина таймлайна НИКОГДА не меньше окна и 120 сек — не зависит от позиции клипа.
            double w = Math.Max(vpW, timelineMinW);
            foreach (var clip in _model.AudioClips)
                w = Math.Max(w, clip.EndTick * zoom + 200);

            return w;
        }

        private void ForceTrackBackgroundWidths(double w)
        {
            foreach (var rect in _trackBgByIndex.Values)
            {
                rect.Width = w;
                Canvas.SetLeft(rect, 0);
            }
        }

        private double GetViewportHeight()
        {
            double vpH = MainScroller.ViewportHeight;
            if (vpH <= 0 || double.IsNaN(vpH))
                vpH = MainScroller.ActualHeight;
            if (vpH <= 0 || double.IsNaN(vpH))
                vpH = ActualHeight;
            return vpH;
        }

        private void ApplyLayerSizes(double contentW, double contentH)
        {
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

        /// <summary>Длительность видимой области таймлайна (для перемещения playhead без клипов).</summary>
        public double GetTimelineDurationSeconds()
        {
            double pps = PixelsPerSecond;
            if (pps <= 1e-6) return 10;
            double contentSec = GetContentWidthPx() / pps;
            double viewportSec = GetViewportWidth() / pps;
            return Math.Max(Math.Max(contentSec, viewportSec), 10);
        }

        private void UpdateContentSize()
        {
            double vpW = Math.Max(GetViewportWidth(), _fixedViewportMinWidth);
            double contentW = Math.Max(GetContentWidthPx(), vpW);

            double contentH = _model.TotalHeight;
            double vpH = GetViewportHeight();
            if (vpH > contentH)
                contentH = vpH;
            if (contentH < 1) contentH = 200;
            if (double.IsNaN(contentH) || double.IsInfinity(contentH)) contentH = 200;

            ScrollContentHost.MinWidth = vpW;
            ScrollContentHost.Width = contentW;
            ContentGrid.MinWidth = vpW;
            ContentGrid.Width = contentW;
            ContentGrid.Height = contentH;
            ApplyLayerSizes(contentW, contentH);
            ForceTrackBackgroundWidths(contentW);

            ScrollContentHost.InvalidateMeasure();
            MainScroller.InvalidateMeasure();
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
            if (_dragMode != DragMode.None)
                return;

            if (_playheadSeconds < 0)
                _playheadSeconds = 0;
            _isDraggingPlayhead = true;
            _playheadDragStartX = e.GetPosition(ClipLayer).X;
            _playheadDragStartSeconds = _playheadSeconds;
            Mouse.OverrideCursor = Cursors.SizeWE;
            MainScroller.CaptureMouse();
            e.Handled = true;
        }

        private void ClipLayer_PlayheadMouseMove(object sender, MouseEventArgs e) =>
            PlayheadHit_MouseMove(sender, e);

        private void ClipLayer_PlayheadMouseUp(object sender, MouseButtonEventArgs e) =>
            PlayheadHit_MouseLeftButtonUp(sender, e);

        private void PlayheadHit_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingPlayhead || e.LeftButton != MouseButtonState.Pressed) return;
            double x = Math.Max(0, e.GetPosition(ClipLayer).X);
            double seconds = PixelXToSeconds(x);
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
            Mouse.OverrideCursor = null;
            if (ClipLayer.IsMouseCaptured)
                ClipLayer.ReleaseMouseCapture();
            if (MainScroller.IsMouseCaptured)
                MainScroller.ReleaseMouseCapture();
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
            SyncVisibleTrackBackgrounds(forceAll: true);
        }

        private void SyncVisibleTrackBackgrounds(bool forceAll = false)
        {
            double w = GetContentWidthPx();

            if (forceAll || _model.NumTracks <= TrackBgVirtualizeThreshold)
            {
                foreach (var kvp in _trackBgByIndex.ToList())
                {
                    kvp.Value.Width = w;
                    Canvas.SetLeft(kvp.Value, 0);
                }
            }

            int minTrack = 0;
            int maxTrack = _model.NumTracks - 1;
            if (!forceAll && _model.NumTracks > TrackBgVirtualizeThreshold)
            {
                var (_, _, minY, maxY) = GetVisibleViewportPx();
                minTrack = Math.Max(0, (int)Math.Floor(minY / _model.TrackHeight));
                maxTrack = Math.Min(_model.NumTracks - 1, (int)Math.Ceiling(maxY / _model.TrackHeight));
            }

            foreach (var i in _trackBgByIndex.Keys.Where(i => i < minTrack || i > maxTrack).ToList())
            {
                if (_trackBgByIndex.TryGetValue(i, out var rect))
                {
                    TrackBgLayer.Children.Remove(rect);
                    _trackBgByIndex.Remove(i);
                }
            }

            for (int i = minTrack; i <= maxTrack; i++)
            {
                if (_trackBgByIndex.TryGetValue(i, out var existing))
                {
                    existing.Width = w;
                    Canvas.SetLeft(existing, 0);
                    continue;
                }

                var rect = new Rectangle
                {
                    Width = w,
                    Height = _model.TrackHeight,
                    Fill = new SolidColorBrush(i % 2 == 0
                        ? Color.FromRgb(28, 28, 32)
                        : Color.FromRgb(22, 22, 26)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, 0);
                Canvas.SetTop(rect, i * _model.TrackHeight);
                _trackBgByIndex[i] = rect;
                TrackBgLayer.Children.Add(rect);
            }
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

            Color clipColor = ParseClipColor(clip);

            var border = new Border
            {
                Width = Math.Max(4, w),
                Height = Math.Max(4, h),
                CornerRadius = new CornerRadius(2),
                BorderBrush = NormalBorderBrush,
                BorderThickness = new Thickness(0.5),
                Background = CreateClipBackgroundBrush(clipColor, selected: false),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Tag = clip.Id
            };

            var inner = new Grid();

            var contentCanvas = new Canvas
            {
                Width = Math.Max(4, w),
                Height = h,
                ClipToBounds = true,
                IsHitTestVisible = false
            };
            DrawClipContent(contentCanvas, clip, w, h);
            inner.Children.Add(contentCanvas);

            var text = new TextBlock
            {
                Text = clip.Name,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 2, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(text, 1);
            inner.Children.Add(text);

            var grip = new Rectangle
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeWE,
                IsHitTestVisible = true,
                ToolTip = "Изменить длину"
            };
            Panel.SetZIndex(grip, 2);
            inner.Children.Add(grip);
            border.Child = inner;

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            Panel.SetZIndex(border, 100);

            border.MouseLeftButtonDown += Clip_MouseLeftButtonDown;
            border.MouseRightButtonDown += Clip_MouseRightButtonDown;
            ApplySelectionStyle(border, _selectedClipIds.Contains(clip.Id));
            return border;
        }

        private static readonly SolidColorBrush SelectedBorderBrush =
            new(Color.FromRgb(120, 129, 255));
        private static readonly SolidColorBrush NormalBorderBrush =
            new(Color.FromArgb(60, 180, 180, 180));

        private static SolidColorBrush CreateClipBackgroundBrush(Color clipColor, bool selected)
        {
            byte alpha = selected ? (byte)95 : (byte)50;
            var brush = new SolidColorBrush(Color.FromArgb(alpha, clipColor.R, clipColor.G, clipColor.B));
            brush.Freeze();
            return brush;
        }

        private static Color ParseClipColor(TrackItemViewModel clip)
        {
            try { return (Color)ColorConverter.ConvertFromString(clip.Color)!; }
            catch { return Color.FromRgb(120, 129, 255); }
        }

        private void ApplySelectionStyle(Border border, bool selected)
        {
            border.BorderBrush = selected ? SelectedBorderBrush : NormalBorderBrush;
            border.BorderThickness = selected ? new Thickness(2) : new Thickness(0.5);

            if (border.Tag is Guid clipId)
            {
                var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
                if (clip != null)
                    border.Background = CreateClipBackgroundBrush(ParseClipColor(clip), selected);
            }
        }

        private void UpdateSelectionVisuals()
        {
            foreach (var kvp in _clipVisuals)
                ApplySelectionStyle(kvp.Value, _selectedClipIds.Contains(kvp.Key));
        }

        private void DrawClipContent(Canvas canvas, TrackItemViewModel clip, double width, double height)
        {
            canvas.Children.Clear();
            if (_peaksCache.TryGetValue(clip.Id, out var peaks) && peaks.Length > 0)
            {
                if (_showSpectrogramInClip)
                    DrawSpectrogramGraph(canvas, peaks, width, height);
                else
                    DrawWaveformInClip(canvas, peaks, width, height, clip.Id);
            }
        }

        private void ClearWaveformCache(Guid clipId)
        {
            foreach (var key in _waveformGeometryCache.Keys.Where(k => k.Id == clipId).ToList())
                _waveformGeometryCache.Remove(key);
        }

        private static double ComputePeakScale(float[] peaks, double centerY, double fillRatio = 0.88)
        {
            float maxPeak = 0;
            for (int i = 0; i < peaks.Length; i++)
                if (peaks[i] > maxPeak) maxPeak = peaks[i];
            double amplitude = centerY * fillRatio;
            return maxPeak > 1e-6f ? amplitude / maxPeak : amplitude;
        }

        private void DrawWaveformInClip(Canvas canvas, float[] peaks, double width, double height, Guid clipId)
        {
            canvas.Children.Clear();
            if (peaks.Length == 0 || width < 2 || height < 2) return;

            int widthKey = Math.Max(10, (int)Math.Round(width));
            var cacheKey = (clipId, widthKey);
            if (Math.Abs(width - widthKey) > 0.5
                || !_waveformGeometryCache.TryGetValue(cacheKey, out var geoms))
            {
                geoms = BuildWaveformGeometries(peaks, width, height);
                if (Math.Abs(width - widthKey) <= 0.5)
                {
                    geoms.Top.Freeze();
                    geoms.Bottom.Freeze();
                    _waveformGeometryCache[cacheKey] = geoms;
                }
            }

            var fillBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            fillBrush.Freeze();

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geoms.Top,
                Fill = fillBrush,
                Opacity = 0.75
            });
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geoms.Bottom,
                Fill = fillBrush,
                Opacity = 0.45
            });
        }

        private static (Geometry Top, Geometry Bottom) BuildWaveformGeometries(float[] peaks, double width, double height)
        {
            int n = Math.Min(peaks.Length, Math.Max(10, (int)width));
            double xStep = width / n;
            double centerY = height / 2;
            double scale = ComputePeakScale(peaks, centerY);

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

            return (geom, geom2);
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
            if (!_clipVisuals.TryGetValue(clip.Id, out var border))
            {
                if (!IsClipInViewport(clip) && !(_isDragging && (_activeClip?.Id == clip.Id || _moveClipIds.Contains(clip.Id))))
                    return;
                border = CreateClipVisual(clip);
                _clipVisuals[clip.Id] = border;
                ClipLayer.Children.Add(border);
                EnsurePlayheadHitAreaOnTop();
                layoutOnly = false;
            }
            double w = Math.Max(4, clip.DurationTicks * _model.ZoomX);
            double h = _model.TrackHeight - 6;
            border.Width = w;
            border.Height = Math.Max(4, h);
            Canvas.SetLeft(border, clip.StartTick * _model.ZoomX);
            Canvas.SetTop(border, clip.TrackIndex * _model.TrackHeight + 3);

            if (border.Child is Grid inner && inner.Children.Count > 0 && inner.Children[0] is Canvas contentCanvas)
            {
                double drawW = Math.Max(4, w);
                bool sizeChanged = Math.Abs(contentCanvas.Width - drawW) > 0.5
                                   || Math.Abs(contentCanvas.Height - h) > 0.5;
                contentCanvas.Width = drawW;
                contentCanvas.Height = h;
                if (!layoutOnly || sizeChanged)
                    DrawClipContent(contentCanvas, clip, drawW, h);
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
                    foreach (var kvp in _clipVisuals.ToList())
                    {
                        var clip = _model.AudioClips.FirstOrDefault(c => c.Id == kvp.Key);
                        if (clip != null)
                            UpdateClipVisual(clip, layoutOnly: false);
                    }
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

        private bool HitTestClipAt(Point posInClipLayer)
        {
            return HitTestClipModelAt(posInClipLayer) != null;
        }

        private TrackItemViewModel? HitTestClipModelAt(Point posInClipLayer)
        {
            TrackItemViewModel? hit = null;
            foreach (var clip in _model.AudioClips)
            {
                double left = clip.StartTick * _model.ZoomX;
                double top = clip.TrackIndex * _model.TrackHeight + 3;
                double w = Math.Max(4, clip.DurationTicks * _model.ZoomX);
                double h = _model.TrackHeight - 6;
                if (posInClipLayer.X >= left && posInClipLayer.X <= left + w
                    && posInClipLayer.Y >= top && posInClipLayer.Y <= top + h)
                    hit = clip;
            }
            return hit;
        }

        private void ClipLayer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(ClipLayer);
            var clip = HitTestClipModelAt(pos);
            if (clip == null) return;

            HandleClipMouseDown(clip, pos, e);
            e.Handled = true;
        }

        private void ClipLayer_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(ClipLayer);
            var clip = HitTestClipModelAt(pos);
            if (clip == null) return;

            SelectClip(clip.Id, raiseEvent: true, preserveMultiSelection: true);
            ClipContextMenuRequested?.Invoke(clip);
            e.Handled = true;
        }

        private void HandleClipMouseDown(TrackItemViewModel clip, Point pos, MouseButtonEventArgs e)
        {
            _activeClip = clip;
            var clipId = clip.Id;

            double clipLeftPx = clip.StartTick * _model.ZoomX;
            double clipRightPx = clipLeftPx + clip.DurationTicks * _model.ZoomX;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SelectClip(clipId, raiseEvent: true, additive: false);
                _dragMode = DragMode.SelectRange;
                _selectStartLocalX = Math.Clamp(pos.X - clipLeftPx, 0, clipRightPx - clipLeftPx);
                _selectEndLocalX = _selectStartLocalX;
                _dragStartPoint = pos;
                _isDragging = false;
                UpdateRangeSelectionVisual();
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _selectedClipId.HasValue)
            {
                var anchor = _model.AudioClips.FirstOrDefault(c => c.Id == _selectedClipId.Value);
                if (anchor != null && anchor.TrackIndex == clip.TrackIndex)
                {
                    double minTick = Math.Min(anchor.StartTick, clip.StartTick);
                    double maxTick = Math.Max(anchor.EndTick, clip.EndTick);
                    var ids = _model.AudioClips
                        .Where(c => c.TrackIndex == anchor.TrackIndex
                            && c.StartTick < maxTick - 0.01
                            && c.EndTick > minTick + 0.01)
                        .Select(c => c.Id)
                        .ToList();
                    if (ids.Count > 0)
                        SelectClips(ids, raiseEvent: true);
                    _dragMode = DragMode.None;
                    return;
                }
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (_selectedClipIds.Contains(clipId) && _selectedClipIds.Count > 1)
                {
                    _selectedClipId = clipId;
                    UpdateSelectionVisuals();
                    ClipSelected?.Invoke(clip);
                }
                else
                {
                    SelectClip(clipId, raiseEvent: true, additive: true);
                    _dragMode = DragMode.None;
                    return;
                }
            }
            else if (_selectedClipIds.Contains(clipId) && _selectedClipIds.Count > 1)
            {
                _selectedClipId = clipId;
                UpdateSelectionVisuals();
                ClipSelected?.Invoke(clip);
            }
            else
            {
                SelectClip(clipId, raiseEvent: true, additive: false);
            }

            _dragMode = Math.Abs(pos.X - clipRightPx) < 8 ? DragMode.ResizeRight : DragMode.Move;
            _dragStartPoint = pos;
            _dragStartTick = clip.StartTick;
            _dragStartTrack = clip.TrackIndex;
            _dragStartDuration = clip.DurationTicks;
            _isDragging = false;
            PrepareMoveDrag(clip);
        }

        private void PrepareMoveDrag(TrackItemViewModel clip)
        {
            _moveClipIds.Clear();
            _dragStartPositions.Clear();

            if (_dragMode == DragMode.Move && _selectedClipIds.Count > 1 && _selectedClipIds.Contains(clip.Id))
            {
                foreach (var id in _selectedClipIds)
                {
                    var selected = _model.AudioClips.FirstOrDefault(c => c.Id == id);
                    if (selected == null) continue;
                    _moveClipIds.Add(id);
                    _dragStartPositions[id] = (selected.StartTick, selected.TrackIndex);
                }
            }
            else
            {
                _moveClipIds.Add(clip.Id);
                _dragStartPositions[clip.Id] = (clip.StartTick, clip.TrackIndex);
            }
        }

        private List<(Guid Id, double StartTick, int StartTrack, double DurationTicks)> GetMoveDragSnapshot()
        {
            var moving = new List<(Guid Id, double StartTick, int StartTrack, double DurationTicks)>();
            foreach (var id in _moveClipIds)
            {
                var movingClip = _model.AudioClips.FirstOrDefault(c => c.Id == id);
                if (movingClip == null || !_dragStartPositions.TryGetValue(id, out var start))
                    continue;
                moving.Add((id, start.Tick, start.Track, movingClip.DurationTicks));
            }
            return moving;
        }

        private void ApplyMoveDelta(double tickDelta, int trackDelta)
        {
            var moving = GetMoveDragSnapshot();
            if (moving.Count == 0) return;

            tickDelta = _model.ClampGroupTickDelta(moving, tickDelta, trackDelta);

            foreach (var id in _moveClipIds)
            {
                var movingClip = _model.AudioClips.FirstOrDefault(c => c.Id == id);
                if (movingClip == null) continue;
                var (startTick, startTrack) = _dragStartPositions[id];
                movingClip.StartTick = Math.Max(0, startTick + tickDelta);
                movingClip.TrackIndex = Math.Clamp(startTrack + trackDelta, 0, Math.Max(0, _model.NumTracks - 1));
                UpdateClipVisual(movingClip, layoutOnly: true);
            }
        }

        private void RevertMoveDrag()
        {
            foreach (var id in _moveClipIds)
            {
                var movingClip = _model.AudioClips.FirstOrDefault(c => c.Id == id);
                if (movingClip == null || !_dragStartPositions.TryGetValue(id, out var start)) continue;
                movingClip.StartTick = start.Tick;
                movingClip.TrackIndex = start.Track;
                UpdateClipVisual(movingClip);
            }
        }

        private void CommitMoveDrag()
        {
            if (_activeClip == null || _moveClipIds.Count == 0) return;

            int trackDelta = _activeClip.TrackIndex - _dragStartTrack;
            var moving = GetMoveDragSnapshot();
            double tickDelta = _activeClip.StartTick - _dragStartTick;
            tickDelta = _model.SnapToGrid(_dragStartTick + tickDelta) - _dragStartTick;
            tickDelta = _model.ClampGroupTickDelta(moving, tickDelta, trackDelta);

            var excludeIds = _moveClipIds.ToList();
            var moves = new List<(TrackItemViewModel Clip, double OldTick, int OldTrack, double NewTick, int NewTrack)>();

            foreach (var id in _moveClipIds.OrderBy(i => _dragStartPositions.TryGetValue(i, out var s) ? s.Tick : 0))
            {
                var movingClip = _model.AudioClips.FirstOrDefault(c => c.Id == id);
                if (movingClip == null || !_dragStartPositions.TryGetValue(id, out var start)) continue;

                double preferredTick = Math.Max(0, start.Tick + tickDelta);
                int preferredTrack = Math.Clamp(start.Track + trackDelta, 0, Math.Max(0, _model.NumTracks - 1));
                _model.ResolveClipPlacement(movingClip, preferredTick, preferredTrack, excludeIds);
                UpdateClipVisual(movingClip);

                if (Math.Abs(movingClip.StartTick - start.Tick) > 0.01 || movingClip.TrackIndex != start.Track)
                {
                    moves.Add((movingClip, start.Tick, start.Track, movingClip.StartTick, movingClip.TrackIndex));
                }
            }

            RefreshContentLayout(_moveClipIds);

            if (moves.Count == 1)
            {
                var m = moves[0];
                ClipMoved?.Invoke(m.Clip, m.OldTick, m.OldTrack, m.NewTick, m.NewTrack);
            }
            else if (moves.Count > 1)
            {
                ClipsMoved?.Invoke(moves);
            }
        }

        private void MainScroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsScrollBarPart(e.OriginalSource as DependencyObject))
                return;

            if (IsClipBorder(e.OriginalSource as DependencyObject))
                return;

            if (_dragMode != DragMode.None)
                return;

            var posOnClip = e.GetPosition(ClipLayer);
            if (posOnClip.X < 0 || posOnClip.Y < 0
                || posOnClip.X > ClipLayer.ActualWidth || posOnClip.Y > ClipLayer.ActualHeight)
                return;

            if (HitTestClipAt(posOnClip))
                return;

            var (track, tick) = GetEmptyAreaHit(posOnClip);
            double seconds = _model.TickToSeconds(tick);

            double playheadX = _model.SecondsToTick(Math.Max(0, _playheadSeconds)) * _model.ZoomX;
            if (Math.Abs(posOnClip.X - playheadX) <= 10)
            {
                PlayheadHit_MouseLeftButtonDown(_playheadHitArea!, e);
                e.Handled = true;
                return;
            }

            EmptyAreaInteracted?.Invoke(track, tick, false);
            if (Keyboard.Modifiers == ModifierKeys.Control) return;

            SetPlayheadTime(seconds);
            SeekRequested?.Invoke(seconds);
            e.Handled = true;
        }

        private void MainScroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsScrollBarPart(e.OriginalSource as DependencyObject))
                return;
            if (e.ChangedButton != MouseButton.Left) return;
            if (_dragMode != DragMode.None)
                EndDrag();
            EndPlayheadDrag();
        }

        private void ClipLayer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(ClipLayer);
            if (HitTestClipAt(pos))
                return;

            var (track, tick) = GetEmptyAreaHit(pos);
            EmptyAreaInteracted?.Invoke(track, tick, true);
            e.Handled = true;
        }

        private void Clip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Guid clipId)
            {
                var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
                if (clip != null)
                {
                    HandleClipMouseDown(clip, e.GetPosition(ClipLayer), e);
                    e.Handled = true;
                    return;
                }

                RemoveClipVisual(clipId);
                PurgeOrphanClipVisuals();
                e.Handled = false;
                return;
            }
            e.Handled = true;
        }

        private void Clip_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: Guid clipId })
            {
                var clip = _model.AudioClips.FirstOrDefault(c => c.Id == clipId);
                if (clip != null)
                {
                    SelectClip(clipId, raiseEvent: true, preserveMultiSelection: true);
                    ClipContextMenuRequested?.Invoke(clip);
                    e.Handled = true;
                    return;
                }

                RemoveClipVisual(clipId);
                PurgeOrphanClipVisuals();
                e.Handled = false;
                return;
            }
            e.Handled = true;
        }

        private void ClipLayer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_activeClip == null || _dragMode == DragMode.None) return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_isDragging)
                    CancelActiveInteraction(revertChanges: true);
                else
                    CancelActiveInteraction(revertChanges: false);
                return;
            }

            Point pos = e.GetPosition(ClipLayer);
            double dx = pos.X - _dragStartPoint.X;
            double dy = pos.Y - _dragStartPoint.Y;

            if (_dragMode == DragMode.SelectRange)
            {
                if (!_isDragging && (Math.Abs(dx) > 2 || Math.Abs(dy) > 2))
                {
                    _isDragging = true;
                    ClipLayer.CaptureMouse();
                }
                double clipLeftPx = _activeClip.StartTick * _model.ZoomX;
                double clipW = _activeClip.DurationTicks * _model.ZoomX;
                _selectEndLocalX = Math.Clamp(pos.X - clipLeftPx, 0, clipW);
                UpdateRangeSelectionVisual();
                return;
            }

            if (!_isDragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
            {
                _isDragging = true;
                ClipLayer.CaptureMouse();
            }

            if (!_isDragging) return;

            switch (_dragMode)
            {
                case DragMode.Move:
                    double tickDelta = dx / _model.ZoomX;
                    int trackDelta = (int)Math.Round(dy / _model.TrackHeight);
                    ApplyMoveDelta(tickDelta, trackDelta);
                    break;

                case DragMode.ResizeRight:
                    double newDuration = _dragStartDuration + dx / _model.ZoomX;
                    _activeClip.DurationTicks = Math.Max(TrackItemViewModel.PPQN / 4, newDuration);
                    _model.ClampClipDurationToTrack(_activeClip, TrackItemViewModel.PPQN / 4);
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
                if (_isDragging)
                    CommitMoveDrag();
                else
                    SelectClip(_activeClip.Id, raiseEvent: true, preserveMultiSelection: true);
            }
            else if (_isDragging && _dragMode == DragMode.ResizeRight)
            {
                double snappedEnd = _model.SnapToGrid(_activeClip.StartTick + _activeClip.DurationTicks);
                double maxEnd = _model.GetMaxAllowedEndTick(_activeClip);
                if (snappedEnd > maxEnd)
                    snappedEnd = maxEnd;
                double newDur = snappedEnd - _activeClip.StartTick;
                _activeClip.DurationTicks = Math.Max(TrackItemViewModel.PPQN / 4, newDur);
                UpdateClipVisual(_activeClip);
                RefreshContentLayout(new HashSet<Guid> { _activeClip.Id });
                if (Math.Abs(_activeClip.DurationTicks - _dragStartDuration) > 0.01)
                    ClipResized?.Invoke(_activeClip, _dragStartDuration, _activeClip.DurationTicks);
            }

            _dragMode = DragMode.None;
            _activeClip = null;
            _isDragging = false;
            _moveClipIds.Clear();
            _dragStartPositions.Clear();
            SyncVisibleClips();
            if (ClipLayer.IsMouseCaptured)
                ClipLayer.ReleaseMouseCapture();
        }

        private void CancelActiveInteraction(bool revertChanges)
        {
            if (revertChanges && _isDragging && _activeClip != null)
            {
                if (_dragMode == DragMode.Move)
                    RevertMoveDrag();
                else if (_dragMode == DragMode.ResizeRight)
                {
                    _activeClip.DurationTicks = _dragStartDuration;
                    UpdateClipVisual(_activeClip);
                }
            }

            if (_dragMode == DragMode.SelectRange)
            {
                SelectionLayer.Children.Clear();
                _rangeSelectionRect = null;
            }

            _dragMode = DragMode.None;
            _activeClip = null;
            _isDragging = false;
            _moveClipIds.Clear();
            _dragStartPositions.Clear();
            EndPlayheadDrag();
            if (ClipLayer.IsMouseCaptured)
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
            DrawTrackBackgrounds();
            DrawGridLines(force: true);
            UpdatePlayheadPosition();
            DrawTimeSelection();
            SyncVisibleClips();
        }

        #endregion

        #region Drop

        private void OnDrop(object sender, DragEventArgs e)
        {
            CancelActiveInteraction(revertChanges: true);

            Point pos = e.GetPosition(ClipLayer);
            double tickPos = (pos.X + MainScroller.HorizontalOffset) / GetEffectiveZoom();
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
                    DurationTicks = Math.Max(TrackItemViewModel.PPQN / 4, durationTicks),
                    TrackIndex = trackIndex
                };

                _model.ResolveClipPlacement(clip, tickPos, trackIndex);

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
