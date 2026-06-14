using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Documents;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Path = System.IO.Path;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using AudioStudio.Commands;
using AudioStudio.ContextMenus;
using AudioStudio.Controls;
using AudioStudio.Services;

namespace AudioStudio
{
    // ========== ОПТИМИЗАЦИЯ: Кэш для waveform ==========
    public class WaveformCache
    {
        private readonly Dictionary<string, WriteableBitmap> _cache = new();
        private readonly Dictionary<string, float[]> _peakCache = new();
        private readonly int _maxCacheSize = 10;
        
        public void CacheWaveform(string filePath, float[] peaks, WriteableBitmap bitmap)
        {
            if (_cache.Count >= _maxCacheSize)
            {
                // Удаляем самый старый элемент
                var oldest = _cache.Keys.FirstOrDefault();
                if (oldest != null)
                {
                    _cache.Remove(oldest);
                    _peakCache.Remove(oldest);
                }
            }
            _cache[filePath] = bitmap;
            _peakCache[filePath] = peaks;
        }
        
        public bool TryGetWaveform(string filePath, out WriteableBitmap? bitmap, out float[]? peaks)
        {
            if (_cache.TryGetValue(filePath, out bitmap) && _peakCache.TryGetValue(filePath, out peaks))
            {
                return true;
            }
            bitmap = null;
            peaks = null;
            return false;
        }
        
        public void Clear()
        {
            _cache.Clear();
            _peakCache.Clear();
        }
    }
    
    // ========== Ghost Adorner для drag-drop как в FL Studio ==========
    public class GhostAdorner : Adorner
    {
        private readonly FontAwesome.Sharp.IconChar icon;
        private readonly string fileName;
        private Point _offset = new Point(15, -30);
        
        public GhostAdorner(UIElement adornedElement, FontAwesome.Sharp.IconChar fileIcon, string fileNameText) 
            : base(adornedElement)
        {
            icon = fileIcon;
            fileName = fileNameText;
            
            // Создаём визуальный контент фантома (для Measure/Arrange)
            var content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 60, 60, 80)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 16, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 8,
                    BlurRadius = 16,
                    Opacity = 0.6
                }
            };
            
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            
            stackPanel.Children.Add(new FontAwesome.Sharp.IconBlock
            {
                Icon = icon,
                FontSize = 18,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            
            stackPanel.Children.Add(new TextBlock
            {
                Text = fileName,
                FontSize = 12,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            
            content.Child = stackPanel;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            
            IsHitTestVisible = false;
        }
        
        protected override Size MeasureOverride(Size constraint)
        {
            return new Size(200, 40); // Размер ghost
        }
        
        protected override Size ArrangeOverride(Size finalSize)
        {
            return finalSize;
        }
        
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            
            // Отрисовываем контент в позиции курсора со смещением
            var pos = Mouse.GetPosition(AdornedElement);
            var renderPos = new Point(
                pos.X + _offset.X,
                pos.Y + _offset.Y
            );
            
            double width = 180;
            double height = 36;
            
            // Рисуем тень
            var shadowBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            drawingContext.DrawRoundedRectangle(shadowBrush, null, 
                new Rect(renderPos.X + 4, renderPos.Y + 4, width, height), 6, 6);
            
            // Рисуем фон
            var bgBrush = new SolidColorBrush(Color.FromArgb(220, 40, 40, 70));
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 129, 255)), 2);
            drawingContext.DrawRoundedRectangle(bgBrush, borderPen, 
                new Rect(renderPos.X, renderPos.Y, width, height), 6, 6);
            
            // Рисуем иконку и текст
            var formattedIcon = new FormattedText(
                char.ConvertFromUtf32((int)icon),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("pack://application:,,,/FontAwesome.Sharp;component/fonts/#Font Awesome 6 Free Solid"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                18,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            
            var formattedText = new FormattedText(
                fileName,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            
            // Позиционируем текст внутри
            double textY = renderPos.Y + (height - Math.Max(formattedIcon.Height, formattedText.Height)) / 2;
            drawingContext.DrawText(formattedIcon, new Point(renderPos.X + 12, textY));
            drawingContext.DrawText(formattedText, new Point(renderPos.X + 40, textY + 3));
        }
        
        public void UpdatePosition(Point mousePos)
        {
            InvalidateVisual();
        }
    }
    
    // ========== ОПТИМИЗАЦИЯ: Оптимизированный рисеймплер ==========
    public class OptimizedBufferedProvider : ISampleProvider
    {
        private readonly float[] _buffer;
        private int _position;
        private readonly WaveFormat _waveFormat;
        private bool _isLooping;
        
        public WaveFormat WaveFormat => _waveFormat;
        public bool IsLooping
        {
            get => _isLooping;
            set => _isLooping = value;
        }
        
        public OptimizedBufferedProvider(WaveFormat waveFormat, float[] samples)
        {
            _waveFormat = waveFormat;
            _buffer = samples;
            _position = 0;
        }
        
        public int Read(float[] buffer, int offset, int count)
        {
            if (_position >= _buffer.Length)
            {
                if (_isLooping)
                {
                    _position = 0;
                }
                else
                {
                    return 0;
                }
            }
            
            int available = Math.Min(count, _buffer.Length - _position);
            Array.Copy(_buffer, _position, buffer, offset, available);
            _position += available;
            return available;
        }
        
        public void Seek(int position)
        {
            _position = Math.Max(0, Math.Min(position, _buffer.Length));
        }
        
        public void Reset()
        {
            _position = 0;
        }
        
        public int Position => _position;
        public int Length => _buffer.Length;
    }
    
    public class AudioClip
    {
        public float[] Samples { get; set; } = Array.Empty<float>();
        public string? SourceFile { get; set; }
        public double StartTime { get; set; }
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 2;
        public int TrackIndex { get; set; }
        public double Duration => Samples.Length / (double)(SampleRate * Math.Max(1, Channels));
        public string Name { get; set; } = "Клип";
        public Rect Bounds { get; set; }
        public bool IsSelected { get; set; }
        public float Volume { get; set; } = 1.0f;
        public float Pan { get; set; } = 0.0f;
        public bool IsDragOver { get; set; }
        public bool IsDropTarget { get; set; } // Рамка - исчезает после drop
        public bool IsDropHighlighted { get; set; } // Подсветка надписи - остаётся до клика
        
        public AudioClip DeepClone()
        {
            return new AudioClip
            {
                Samples = (float[])Samples.Clone(),
                SampleRate = SampleRate,
                Channels = Channels,
                StartTime = StartTime,
                SourceFile = SourceFile,
                TrackIndex = TrackIndex,
                Name = Name,
                Volume = Volume,
                Pan = Pan,
                Bounds = Bounds,
                IsSelected = IsSelected
            };
        }
    }

    public class FileItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Extension { get; set; } = "";
        public bool IsDirectory { get; set; } = false;
        public FontAwesome.Sharp.IconChar Icon => GetFileIcon(Extension);
        public string Duration { get; set; } = "";
        public long Size { get; set; }
        public List<object> Children { get; set; } = new(); // для совместимости с TreeView
        public string DisplayName => Name;

        public static FontAwesome.Sharp.IconChar GetFileIcon(string ext)
        {
            return ext.ToLower() switch
            {
                ".wav" => FontAwesome.Sharp.IconChar.FileAudio,
                ".mp3" => FontAwesome.Sharp.IconChar.Music,
                ".flac" => FontAwesome.Sharp.IconChar.CompactDisc,
                ".ogg" => FontAwesome.Sharp.IconChar.FileAudio,
                ".m4a" => FontAwesome.Sharp.IconChar.Headphones,
                ".aiff" or ".aif" => FontAwesome.Sharp.IconChar.FileAudio,
                _ => FontAwesome.Sharp.IconChar.File
            };
        }
    }
    
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FolderItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public FontAwesome.Sharp.IconChar Icon => FontAwesome.Sharp.IconChar.Folder;
        public string DisplayName => Name;
        public string Duration { get; set; } = "";
        public bool IsDirectory { get; set; } = true;
        public List<object> Children { get; set; } = new();
        public bool IsExpanded { get; set; }
        public bool IsLoaded { get; set; }
    }

    public partial class MainWindow : Window
    {
        
        private AudioEngine _audio = new();
        
        private List<AudioClip> tracks = new();
        private int selectedTrackIndex = -1;
        private int focusedClipIndex = -1;
        
        private float[]? clipboard;
        private int clipboardChannels = 2;
        private int clipboardSampleRate = 44100;
        
        private double selectionStart = -1;
        private double selectionEnd = -1;
        
        private double pixelsPerSecond = 50;
        
        private DispatcherTimer? _previewTimer;
        private bool isPlaying;
        private double currentTime;
        private bool _isLoopEnabled;
        private bool _isOperationActive;
        private string _currentOperationDescription = "";
        private string _lastStatusDetail = "";
        private string _activityBaseText = "";
        private int _dotsPhase;
        private DispatcherTimer? _popupUpdateTimer;
        private DispatcherTimer? _activityPopupCloseTimer;
        private DispatcherTimer? _dotsTimer;
        private DispatcherTimer? _ringIdleTimer;
        private double _ringProgress;
        private double _ringReportedProgress;
        private bool _ringAnimActive;
        private DateTime _ringAnimStartUtc;
        private DispatcherTimer? _zoomDebounce;
        
        // ========== Command System for Undo/Redo ==========
        private readonly AudioStudio.Commands.CommandManager _commandManager = new();
        
        // Selection overlay on separate layer
        private SelectionOverlaySimple? _selectionOverlay;
        
        // Selection state (in seconds)
        private double _selectionStartTime = -1;
        private double _selectionEndTime = -1;
        
        // Selection dragging state
        private bool _isSelecting;
        private Point _selectionStartPoint;
        private int _selectingTrackIndex = -1;
        
        // Handle dragging state (для ручек на waveform canvas)
        private enum HandleDrag { None, Left, Right }
        private HandleDrag _handleDrag = HandleDrag.None;
        
        // View mode
        private bool _showSpectrogram = true;
        private readonly Models.PlaylistViewModel _playlistViewModel = new();
        private readonly Dictionary<Guid, float[]> _clipSamplesCache = new();
        private readonly Models.PlaylistClipboard _playlistClipboard = new();
        private Guid? _instrumentsPlaylistClipId;
        private Guid? _playlistRangeClipId;
        private double _playlistRangeStartSec = -1;
        private double _playlistRangeEndSec = -1;
        private string? _currentProjectPath;
        private readonly Dictionary<int, float[]> _spectrogramCache = new();
        private readonly Dictionary<int, WriteableBitmap> _spectrogramBitmaps = new();
        private readonly HashSet<int> _spectrogramDirty = new();
        private bool _trackLabelsUpdating;
        private readonly Dictionary<string, string> _durationCache = new();
        
        // Legacy SelectionManager (for compatibility)
        public SelectionManager SelectionManager { get; private set; }
        private AudioContextMenu? _contextMenu;
        
        // Public properties for Command access
        public List<AudioClip> Tracks => tracks;
        public List<AudioClip> TracksInternal => tracks;
        public float[]? ClipboardData { get; set; }
        public int ClipboardChannels { get; set; } = 2;
        public int ClipboardSampleRate { get; set; } = 44100;
        
        // Create empty track helper for commands
        public AudioClip CreateEmptyTrackInternal(int index)
        {
            return new AudioClip
            {
                TrackIndex = index,
                Name = $"Дорожка {index + 1}",
                Samples = Array.Empty<float>(),
                StartTime = 0
            };
        }
        
        // Public accessors for SelectionManager
        public double PixelsPerSecond => pixelsPerSecond;
        
        public bool HasPlaylistTimeSelection() =>
            _playlistRangeClipId.HasValue &&
            _playlistRangeEndSec - _playlistRangeStartSec >= 0.05;

        public bool HasSelection() =>
            HasPlaylistTimeSelection() ||
            HasSelectedPlaylistClip() ||
            (SelectionManager?.HasSelection ?? false);

        public Models.PlaylistClipboard PlaylistClipboard => _playlistClipboard;

        public bool HasSelectedPlaylistClip()
        {
            var clip = PlaylistViewControl?.GetSelectedClip();
            return clip != null;
        }

        public int SelectedPlaylistClipCount => PlaylistViewControl?.SelectedClipCount ?? 0;

        public bool CanMergeSelectedPlaylistClips() => GetMergeDisabledReason() == null;

        public string? GetMergeDisabledReason()
        {
            var clips = PlaylistViewControl?.GetSelectedClips();
            if (clips == null || clips.Count < 2)
                return "Выберите 2 или больше клипов на одной дорожке.\n\nCtrl+клик — добавить к выбору\nShift+клик — выбрать диапазон на дорожке";

            if (clips.Select(c => c.TrackIndex).Distinct().Count() != 1)
                return "Склеить можно только клипы на одной дорожке плейлиста.";

            int sr = clips[0].SampleRate;
            int ch = Math.Max(1, clips[0].Channels);
            if (clips.Any(c => c.SampleRate != sr || Math.Max(1, c.Channels) != ch))
                return "У выбранных клипов разный формат (sample rate или каналы).";

            if (!clips.Any(c => TryGetCachedClipSamples(c, out var s) && s.Length > 0))
                return "В выбранных клипах нет загруженного аудио.";

            return null;
        }

        private bool TryGetCachedClipSamples(Models.TrackItemViewModel clip, out float[] samples)
        {
            if (_clipSamplesCache.TryGetValue(clip.Id, out samples!) && samples.Length > 0)
                return true;

            samples = LoadClipSamples(clip);
            return samples.Length > 0;
        }
        
        public bool HasClipboard() =>
            _playlistClipboard.HasContent ||
            (ClipboardData != null && ClipboardData.Length > 0);
        
        public AudioStudio.Commands.CommandManager CommandManager => _commandManager;
        
        public int FocusedClipIndex
        {
            get => focusedClipIndex;
            set => focusedClipIndex = value;
        }
        
        public void UpdateSelectionDisplay()
        {
            if (SelectionManager?.HasSelection == true)
            {
                double start = Math.Min(SelectionManager.SelectionStart, SelectionManager.SelectionEnd);
                double end = Math.Max(SelectionManager.SelectionStart, SelectionManager.SelectionEnd);
                double duration = end - start;
                
                // Update TimeRuler
                if (TimeRulerControl != null)
                {
                    TimeRulerControl.PixelsPerSecond = pixelsPerSecond;
                    TimeRulerControl.SelectionStart = start;
                    TimeRulerControl.SelectionEnd = end;
                    TimeRulerControl.UpdateSelectionHighlight();
                }
                
                SetStatusText($"Выделено: {FormatTime(duration)}");
            }
            else
            {
                // Hide selection on TimeRuler
                if (TimeRulerControl != null)
                {
                    TimeRulerControl.SelectionStart = -1;
                    TimeRulerControl.SelectionEnd = -1;
                    TimeRulerControl.UpdateSelectionHighlight();
                }
            }
        }
        
        public void ClearSelectionUI()
        {
            EnableControls(HasPlayableContent());
            SetStatusText("Готово");
        }
        
        /// <summary>
        /// Clear selection and hide overlay
        /// </summary>
        public void ClearSelection()
        {
            _selectionStartTime = -1;
            _selectionEndTime = -1;
            _playlistRangeClipId = null;
            _playlistRangeStartSec = -1;
            _playlistRangeEndSec = -1;
            PlaylistViewControl?.ClearTimeSelection();
            PlaylistViewControl?.ClearClipSelection();

            SelectionManager.SelectionStart = -1;
            SelectionManager.SelectionEnd = -1;
            
            // Hide overlay
            if (_selectionOverlay != null)
            {
                _selectionOverlay.IsVisible = false;
            }
            
            // Clear TimeRuler
            if (TimeRulerControl != null)
            {
                TimeRulerControl.SelectionStart = -1;
                TimeRulerControl.SelectionEnd = -1;
                TimeRulerControl.UpdateSelectionHighlight();
            }
            
            ClearSelectionUI();
        }
        
        /// <summary>
        /// Update selection UI with current selection
        /// </summary>
        private void UpdateSelectionUI()
        {
            if (_selectionStartTime < 0 || _selectionEndTime < 0) return;
            
            double start = Math.Min(_selectionStartTime, _selectionEndTime);
            double end = Math.Max(_selectionStartTime, _selectionEndTime);
            double duration = end - start;
            
            // Update overlay position (лейбл + скролл)
            if (_selectionOverlay != null)
            {
                double scrollOffset = TracksScroller.HorizontalOffset;
                _selectionOverlay.Left = start * pixelsPerSecond - scrollOffset + TrackLabelWidth;
                _selectionOverlay.Right = end * pixelsPerSecond - scrollOffset + TrackLabelWidth;
            }
            
            // Update TimeRuler
            if (TimeRulerControl != null)
            {
                TimeRulerControl.PixelsPerSecond = pixelsPerSecond;
                TimeRulerControl.SelectionStart = start;
                TimeRulerControl.SelectionEnd = end;
                TimeRulerControl.UpdateSelectionHighlight();
            }
        }
        
        private void TracksScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            TimeRulerControl.ScrollOffset = TracksScroller.HorizontalOffset;
            TimeRulerControl.TotalDuration = GetTotalDuration();
            TimeRulerControl.RefreshScroll();
            if (TimeRulerControl.SelectionStart >= 0)
                TimeRulerControl.UpdateSelectionHighlight();
            
            if (_selectionStartTime >= 0 && _selectionEndTime >= 0)
                UpdateSelectionUI();
        }
        
        public void SelectAll()
        {
            var playlistClip = GetSelectedPlaylistClip();
            if (playlistClip != null)
            {
                double start = _playlistViewModel.TickToSeconds(playlistClip.StartTick);
                double end = _playlistViewModel.TickToSeconds(playlistClip.EndTick);
                SetPlaylistTimeSelection(playlistClip.Id, start, end);
                return;
            }

            if (selectedTrackIndex >= 0 && selectedTrackIndex < tracks.Count)
            {
                var track = tracks[selectedTrackIndex];
                if (track.Samples.Length > 0)
                {
                    _selectionStartTime = 0;
                    _selectionEndTime = track.Duration;
                    SelectionManager.SelectionStart = 0;
                    SelectionManager.SelectionEnd = track.Duration;
                    focusedClipIndex = selectedTrackIndex;
                    
                    UpdateSelectionUI();
                    EnableControls(true);
                    SetStatusText("Выделено: весь клип");
                }
            }
        }
        
        private const int TrackMargin = 3;
        private const int TrackLabelWidth = 160;
        private const int TrackHeight = 100;
        private const double MinPixelsPerSecond = 5;
        private const int MaxTracks = 50; // Ограничение на количество треков
        private const int MaxClipsPerTrack = 100; // Ограничение на клипы

        // ========== ОПТИМИЗАЦИЯ: Поля для оптимизации ==========
        private readonly WaveformCache _waveformCache = new();
        private readonly Dictionary<int, WriteableBitmap> _waveformBitmaps = new();
        private readonly Dictionary<int, float[]> _waveformPeaks = new();
        private bool _isUpdatingPlayhead = false;
        private readonly double _lastPlayheadUpdate = 0;
        private string _lastPlayheadTimeText = "";
        
        // ========== Ghost Adorner для drag-drop из TreeView ==========
        private GhostAdorner? _ghostAdorner;
        private AdornerLayer? _adornerLayer;
        private FileItem? _draggedFileItem;
        private Point _lastMousePos;
        private const double DragThreshold = 5; // Минимальное расстояние для начала drag
        
        // Окно инструментов (FL Studio style - привязано к главному окну)
        private InstrumentsWindow? _instrumentsWindow;
        
        // Браузер файлов
        private string _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        private string _currentPath = "";
        private List<FileItem> _currentFiles = new();
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aiff", ".aif", ".wma", ".aac" };

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize SelectionManager and ContextMenu
            SelectionManager = new SelectionManager(this);
            _contextMenu = new AudioContextMenu();
            _contextMenu.SetMainWindow(this);
            
            // Subscribe to command history changes
            _commandManager.HistoryChanged += UpdateCommandButtons;
            
            tracks.Add(CreateEmptyTrack(0));
            tracks.Add(CreateEmptyTrack(1));
            
            // Subscribe to stop event
            _audio.OnPlaybackStopped += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!isPlaying)
                        return;

                    isPlaying = false;
                    SetPlayIcon(false);
                    CompositionTarget.Rendering -= OnRenderFrame;
                    currentTime = 0;
                    _audio.Seek(0);
                    DrawTimeline(rebuildTracks: false);
                    PlaylistViewControl.SetPlayheadTime(0);
                    SetStatusText("Воспроизведение завершено");
                });
            };
            
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            KeyDown += MainWindow_KeyDown;
            KeyDown += MainWindow_KeyDown_Global;
            KeyUp += MainWindow_KeyUp_Global;

            ContentRendered += (_, _) =>
            {
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
            };
            
            Loaded += (s, e) =>
            {
                _selectionOverlay = new SelectionOverlaySimple(SelectionCanvas);

                // Init PlaylistView
                PlaylistViewControl.Model = _playlistViewModel;
                PlaylistViewControl.SetZoom(pixelsPerSecond);
                PlaylistViewControl.InvalidateAll();

                _playlistViewModel.AudioClips.CollectionChanged += OnPlaylistClipsChanged;
                PlaylistViewControl.ClipSelected += OnPlaylistClipSelected;
                PlaylistViewControl.ClipContextMenuRequested += _ =>
                    ShowPlaylistContextMenu(onClip: true, onEmptyTrack: false);
                PlaylistViewControl.ClipRangeSelected += OnPlaylistClipRangeSelected;
                PlaylistViewControl.ClipMoved += OnPlaylistClipMoved;
                PlaylistViewControl.ClipsMoved += OnPlaylistClipsMoved;
                PlaylistViewControl.ClipResized += OnPlaylistClipResized;
                PlaylistViewControl.EmptyAreaInteracted += OnPlaylistEmptyAreaInteracted;
                PlaylistViewControl.FileDropped += (path, tick, track) =>
                    TryAddFileToPlaylist(path, tick, track, replaceClipsAtDropPoint: true);
                PlaylistViewControl.SeekRequested += time =>
                {
                    SeekToTime(time);
                    PlaylistViewControl.SetPlayheadTime(currentTime);
                };

                // Connect PlaylistView scroll → TimeRuler
                PlaylistViewControl.ScrollUpdated += (_, _) =>
                {
                    TimeRulerControl.ScrollOffset = PlaylistViewControl.HorizontalScrollOffset;
                    TimeRulerControl.RefreshScroll();
                };

                // Init TimeRuler from PlaylistView
                SyncTimeRuler();

                TimeRulerControl.Cursor = Cursors.Hand;
                TimeRulerControl.MouseLeftButtonDown += (_, e) =>
                {
                    double time = Math.Max(0, (e.GetPosition(TimeRulerControl).X + TimeRulerControl.ScrollOffset)
                        / TimeRulerControl.PixelsPerSecond);
                    SeekToTime(time);
                    PlaylistViewControl.SetPlayheadTime(currentTime);
                };

                InitializeBrowser();
                SetupTreeViewDragDrop();
                ShowRingIdle();
                SetupActivityPopupHover();
                PlaylistViewControl.SetPlayheadTime(0);
            };
            
            SizeChanged += (s, e) =>
            {
                ClampBrowserPanelWidth();
                if (!_isResizing && PlaylistViewControl.IsVisible)
                {
                    PlaylistViewControl.RefreshViewportLayout();
                    SyncTimeRuler();
                }
            };
        }
        
        private Point _titleDragScreen;
        private Point _titleDragWindow;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
                return;
            }

            if (WindowState == WindowState.Maximized)
                return;

            _titleDragWindow = new Point(Left, Top);
            _titleDragScreen = PointToScreen(e.GetPosition(this));

            Mouse.OverrideCursor = Cursors.Arrow;

            if (sender is UIElement element)
            {
                element.CaptureMouse();
                element.MouseMove += TitleBar_MouseMove;
                element.MouseLeftButtonUp += TitleBar_MouseLeftButtonUp;
            }

            e.Handled = true;
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                TitleBar_EndDrag(sender);
                return;
            }

            var screen = PointToScreen(e.GetPosition(this));
            var delta = screen - _titleDragScreen;
            Left = _titleDragWindow.X + delta.X;
            Top = _titleDragWindow.Y + delta.Y;
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            TitleBar_EndDrag(sender);

        private void TitleBar_EndDrag(object sender)
        {
            Mouse.OverrideCursor = null;

            if (sender is UIElement element)
            {
                element.MouseMove -= TitleBar_MouseMove;
                element.MouseLeftButtonUp -= TitleBar_MouseLeftButtonUp;
                element.ReleaseMouseCapture();
            }
        }
        
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeIcon.Icon = FontAwesome.Sharp.IconChar.WindowMaximize;
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeIcon.Icon = FontAwesome.Sharp.IconChar.WindowRestore;
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Очищаем память перед закрытием
            _waveformPeaks.Clear();
            _waveformBitmaps.Clear();
            _waveformCache.Clear();
            
            foreach (var track in tracks)
            {
                if (track.Samples != null)
                {
                    track.Samples = Array.Empty<float>();
                }
            }
            tracks.Clear();
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            Close();
        }
        
        #region Window Resize
        
        private Point _resizeStart;
        private Rect _windowStart;
        private string _resizeDirection = "";
        private bool _isResizing = false;
        
        private void Resize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WindowState == WindowState.Maximized) return;
            
            var element = sender as FrameworkElement;
            if (element == null) return;
            
            _resizeDirection = element.Tag?.ToString() ?? "";
            _resizeStart = e.GetPosition(this);
            _windowStart = new Rect(Left, Top, ActualWidth, ActualHeight);
            element.CaptureMouse();
            element.MouseMove += Resize_MouseMove;
            element.MouseLeftButtonUp += Resize_MouseLeftButtonUp;
        }
        
        private void Resize_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            
            var element = sender as FrameworkElement;
            if (element == null || string.IsNullOrEmpty(_resizeDirection)) return;
            
            var pos = e.GetPosition(this);
            var dx = pos.X - _resizeStart.X;
            var dy = pos.Y - _resizeStart.Y;
            
            var newLeft = _windowStart.Left;
            var newTop = _windowStart.Top;
            var newWidth = _windowStart.Width;
            var newHeight = _windowStart.Height;
            
            switch (_resizeDirection)
            {
                case "Left":
                    newLeft = _windowStart.Left + dx;
                    newWidth = _windowStart.Width - dx;
                    break;
                case "Right":
                    _isResizing = true;
                    newWidth = _windowStart.Width + dx;
                    break;
                case "Top":
                    newTop = _windowStart.Top + dy;
                    newHeight = _windowStart.Height - dy;
                    break;
                case "Bottom":
                    _isResizing = true;
                    newHeight = _windowStart.Height + dy;
                    break;
                case "TopLeft":
                    newLeft = _windowStart.Left + dx;
                    newTop = _windowStart.Top + dy;
                    newWidth = _windowStart.Width - dx;
                    newHeight = _windowStart.Height - dy;
                    break;
                case "TopRight":
                    newTop = _windowStart.Top + dy;
                    newWidth = _windowStart.Width + dx;
                    newHeight = _windowStart.Height - dy;
                    break;
                case "BottomLeft":
                    newLeft = _windowStart.Left + dx;
                    newWidth = _windowStart.Width - dx;
                    newHeight = _windowStart.Height + dy;
                    break;
                case "BottomRight":
                    _isResizing = true;
                    newWidth = _windowStart.Width + dx;
                    newHeight = _windowStart.Height + dy;
                    break;
            }
            
            newWidth = Math.Max(800, newWidth);
            newHeight = Math.Max(600, newHeight);
            
            Left = newLeft;
            Top = newTop;
            Width = newWidth;
            Height = newHeight;
        }
        
        private void Resize_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null)
            {
                element.MouseMove -= Resize_MouseMove;
                element.MouseLeftButtonUp -= Resize_MouseLeftButtonUp;
                element.ReleaseMouseCapture();
            }
            _resizeDirection = "";
            _isResizing = false;
            DrawTimeline(rebuildTracks: true);
        }
        
        // ========== Window-level Resize Handlers ==========
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element?.Tag == null) return;
            
            _resizeDirection = element.Tag.ToString();
            _resizeStart = e.GetPosition(this);
            _windowStart = new Rect(Left, Top, ActualWidth, ActualHeight);
            
            element.CaptureMouse();
            element.MouseMove += Resize_MouseMove;
            element.MouseLeftButtonUp += Resize_MouseLeftButtonUp;
        }
        #endregion

        [System.Diagnostics.Conditional("DEBUG")]
        private static void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine(msg);
        }
        
        #region File Browser
        
        private void InitializeBrowser()
        {
            _currentPath = _rootPath;
            CurrentPathBox.Text = _rootPath;
            LoadDrives();
            LoadFolderContents(_rootPath);
            
            // Добавляем Downloads после загрузки дисков
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                AddDownloadFolder();
            });
        }
        
        // ========== TreeView Drag-Drop для FL Studio стиля ==========
        private void SetupTreeViewDragDrop()
        {
            // Добавляем обработчики PreviewMouseLeftButtonDown и PreviewMouseMove
            FolderTree.PreviewMouseLeftButtonDown += FolderTree_PreviewMouseLeftButtonDown;
            FolderTree.PreviewMouseMove += FolderTree_PreviewMouseMove;
        }
        
        private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _lastMousePos = e.GetPosition(FolderTree);
            
            // Проверяем, что кликнули на FileItem
            var item = GetItemAtPoint(FolderTree, _lastMousePos);
            if (item is FileItem fileItem)
            {
                _draggedFileItem = fileItem;
            }
            else
            {
                _draggedFileItem = null;
            }
        }
        
        private void FolderTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedFileItem != null)
            {
                var currentPos = e.GetPosition(FolderTree);
                var diff = currentPos - _lastMousePos;
                
                // Проверяем, достаточно ли движения для начала drag
                if (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold)
                {
                    // Показываем ghost и начинаем drag-drop
                    ShowGhostAdorner(_draggedFileItem.Icon, _draggedFileItem.Name);
                    
                    var data = new DataObject(typeof(FileItem), _draggedFileItem);
                    DragDrop.DoDragDrop(FolderTree, data, DragDropEffects.Copy);
                    
                    // После завершения drag скрываем ghost
                    HideGhostAdorner();
                    _draggedFileItem = null;
                }
            }
        }
        
        private object? GetItemAtPoint(TreeView treeView, Point point)
        {
            var element = treeView.InputHitTest(point) as DependencyObject;
            while (element != null)
            {
                if (element is TreeViewItem tvi)
                {
                    return tvi.DataContext;
                }
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }
        
        private void ShowGhostAdorner(FontAwesome.Sharp.IconChar icon, string fileName)
        {
            // Получаем AdornerLayer из TracksBorder
            _adornerLayer = AdornerLayer.GetAdornerLayer(TracksBorder);
            if (_adornerLayer == null) return;
            
            // Создаём и добавляем ghost adorner
            _ghostAdorner = new GhostAdorner(TracksBorder, icon, fileName);
            _adornerLayer.Add(_ghostAdorner);
        }
        
        private void HideGhostAdorner()
        {
            if (_ghostAdorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_ghostAdorner);
                _ghostAdorner = null;
            }
        }
        
        private void LoadDrives()
        {
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new FolderItem 
                    { 
                        Name = d.Name, 
                        FullPath = d.RootDirectory.FullName,
                        IsLoaded = false
                    })
                    .ToList();
                
                FolderTree.ItemsSource = drives;
                
                // Загружаем подпапки с небольшой задержкой для отрисовки
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    foreach (var drive in drives)
                    {
                        try
                        {
                            // Загружаем подпапки сразу
                            LoadSubFoldersSync(drive);
                            
                            var item = FolderTree.ItemContainerGenerator.ContainerFromItem(drive) as TreeViewItem;
                            if (item != null)
                            {
                                item.Expanded -= Folder_Expanded;
                                item.Expanded += Folder_Expanded;
                                item.Collapsed -= Folder_Collapsed;
                                item.Collapsed += Folder_Collapsed;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading drive {drive.Name}: {ex.Message}");
                        }
                    }
                    
                    // Обновляем дерево
                    FolderTree.Items.Refresh();
                });
                
                SetStatusText($"Загружено дисков: {drives.Count}");
            }
            catch (Exception ex)
            {
                SetStatusText($"Ошибка загрузки дисков: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadDrives error: {ex}");
            }
        }
        
        private void AddDownloadFolder()
        {
            try
            {
                var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                downloadsPath = Path.Combine(downloadsPath, "Downloads");
                
                if (!Directory.Exists(downloadsPath)) return;
                
                var downloadsItem = new FolderItem
                {
                    Name = "Downloads",
                    FullPath = downloadsPath,
                    IsLoaded = false
                };
                
                // Добавляем в начало списка дисков
                var items = FolderTree.ItemsSource as List<FolderItem>;
                if (items != null)
                {
                    items.Insert(0, downloadsItem);
                    FolderTree.Items.Refresh();
                    
                    // Привязываем события
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                    {
                        var item = FolderTree.ItemContainerGenerator.ContainerFromItem(downloadsItem) as TreeViewItem;
                        if (item != null)
                        {
                            item.Expanded -= Folder_Expanded;
                            item.Expanded += Folder_Expanded;
                            item.Collapsed -= Folder_Collapsed;
                            item.Collapsed += Folder_Collapsed;
                            LoadSubFoldersSync(downloadsItem);
                            FolderTree.Items.Refresh();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddDownloadFolder error: {ex.Message}");
            }
        }
        
        private void LoadSubFoldersSync(FolderItem folder)
        {
            if (folder.IsLoaded) return;
            folder.IsLoaded = true;
            
            folder.Children.Clear();
            
            try
            {
                // Получаем подпапки
                var dirs = Directory.GetDirectories(folder.FullPath)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(d => d.Name)
                    .Take(50)
                    .Select(d => new FolderItem 
                    { 
                        Name = d.Name, 
                        FullPath = d.FullName,
                        IsLoaded = false
                    })
                    .ToList();
                
                foreach (var dir in dirs)
                {
                    try
                    {
                        var hasSubs = Directory.GetDirectories(dir.FullPath).Any();
                        if (hasSubs)
                        {
                            dir.Children.Add(new FolderItem { Name = "...", FullPath = "" });
                        }
                    }
                    catch { }
                    
                    folder.Children.Add(dir);
                }
                
                // Получаем аудио файлы из этой папки
                var audioFiles = Directory.GetFiles(folder.FullPath)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();
                
                foreach (var filePath in audioFiles)
                {
                    try
                    {
                        var info = new FileInfo(filePath);
                        folder.Children.Add(new FileItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            Extension = info.Extension.ToUpper(),
                            IsDirectory = false,
                            Size = info.Length,
                            Duration = GetAudioDuration(info.FullName)
                        });
                    }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }
        }
        
        private void Folder_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                var folder = item.DataContext as FolderItem;
                if (folder != null && !folder.IsLoaded)
                {
                    LoadSubFoldersSync(folder);
                    
                    // Привязываем события к дочерним элементам
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                    {
                        foreach (var child in folder.Children)
                        {
                            var childItem = item.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                            if (childItem != null)
                            {
                                childItem.Expanded -= Folder_Expanded;
                                childItem.Expanded += Folder_Expanded;
                                childItem.Collapsed -= Folder_Collapsed;
                                childItem.Collapsed += Folder_Collapsed;
                            }
                        }
                        
                        // Удаляем placeholder
                        folder.Children.RemoveAll(c => c is FolderItem fi && fi.Name == "...");
                        FolderTree.Items.Refresh();
                    });
                }
            }
            e.Handled = true;
        }
        
        private void Folder_Collapsed(object sender, RoutedEventArgs e)
        {
            // Можно очистить дочерние элементы для экономии памяти
        }
        
        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // При одинарном клике НЕ загружаем файл - только показываем информацию
            // Загрузка происходит ТОЛЬКО при двойном клике или drag-drop
            
            if (e.NewValue is FolderItem folder && !string.IsNullOrEmpty(folder.FullPath))
            {
                _currentPath = folder.FullPath;
                CurrentPathBox.Text = folder.FullPath;
                
                if (!folder.IsLoaded)
                {
                    LoadSubFoldersSync(folder);
                    FolderTree.Items.Refresh();
                }
            }
            else if (e.NewValue is FileItem file)
            {
                // Показываем информацию о файле, но НЕ загружаем
                int currentTrack = selectedTrackIndex >= 0 ? selectedTrackIndex : 0;
                if (currentTrack >= tracks.Count) currentTrack = 0;
                
                if (tracks[currentTrack].Samples.Length > 0)
                {
                    var currentFile = Path.GetFileName(tracks[currentTrack].SourceFile ?? "");
                    SetStatusText($"Выбран: {file.Name} | Двойной клик или drag -> дорожка {currentTrack + 1} плейлиста");
                }
                else
                {
                    SetStatusText($"Выбран: {file.Name} | Двойной клик или drag -> Трек {currentTrack + 1}");
                }
            }
        }
        
        // ========== Двойной клик для загрузки файла ==========
        private void FolderTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Находим FileItem под курсором
            var point = e.GetPosition(FolderTree);
            var element = FolderTree.InputHitTest(point) as DependencyObject;
            
            while (element != null)
            {
                if (element is TreeViewItem tvi && tvi.DataContext is FileItem file)
                {
                    int targetTrack = selectedTrackIndex >= 0 ? selectedTrackIndex : 0;
                    if (targetTrack >= _playlistViewModel.NumTracks) targetTrack = 0;
                    EnqueuePlaylistFiles(new[] { file.FullPath }, targetTrack);
                    e.Handled = true;
                    return;
                }
                element = VisualTreeHelper.GetParent(element);
            }
        }
        
        private void LoadFolderContents(string path)
        {
            try
            {
                // Считаем файлы через TreeView
                int fileCount = 0;
                foreach (var item in FolderTree.ItemsSource as IEnumerable<FolderItem> ?? Enumerable.Empty<FolderItem>())
                {
                    fileCount += CountFilesRecursive(item);
                }
                
                // StatusText shows "Готово" by default
            }
            catch (UnauthorizedAccessException)
            {
                SetStatusText("Нет доступа к папке");
            }
            catch (Exception ex)
            {
                SetStatusText($"Ошибка: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadFolderContents error: {ex}");
            }
        }
        
        private int CountFilesRecursive(FolderItem folder)
        {
            int count = 0;
            foreach (var child in folder.Children)
            {
                if (child is FileItem)
                    count++;
                else if (child is FolderItem sub)
                    count += CountFilesRecursive(sub);
            }
            return count;
        }
        
        private string GetAudioDuration(string path)
        {
            if (_durationCache.TryGetValue(path, out string? cached))
                return cached;
            try
            {
                using var reader = new AudioFileReader(path);
                var duration = reader.TotalTime;
                var result = duration.Hours > 0 
                    ? $"{duration.Hours:D1}:{duration.Minutes:D2}:{duration.Seconds:D2}" 
                    : $"{duration.Minutes:D1}:{duration.Seconds:D2}";
                _durationCache[path] = result;
                return result;
            }
            catch
            {
                return "--:--";
            }
        }
        
        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Метод оставлен для обратной совместимости, но FileList больше не используется
            // Двойной клик обрабатывается в FolderTree_SelectedItemChanged
        }
        
        private void ExpandFolderInTree(string path)
        {
            foreach (var drive in FolderTree.ItemsSource as IEnumerable<FolderItem>)
            {
                if (path.StartsWith(drive.FullPath))
                {
                    var driveItem = FolderTree.ItemContainerGenerator.ContainerFromItem(drive) as TreeViewItem;
                    if (driveItem != null)
                    {
                        ExpandPath(driveItem, path, drive.FullPath);
                    }
                    break;
                }
            }
        }
        
        private bool ExpandPath(TreeViewItem parent, string targetPath, string currentPath)
        {
            if (targetPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
            {
                parent.IsSelected = true;
                return true;
            }
            
            foreach (var child in parent.Items)
            {
                if (child is FolderItem folder && targetPath.StartsWith(folder.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    var childItem = parent.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                    if (childItem != null)
                    {
                        childItem.IsExpanded = true;
                        if (ExpandPath(childItem, targetPath, folder.FullPath))
                            return true;
                    }
                }
            }
            return false;
        }
        
        private void BrowseRootFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Выберите корневую папку для браузера",
            };
            
            if (!string.IsNullOrEmpty(_currentPath) && Directory.Exists(_currentPath))
            {
                dialog.InitialDirectory = _currentPath;
            }
            
            if (dialog.ShowDialog() == true)
            {
                _rootPath = dialog.FolderName;
                _currentPath = _rootPath;
                CurrentPathBox.Text = _rootPath;
                LoadDrives();
                LoadFolderContents(_rootPath);
                SetStatusText($"Браузер: {_rootPath}");
            }
        }
        
        private void GoUp_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath)) return;
            
            var parent = Directory.GetParent(_currentPath);
            if (parent != null)
            {
                _currentPath = parent.FullName;
                CurrentPathBox.Text = _currentPath;
                LoadFolderContents(_currentPath);
            }
        }
        
        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_rootPath))
            {
                _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            }
            _currentPath = _rootPath;
            CurrentPathBox.Text = _rootPath;
            LoadDrives();
            
            BtnAll.Background = new SolidColorBrush(Color.FromRgb(62, 62, 66));
            BtnDownloads.Background = Brushes.Transparent;
        }
        
        private void FilterDownloads_Click(object sender, RoutedEventArgs e)
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                _currentPath = downloadsPath;
                CurrentPathBox.Text = downloadsPath;
                
                // Создаём TreeView с одной папкой Downloads
                var downloadsItem = new FolderItem
                {
                    Name = "Downloads",
                    FullPath = downloadsPath,
                    IsLoaded = false
                };
                
                FolderTree.ItemsSource = new List<FolderItem> { downloadsItem };
                LoadSubFoldersSync(downloadsItem);
                FolderTree.Items.Refresh();
            }
            
            BtnDownloads.Background = new SolidColorBrush(Color.FromRgb(62, 62, 66));
            BtnAll.Background = Brushes.Transparent;
        }

        #endregion

        private void ShowHotkeys_Click(object sender, RoutedEventArgs e)
        {
            var hotkeysText = @"ГОРЯЧИЕ КЛАВИШИ

Файл:
  Ctrl+O        - Добавить аудио
  Ctrl+S        - Сохранить проект
  Ctrl+Shift+S  - Сохранить как

Воспроизведение:
  Space     - Воспроизведение / Пауза
  R         - Перезапуск с начала
  K         - Стоп
  Enter     - Стоп и в начало

Клип на плейлисте:
  Ctrl+клик        - Выделить несколько клипов
  Shift+клик       - Выбрать диапазон на дорожке
  Ctrl+Shift+ЛКМ   - Область на записи

Редактирование:
  Ctrl+X    - Вырезать
  Ctrl+C    - Копировать
  Ctrl+V    - Вставить
  ПКМ → Склеить выбранные клипы (2+ на одной дорожке)
  Ctrl+D    - Выделить всё
  Del       - Удалить выделенное
  Ctrl+Z    - Отменить
  Ctrl+Y    - Повторить

Навигация:
  Ctrl+колесо мыши - Зум
  Колесо мыши      - Горизонтальная прокрутка

Другое:
  Home      - В начало трека
  End       - В конец трека";
            
            MessageBox.Show(hotkeysText, "Горячие клавиши", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void UpdatePreviewPosition()
        {
            if (isPlaying) return;
            
            double total = GetTotalDuration();
            CurrentTimeText.Text = FormatTime(currentTime);
            TotalTimeText.Text = FormatTime(total);
        }

        private AudioClip CreateEmptyTrack(int index)
        {
            return new AudioClip
            {
                TrackIndex = index,
                Name = $"Дорожка {index + 1}",
                Samples = Array.Empty<float>(),
                StartTime = 0
            };
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsTextInputFocused())
                return;

            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
            {
                TogglePlayStopTransport();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            switch (e.Key)
            {
                case Key.R:
                    Restart_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.K:
                    Stop_Click(sender, e);
                    e.Handled = true;
                    break;
            }
        }

        private void TogglePlayStopTransport()
        {
            if (isPlaying)
                StopTransport(resetToStart: false);
            else
                StartPlayback();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
            {
                SaveProjectAs_Click(sender, e);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.X: Cut_Click(sender, e); break;
                    case Key.C: Copy_Click(sender, e); break;
                    case Key.V: Paste_Click(sender, e); break;
                    case Key.Z: Undo_Click(sender, e); break;
                    case Key.Y: Redo_Click(sender, e); break;
                    case Key.D: SelectAll(); break;
                    case Key.O: AddAudio_Click(sender, e); e.Handled = true; break;
                    case Key.S:
                        SaveProject_Click(sender, e);
                        e.Handled = true;
                        break;
                }
            }
            else if (e.Key == Key.Delete)
            {
                Delete_Click(sender, e);
            }
            else if (e.Key == Key.Enter)
            {
                Stop_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                currentTime = 0;
                UpdatePreviewPosition();
                DrawTimeline(rebuildTracks: false);
            }
            else if (e.Key == Key.End)
            {
                currentTime = GetTotalDuration();
                UpdatePreviewPosition();
                DrawTimeline(rebuildTracks: false);
            }
        }
        
        private void MainWindow_KeyDown_Global(object sender, KeyEventArgs e)
        {
            // Ctrl зажат - обновляем курсоры всех клипов
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                // Обновляем состояние курсора
            }
        }
        
        private void MainWindow_KeyUp_Global(object sender, KeyEventArgs e)
        {
            // Ctrl отпущен
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                // Сбрасываем курсоры
            }
        }
        
        public void UpdateTrackLabels()
        {
            Log($"UpdateTrackLabels: start, tracks={tracks.Count}, updating={_trackLabelsUpdating}");
            if (_trackLabelsUpdating)
            {
                Log("UpdateTrackLabels: reentrant call, skipping");
                return;
            }
            _trackLabelsUpdating = true;
            try
            {
            TracksPanel.Children.Clear();
            _playheadLines.Clear(); // Оптимизация: очищаем кэш playhead
            _endOfTrackLines.Clear(); // Очищаем индикаторы конца
            
            foreach (var track in tracks)
            {
                bool isSelected = track.TrackIndex == selectedTrackIndex;
                bool isDropTarget = track.IsDropTarget;     // Рамка - только во время drag
                bool isDropHighlighted = track.IsDropHighlighted; // Подсветка надписи - остаётся после drop
                
                var trackRow = new Grid
                {
                    Height = TrackHeight,
                    Margin = new Thickness(0, 0, 0, 1),
                    Tag = track.TrackIndex
                };
                
                trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrackLabelWidth) });
                trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                // Рамка - только во время drag, исчезает после drop
                var labelPanel = new Border
                {
                    Background = isDropHighlighted ? 
                        new SolidColorBrush(Color.FromArgb(100, 120, 129, 255)) : // Подсветка для highlighted
                        new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                    BorderBrush = isDropTarget ? 
                        new SolidColorBrush(Color.FromRgb(120, 129, 255)) :  // Рамка - только во время drag
                        new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                    BorderThickness = isDropTarget ? new Thickness(3) : new Thickness(0, 0, 1, 0),
                    Cursor = Cursors.Hand,
                    Tag = track.TrackIndex,
                    AllowDrop = true
                };
                
                // FL Studio style: drag обработчики для КАЖДОГО labelPanel
                labelPanel.DragEnter += LabelPanel_DragEnter;
                labelPanel.DragLeave += LabelPanel_DragLeave;
                labelPanel.DragOver += LabelPanel_DragOver;
                labelPanel.Drop += LabelPanel_Drop;
                
                var waveformPanel = new Border
                {
                    Background = isDropHighlighted ? 
                        new SolidColorBrush(Color.FromArgb(100, 60, 90, 160)) :
                        (isSelected ? 
                            new SolidColorBrush(Color.FromArgb(255, 35, 35, 40)) :
                            new SolidColorBrush(Color.FromRgb(30, 30, 35))),
                    BorderBrush = isDropTarget ? 
                        new SolidColorBrush(Color.FromRgb(120, 129, 255)) :
                        new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                    BorderThickness = isDropTarget ? new Thickness(2) : new Thickness(0),
                    Tag = track.TrackIndex,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ClipToBounds = true
                };
                
                // ======= ЛЕВАЯ ПАНЕЛЬ С НАЗВАНИЕМ ТРЕКА =======
                var labelGrid = new Grid();
                labelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                labelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                labelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                var trackNumber = new TextBlock
                {
                    Text = $"Track {track.TrackIndex + 1}",
                    Foreground = isDropHighlighted ? 
                        new SolidColorBrush(Color.FromRgb(200, 209, 255)) : // Яркий после drop
                        (isSelected ? 
                            new SolidColorBrush(Color.FromRgb(120, 129, 255)) : 
                            new SolidColorBrush(Color.FromRgb(150, 150, 155))),
                    FontSize = 14,
                    FontWeight = isDropHighlighted ? FontWeights.Bold : (isSelected ? FontWeights.Bold : FontWeights.Normal),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(8, 5, 0, 0)
                };
                
                var fileNameBlock = new TextBlock
                {
                    Text = track.Samples.Length > 0 ? 
                        (Path.GetFileName(track.SourceFile) ?? "Файл") : 
                        "Пусто",
                    Foreground = isDropHighlighted ? 
                        new SolidColorBrush(Color.FromRgb(220, 220, 230)) :
                        (isSelected ? 
                            new SolidColorBrush(Color.FromRgb(180, 180, 185)) : 
                            new SolidColorBrush(Color.FromRgb(120, 120, 125))),
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(8, 2, 8, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                
                var playButton = new Button
                {
                    Content = "▶",
                    Width = 40,
                    Height = 40,
                    FontSize = 22,
                    Background = Brushes.Transparent,
                    Foreground = isDropHighlighted ? 
                        new SolidColorBrush(Color.FromRgb(200, 209, 255)) :
                        new SolidColorBrush(Color.FromRgb(150, 150, 155)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 8, 8),
                    Tag = track.TrackIndex
                };
                playButton.Click += (s, e) => 
                {
                    // При клике сбрасываем drop-highlighted, выделение и выбираем трек
                    foreach (var t in tracks) t.IsDropHighlighted = false;
                    foreach (var t in tracks) t.IsDropTarget = false;
                    ClearSelection();
                    selectedTrackIndex = track.TrackIndex;
                    UpdateTrackLabels();
                };
                
                Grid.SetRow(trackNumber, 0);
                Grid.SetRow(fileNameBlock, 1);
                Grid.SetRow(playButton, 2);
                Grid.SetColumn(playButton, 1);
                
                labelGrid.Children.Add(trackNumber);
                labelGrid.Children.Add(fileNameBlock);
                labelGrid.Children.Add(playButton);
                
                labelPanel.Child = labelGrid;
                labelPanel.MouseLeftButtonDown += (s, args) =>
                {
                    foreach (var t in tracks) { t.IsDropHighlighted = false; t.IsDropTarget = false; }
                    ClearSelection();
                    selectedTrackIndex = (int)((Border)s).Tag;
                    UpdateTrackLabels();
                    UpdateInstrumentsWindow();
                };
                labelPanel.MouseRightButtonDown += (s, args) =>
                {
                    selectedTrackIndex = (int)((Border)s).Tag;
                    focusedClipIndex = selectedTrackIndex;
                    _contextMenu?.UpdateMenuState();
                    _contextMenu!.IsOpen = true;
                    args.Handled = true;
                };
                
                var waveformCanvas = new Canvas
                {
                    Background = Brushes.Transparent,
                    Tag = track.TrackIndex,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };
                
                waveformCanvas.MouseLeftButtonDown += WaveformCanvas_MouseLeftButtonDown;
                waveformCanvas.MouseMove += WaveformCanvas_MouseMove;
                waveformCanvas.MouseLeftButtonUp += WaveformCanvas_MouseLeftButtonUp;
                waveformCanvas.MouseRightButtonUp += WaveformCanvas_MouseRightButtonUp;
                
                waveformPanel.Child = waveformCanvas;
                
                Grid.SetColumn(waveformPanel, 1);
                
                trackRow.Children.Add(labelPanel);
                trackRow.Children.Add(waveformPanel);
                
                TracksPanel.Children.Add(trackRow);
                
                if (track.Samples.Length > 0)
                {
                    double trackWidth = Math.Max(track.Duration * pixelsPerSecond, 500);
                    waveformCanvas.Width = trackWidth;
                    waveformPanel.Width = trackWidth;
                    Log($"UpdateTrackLabels: calling DrawWaveformInCanvas track {track.TrackIndex}, width={trackWidth:F0}");
                    DrawWaveformInCanvas(waveformCanvas, track, trackWidth);
                    Log($"UpdateTrackLabels: DrawWaveformInCanvas done track {track.TrackIndex}");
                    
                    // Добавляем индикатор КОНЦА трека (зелёная линия)
                    double endX = track.Duration * pixelsPerSecond;
                    var endLine = new Line
                    {
                        X1 = endX, Y1 = 0,
                        X2 = endX, Y2 = TrackHeight,
                        Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Зелёный
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 2, 2 }, // Пунктир
                        Tag = "endOfTrack"
                    };
                    _endOfTrackLines[track.TrackIndex] = endLine;
                    waveformCanvas.Children.Add(endLine);
                    
                    // Добавляем playhead ПОСЛЕ waveform, чтобы он был поверх
                    double playheadX = currentTime * pixelsPerSecond;
                    if (playheadX >= 0)
                    {
                        var playheadLine = new Line
                        {
                            X1 = playheadX, Y1 = 0,
                            X2 = playheadX, Y2 = TrackHeight,
                            Stroke = new SolidColorBrush(Color.FromRgb(255, 50, 50)),
                            StrokeThickness = 3,
                            IsHitTestVisible = true,
                            Cursor = Cursors.SizeWE,
                            Tag = "playhead"
                        };
                        
                        // Оптимизация: регистрируем линию для быстрого обновления
                        RegisterPlayheadLine(track.TrackIndex, playheadLine);
                        
                        var hitArea = new Rectangle
                        {
                            Width = 20,
                            Height = TrackHeight,
                            Fill = Brushes.Transparent,
                            Tag = "playhead"
                        };
                        Canvas.SetLeft(hitArea, playheadX - 10);
                        Canvas.SetTop(hitArea, 0);
                        
                        waveformCanvas.Children.Add(hitArea);
                        waveformCanvas.Children.Add(playheadLine);
                    }
                }
                else
                {
                    // Если нет семплов, всё равно добавляем playhead с учетом минимальной ширины
                    double playheadX = currentTime * pixelsPerSecond;
                    if (playheadX >= 0)
                    {
                        var playheadLine = new Line
                        {
                            X1 = playheadX, Y1 = 0,
                            X2 = playheadX, Y2 = TrackHeight,
                            Stroke = new SolidColorBrush(Color.FromRgb(255, 50, 50)),
                            StrokeThickness = 3,
                            IsHitTestVisible = true,
                            Cursor = Cursors.SizeWE,
                            Tag = "playhead"
                        };
                        
                        RegisterPlayheadLine(track.TrackIndex, playheadLine);
                        
                        var hitArea = new Rectangle
                        {
                            Width = 20,
                            Height = TrackHeight,
                            Fill = Brushes.Transparent,
                            Tag = "playhead"
                        };
                        Canvas.SetLeft(hitArea, playheadX - 10);
                        Canvas.SetTop(hitArea, 0);
                        
                        waveformCanvas.Children.Add(hitArea);
                        waveformCanvas.Children.Add(playheadLine);
                    }
                }
            }
            Log("UpdateTrackLabels: done");
            }
            catch (Exception ex)
            {
                Log($"UpdateTrackLabels error: {ex.Message}");
            }
            finally
            {
                _trackLabelsUpdating = false;
            }
        }
        
        private void DrawWaveformInCanvas(Canvas canvas, AudioClip clip, double width)
        {
            if (clip.Samples == null || clip.Samples.Length == 0) return;
            
            double displayWidth = Math.Max(1, width);
            Log($"DrawWaveformInCanvas: track={clip.TrackIndex}, width={displayWidth:F0}, spectrogram={_showSpectrogram}");
            
            if (_showSpectrogram)
            {
                DrawSpectrogramInCanvas(canvas, clip, displayWidth);
                return;
            }
            
            // Пики считаются один раз при загрузке (макс 5000) и кэшируются
            if (!_waveformPeaks.TryGetValue(clip.TrackIndex, out float[] peaks))
            {
                peaks = ComputePeaks(clip.Samples, 5000);
                _waveformPeaks[clip.TrackIndex] = peaks;
            }
            
            DrawWaveformFromPeaks(canvas, peaks, displayWidth, clip.TrackIndex);
        }
        
        // FL Studio-style waveform (vector-based, anti-aliased)
        private void DrawWaveformFromPeaks(Canvas canvas, float[] peaks, double width, int trackIndex)
        {
            if (peaks == null || peaks.Length == 0) return;

            double h = TrackHeight - 4;
            double centerY = h / 2;
            double scale = centerY * 1.2;
            int n = Math.Min(peaks.Length, Math.Max(100, (int)width));
            double xStep = width / n;

            // Remove old waveform paths, keep playhead/end lines
            var toRemove = canvas.Children.OfType<System.Windows.Shapes.Path>().ToList();
            foreach (var p in toRemove) canvas.Children.Remove(p);

            var accentColor = Color.FromRgb(255, 120, 129);
            var upperPath = new PathGeometry();
            var upperFig = new PathFigure { StartPoint = new Point(0, centerY) };
            upperFig.Segments.Add(new LineSegment(new Point(0, centerY), true));

            for (int i = 0; i < n; i++)
            {
                int idx = (int)((double)i / n * peaks.Length);
                float peak = peaks[Math.Min(idx, peaks.Length - 1)];
                double x = i * xStep;
                double y = centerY - peak * scale;
                upperFig.Segments.Add(new LineSegment(new Point(x, y), true));
            }
            upperFig.Segments.Add(new LineSegment(new Point(width - xStep, centerY), true));
            upperFig.Segments.Add(new LineSegment(new Point(0, centerY), true));
            upperPath.Figures.Add(upperFig);

            var lowerPath = new PathGeometry();
            var lowerFig = new PathFigure { StartPoint = new Point(0, centerY) };
            lowerFig.Segments.Add(new LineSegment(new Point(0, centerY), true));
            for (int i = 0; i < n; i++)
            {
                int idx = (int)((double)i / n * peaks.Length);
                float peak = peaks[Math.Min(idx, peaks.Length - 1)];
                double x = i * xStep;
                double y = centerY + peak * scale;
                lowerFig.Segments.Add(new LineSegment(new Point(x, y), true));
            }
            lowerFig.Segments.Add(new LineSegment(new Point(width - xStep, centerY), true));
            lowerFig.Segments.Add(new LineSegment(new Point(0, centerY), true));
            lowerPath.Figures.Add(lowerFig);

            canvas.Children.Insert(0, new System.Windows.Shapes.Path
            {
                Data = upperPath,
                Fill = new SolidColorBrush(Color.FromArgb(180, 255, 120, 129)),
                Opacity = 0.9
            });
            canvas.Children.Insert(0, new System.Windows.Shapes.Path
            {
                Data = lowerPath,
                Fill = new SolidColorBrush(Color.FromArgb(100, 255, 120, 129)),
                Opacity = 0.6
            });
        }

        // ========== Spectrogram rendering with caching ==========
        private void EnsureSpectrogramCache(AudioClip clip, out float[] data, out int numFrames, out int fftSize)
        {
            int trackIdx = clip.TrackIndex;
            if (_spectrogramCache.TryGetValue(trackIdx, out float[] cached))
            {
                fftSize = (int)cached[0];
                numFrames = (int)cached[1];
                data = cached;
                return;
            }

            fftSize = 512;
            int m = (int)Math.Round(Math.Log(fftSize, 2));
            int hop = fftSize / 2;
            int channels = clip.Channels;
            int totalFrames = clip.Samples.Length / channels;
            numFrames = Math.Max(1, (totalFrames - fftSize) / hop + 1);
            int targetFrames = 20000; // соответствие макс ширине спектрограммы
            int frameStepFFT = Math.Max(1, numFrames / targetFrames);
            numFrames = Math.Min(numFrames, targetFrames);
            int bins = fftSize / 2;

            float[] specData = new float[2 + numFrames * bins];
            specData[0] = fftSize;
            specData[1] = numFrames;

            Complex[] cbuf = new Complex[fftSize];
            float[] window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
                window[i] = 0.5f * (1 - (float)Math.Cos(2 * Math.PI * i / (fftSize - 1)));

            for (int f = 0; f < numFrames; f++)
            {
                int frameIdx = f * frameStepFFT;
                int startSample = frameIdx * hop * channels;
                if (startSample + fftSize * channels > clip.Samples.Length) break;

                for (int i = 0; i < fftSize; i++)
                {
                    float s = 0;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int idx = startSample + i * channels + ch;
                        if (idx < clip.Samples.Length) s += clip.Samples[idx];
                    }
                    cbuf[i].X = (s / channels) * window[i];
                    cbuf[i].Y = 0;
                }

                FastFourierTransform.FFT(true, m, cbuf);

                for (int b = 0; b < bins; b++)
                {
                    float mag = (float)Math.Sqrt(cbuf[b].X * cbuf[b].X + cbuf[b].Y * cbuf[b].Y);
                    float db = 20 * (float)Math.Log10(Math.Max(mag, 1e-10f));
                    specData[2 + f * bins + b] = db;
                }
            }

            _spectrogramCache[trackIdx] = specData;
            _spectrogramDirty.Add(trackIdx);
            data = specData;
        }

        private void DrawSpectrogramInCanvas(Canvas canvas, AudioClip clip, double width)
        {
            if (clip.Samples == null || clip.Samples.Length == 0) return;

            int trackIdx = clip.TrackIndex;
            double displayWidth = Math.Max(1, Math.Min(width, 100000));
            int w = Math.Max(1, Math.Min((int)displayWidth, 100000));
            int h = Math.Max(1, TrackHeight - 4);

            EnsureSpectrogramCache(clip, out float[] cacheEntry, out int numFrames, out int fftSize);
            int bins = fftSize / 2;
            int dataOffset = 2;
            bool isDirty = _spectrogramDirty.Remove(trackIdx);
            Log($"DrawSpectrogram: trackIdx={trackIdx}, w={w}, h={h}, isDirty={isDirty}");

            System.Windows.Controls.Image? specImage = null;
            foreach (var child in canvas.Children)
            {
                if (child is System.Windows.Controls.Image img && img.Tag?.ToString() == "spec")
                {
                    specImage = img;
                    break;
                }
            }

            try
            {
                // Use bitmap cache: reuse existing bitmap across canvas recreations
                bool needsRender = false;
                WriteableBitmap bmp;
                if (_spectrogramBitmaps.TryGetValue(trackIdx, out WriteableBitmap? cached) &&
                    cached.PixelWidth == w && cached.PixelHeight == h)
                {
                    bmp = cached;
                    needsRender = isDirty;
                }
                else
                {
                    bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                    _spectrogramBitmaps[trackIdx] = bmp;
                    needsRender = true;
                }

                // Update Image source (canvas may have been recreated)
                if (specImage != null)
                {
                    specImage.Source = bmp;
                    specImage.Width = w;
                    specImage.Height = h;
                }
                else
                {
                    specImage = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Width = w,
                        Height = h,
                        Tag = "spec"
                    };
                    Canvas.SetLeft(specImage, 0);
                    Canvas.SetTop(specImage, 2);
                    canvas.Children.Insert(0, specImage);
                }

                if (w < width)
                    canvas.Background = new SolidColorBrush(Color.FromRgb(13, 0, 5));
                else
                    canvas.Background = null;

                if (!needsRender)
                    return;

                bool locked = false;
                try
                {
                    bmp.Lock();
                    locked = true;
                    unsafe
                    {
                        IntPtr buffer = bmp.BackBuffer;
                        int* pixels = (int*)buffer.ToPointer();
                        int stride = bmp.BackBufferStride;

                        int bgColor = unchecked((int)0xFF0D0005);
                        for (int i = 0; i < w * h; i++)
                            pixels[i] = bgColor;

                        double minDb = -80, maxDb = 0;
                        double frameStep = Math.Max(1, (double)numFrames / w);
                        double binStep = (double)bins / h;

                        for (int x = 0; x < w; x++)
                        {
                            int fIdx = (int)(x * frameStep);
                            if (fIdx >= numFrames) fIdx = numFrames - 1;

                            for (int y = 0; y < h; y++)
                            {
                                int bIdx = (int)(y * binStep);
                                if (bIdx >= bins) bIdx = bins - 1;

                                float db = cacheEntry[dataOffset + fIdx * bins + bIdx];
                                float norm = (float)((db - minDb) / (maxDb - minDb));
                                if (norm < 0) norm = 0;
                                if (norm > 1) norm = 1;

                                int pixelY = h - 1 - y;
                                pixels[pixelY * (stride / 4) + x] = HeatMapColor(norm);
                            }
                        }

                        bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
                    }
                }
                finally
                {
                    if (locked) bmp.Unlock();
                }
            }
            catch { }
        }

        private static int HeatMapColor(float t)
        {
            byte r, g, b;
            if (t < 0.25f)
            {
                // #0D0004 -> #3D0014
                float v = t / 0.25f;
                r = (byte)(13 + v * 48);
                g = (byte)(v * 0);
                b = (byte)(4 + v * 16);
            }
            else if (t < 0.5f)
            {
                // #3D0014 -> #7A0D2E
                float v = (t - 0.25f) / 0.25f;
                r = (byte)(61 + v * 61);
                g = (byte)(0 + v * 13);
                b = (byte)(20 + v * 26);
            }
            else if (t < 0.75f)
            {
                // #7A0D2E -> #BF4058
                float v = (t - 0.5f) / 0.25f;
                r = (byte)(122 + v * 69);
                g = (byte)(13 + v * 51);
                b = (byte)(46 + v * 42);
            }
            else
            {
                // #BF4058 -> #FFA070
                float v = (t - 0.75f) / 0.25f;
                r = (byte)(191 + v * 64);
                g = (byte)(64 + v * 96);
                b = (byte)(88 + v * 24);
            }
            return (255 << 24) | (b << 16) | (g << 8) | r;
        }

        // ========== Browser/Tracks Splitter ==========
        private const double BrowserPanelMinWidth = 150;
        private const double WorkspaceMinWidth = 200;
        private const double SplitterColumnWidth = 4;

        private Point _splitterStart;
        private double _splitterStartWidth;
        private bool _isSplitterDragging = false;

        private double GetMaxBrowserWidth()
        {
            double areaW = MainWorkspaceGrid?.ActualWidth ?? ActualWidth;
            if (areaW <= 0) areaW = ActualWidth;
            return Math.Max(BrowserPanelMinWidth, areaW - SplitterColumnWidth - WorkspaceMinWidth);
        }

        private void ClampBrowserPanelWidth()
        {
            if (BrowserColumn.Width.IsAbsolute)
            {
                double clamped = Math.Max(BrowserPanelMinWidth,
                    Math.Min(BrowserColumn.Width.Value, GetMaxBrowserWidth()));
                if (Math.Abs(clamped - BrowserColumn.Width.Value) > 0.5)
                    BrowserColumn.Width = new GridLength(clamped);
            }
        }
        
        private void Splitter_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                _isSplitterDragging = true;
                _splitterStart = e.GetPosition(this);
                _splitterStartWidth = BrowserColumn.Width.Value;
                
                var border = sender as Border;
                if (border != null)
                {
                    border.CaptureMouse();
                    border.MouseMove += Splitter_MouseMove;
                    border.MouseLeftButtonUp += Splitter_MouseLeftButtonUp;
                }
                
                e.Handled = true;
            }
        }
        
        private void Splitter_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isSplitterDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                var dx = pos.X - _splitterStart.X;
                var newWidth = _splitterStartWidth + dx;
                
                newWidth = Math.Max(BrowserPanelMinWidth, newWidth);
                newWidth = Math.Min(GetMaxBrowserWidth(), newWidth);

                BrowserColumn.Width = new GridLength(newWidth);
                PlaylistViewControl?.RefreshViewportLayout();
                SyncTimeRuler();
            }
        }
        
        private void Splitter_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isSplitterDragging = false;
            
            var border = sender as Border;
            if (border != null)
            {
                border.MouseMove -= Splitter_MouseMove;
                border.MouseLeftButtonUp -= Splitter_MouseLeftButtonUp;
                border.ReleaseMouseCapture();
            }
            
            PlaylistViewControl?.RefreshViewportLayout();
            SyncTimeRuler();
            DrawTimeline(rebuildTracks: true);
            e.Handled = true;
        }
        
        // ========== GridSplitter Handlers ==========
        private bool _isLayoutUpdating = false;
        
        private void GridSplitter_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isLayoutUpdating = true;
        }
        
        private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            // GridSplitter автоматически изменяет размеры BrowserColumn
        }
        
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isLayoutUpdating = false;
            DrawTimeline(rebuildTracks: true);
        }
        
        public void DrawTimeline(bool rebuildTracks = false)
        {
            try
            {
                if (rebuildTracks)
                    UpdateTrackLabels();
                TimeRulerControl.TotalDuration = GetTotalDuration();
                TimeRulerControl.UpdateTicks();
            }
            catch (Exception ex)
            {
                Log($"DrawTimeline error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"DrawTimeline error: {ex}");
            }
        }

        private static double[] _niceSteps = { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 20, 30, 60, 120, 300, 600, 1800, 3600 };

        private static double NiceTimeStep(double pps)
        {
            double rough = 80.0 / pps;
            for (int i = 0; i < _niceSteps.Length - 1; i++)
                if (rough <= Math.Sqrt(_niceSteps[i] * _niceSteps[i + 1]))
                    return _niceSteps[i];
            return _niceSteps[^1];
        }

        private void UpdateZoomLayout(bool redrawContent = true)
        {
            Log($"UpdateZoomLayout: start (redrawContent={redrawContent})");
            try
            {
                double viewportW = TracksScroller.ViewportWidth;

                double majorStep = NiceTimeStep(pixelsPerSecond);
                double minorStep = majorStep / 5;

                var gridMajor = new SolidColorBrush(Color.FromArgb(40, 180, 180, 180));
                var gridMinor = new SolidColorBrush(Color.FromArgb(18, 150, 150, 150));

                foreach (var child in TracksPanel.Children)
                {
                    if (child is not Grid trackRow) continue;
                    int trackIdx = (int)trackRow.Tag;
                    var track = tracks.FirstOrDefault(t => t.TrackIndex == trackIdx);
                    if (track == null || track.Samples.Length == 0) continue;

                    double contentWidth = track.Duration * pixelsPerSecond;
                    double canvasWidth = Math.Max(contentWidth, viewportW);

                    foreach (var rowChild in trackRow.Children)
                    {
                        if (rowChild is not Border border) continue;
                        if (Grid.GetColumn((UIElement)rowChild) != 1) continue;

                        border.Width = canvasWidth;
                        if (border.Child is Canvas canvas)
                        {
                            canvas.Width = canvasWidth;
                            if (redrawContent)
                                DrawWaveformInCanvas(canvas, track, contentWidth);

                            if (!_showSpectrogram)
                            {
                                foreach (var c in canvas.Children)
                                {
                                    if (c is System.Windows.Controls.Image img && img.Tag?.ToString() == "spec")
                                        canvas.Children.Remove(img);
                                }
                            }

                            // Draw grid lines (always, fast)
                            var oldGrid = canvas.Children.OfType<System.Windows.Shapes.Line>().Where(l => l.Tag?.ToString() == "grid").ToList();
                            foreach (var line in oldGrid) canvas.Children.Remove(line);

                            double maxTime = canvasWidth / pixelsPerSecond;
                            for (double t = 0; t <= maxTime + 0.0001; t += minorStep)
                            {
                                double x = t * pixelsPerSecond;
                                bool isMajor = Math.Abs(t % majorStep) < majorStep * 0.001;
                                canvas.Children.Add(new System.Windows.Shapes.Line
                                {
                                    X1 = x, X2 = x,
                                    Y1 = 0, Y2 = TrackHeight,
                                    Stroke = isMajor ? gridMajor : gridMinor,
                                    StrokeThickness = 0.5,
                                    Tag = "grid"
                                });
                            }
                        }
                    }

                    if (_endOfTrackLines.TryGetValue(trackIdx, out Line? endLine))
                    {
                        double endX = track.Duration * pixelsPerSecond;
                        endLine.X1 = endX;
                        endLine.X2 = endX;
                    }

                    if (_playheadLines.TryGetValue(trackIdx, out List<Line>? lines))
                    {
                        foreach (var line in lines)
                        {
                            double phX = currentTime * pixelsPerSecond;
                            line.X1 = phX;
                            line.X2 = phX;
                        }
                    }
                }

                TimeRulerControl.TotalDuration = GetTotalDuration();
                if (redrawContent)
                    TimeRulerControl.UpdateTicks();
                else
                    TimeRulerControl.RefreshScroll();
                Log("UpdateZoomLayout: done");
            }
            catch (Exception ex)
            {
                Log($"UpdateZoomLayout error: {ex}");
            }
        }

        private bool HasPlayableContent() =>
            _playlistViewModel.AudioClips.Any(c => !string.IsNullOrEmpty(c.FilePath)) ||
            tracks.Any(t => t.Samples.Length > 0);

        private double GetClipPlaybackEndTick(Models.TrackItemViewModel clip)
        {
            double endTick = clip.EndTick;
            if (!_clipSamplesCache.TryGetValue(clip.Id, out var samples) || samples.Length == 0)
                return endTick;

            int denom = clip.SampleRate * Math.Max(1, clip.Channels);
            if (denom <= 0) return endTick;

            double sampleTicks = _playlistViewModel.SecondsToTick(samples.Length / (double)denom);
            return Math.Max(endTick, clip.StartTick + sampleTicks);
        }

        private double GetContentDuration()
        {
            if (_playlistViewModel.AudioClips.Any(c => !string.IsNullOrEmpty(c.FilePath)))
            {
                double maxTicks = 0;
                foreach (var clip in _playlistViewModel.AudioClips)
                {
                    double end = GetClipPlaybackEndTick(clip);
                    if (end > maxTicks) maxTicks = end;
                }
                return _playlistViewModel.TickToSeconds(maxTicks);
            }

            double max = 0;
            foreach (var track in tracks)
            {
                double end = track.StartTime + track.Duration;
                if (end > max) max = end;
            }
            return max;
        }

        private double GetTotalDuration()
        {
            double content = GetContentDuration();
            double timeline = PlaylistViewControl?.GetTimelineDurationSeconds() ?? 10;
            return Math.Max(Math.Max(content, timeline), 10);
        }

        private void OnPlaylistClipsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Models.TrackItemViewModel item in e.OldItems)
                    _clipSamplesCache.Remove(item.Id);
            }
        }

        private float[] LoadClipSamples(Models.TrackItemViewModel clip)
        {
            if (_clipSamplesCache.TryGetValue(clip.Id, out var cached))
                return cached;

            if (string.IsNullOrEmpty(clip.FilePath) || !File.Exists(clip.FilePath))
                return Array.Empty<float>();

            try
            {
                var (samples, _, _, _) = ReadAudioFile(clip.FilePath);
                _clipSamplesCache[clip.Id] = samples;
                return samples;
            }
            catch
            {
                return Array.Empty<float>();
            }
        }

        private static (float[] Samples, int SampleRate, int Channels, double DurationSeconds) ReadAudioFile(string filePath)
        {
            using var reader = new AudioFileReader(filePath);
            int sampleRate = reader.WaveFormat.SampleRate;
            int channels = Math.Max(1, reader.WaveFormat.Channels);
            int estimated = (int)(reader.TotalTime.TotalSeconds * sampleRate * channels);
            estimated = Math.Clamp(estimated, 4096, 50_000_000);
            var allSamples = new List<float>(estimated);
            var buffer = new float[131072];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                allSamples.AddRange(buffer.AsSpan(0, read));
            var samples = allSamples.ToArray();
            double durationSec = samples.Length / (double)(sampleRate * channels);
            return (samples, sampleRate, channels, durationSec);
        }

        private sealed record PlaylistClipPayload(Models.TrackItemViewModel Clip, float[] Samples, float[] Peaks);

        private PlaylistClipPayload? BuildPlaylistClipPayload(
            string filePath,
            double tickPos,
            int trackIndex,
            bool autoPlace = true,
            Models.TrackItemViewModel? template = null)
        {
            try
            {
                var (samples, sampleRate, channels, durationSec) = ReadAudioFile(filePath);
                var peaks = ComputePeaksForClip(samples, 5000);

                var clip = new Models.TrackItemViewModel
                {
                    Id = template?.Id ?? Guid.NewGuid(),
                    Name = template?.Name ?? Path.GetFileName(filePath),
                    FilePath = filePath,
                    SampleRate = template?.SampleRate ?? sampleRate,
                    Channels = template?.Channels ?? channels,
                    SourceDurationSeconds = durationSec,
                    TrackIndex = trackIndex
                };

                PlaylistIntegrity.EnsureDurationFromSamples(clip, samples, _playlistViewModel);

                if (autoPlace)
                    _playlistViewModel.ResolveClipPlacement(clip, tickPos, trackIndex);
                else
                {
                    clip.TrackIndex = Math.Clamp(trackIndex, 0, Math.Max(0, _playlistViewModel.NumTracks - 1));
                    clip.StartTick = _playlistViewModel.SnapToGrid(tickPos);
                }

                return new PlaylistClipPayload(clip, samples, peaks);
            }
            catch
            {
                return null;
            }
        }

        private HashSet<Guid> ReconcileClipDurationsFromSamples()
        {
            var changed = new HashSet<Guid>();
            foreach (var clip in _playlistViewModel.AudioClips)
            {
                if (!_clipSamplesCache.TryGetValue(clip.Id, out var samples) || samples.Length == 0)
                    continue;

                int denom = clip.SampleRate * Math.Max(1, clip.Channels);
                if (denom <= 0) continue;

                double sampleTicks = _playlistViewModel.SecondsToTick(samples.Length / (double)denom);
                if (Math.Abs(clip.DurationTicks - sampleTicks) <= 0.5)
                    continue;

                clip.DurationTicks = Math.Max(Models.PlaylistViewModel.PPQN / 4.0, sampleTicks);
                changed.Add(clip.Id);
            }
            return changed;
        }

        private void SyncPlaylistToEngine()
        {
            var clips = new List<AudioClipModel>();
            foreach (var item in _playlistViewModel.AudioClips)
            {
                if (!PlaylistIntegrity.IsClipFileAccessible(item))
                    continue;

                var samples = GetClipSamplesForDuration(item);
                if (!PlaylistIntegrity.HasPlayableAudio(item, samples))
                    continue;

                clips.Add(new AudioClipModel
                {
                    Samples = samples,
                    StartTime = (float)_playlistViewModel.TickToSeconds(item.StartTick),
                    Duration = (float)_playlistViewModel.TickToSeconds(item.DurationTicks),
                    SampleRate = item.SampleRate,
                    Channels = item.Channels,
                    Name = item.Name,
                    TrackIndex = item.TrackIndex,
                    Color = item.Color
                });
            }

            _audio.LoadClips(clips);
            _audio.Seek((float)currentTime);
        }

        private void OnPlaylistClipSelected(Models.TrackItemViewModel clip)
        {
            selectedTrackIndex = clip.TrackIndex;
            focusedClipIndex = clip.TrackIndex;
            int count = PlaylistViewControl?.SelectedClipCount ?? 1;
            if (count > 1)
            {
                var reason = GetMergeDisabledReason();
                SetStatusText(reason == null
                    ? $"Выбрано клипов: {count} — ПКМ → Склеить"
                    : $"Выбрано клипов: {count}");
            }
            else
            {
                SetStatusText($"Выбран клип: {clip.Name}");
            }
            UpdateInstrumentsWindow();
            EnableControls(true);
            _contextMenu?.UpdateMenuState();
        }

        private void OnPlaylistEmptyAreaInteracted(int trackIndex, double tick, bool isRightButton)
        {
            ClearSelection();
            selectedTrackIndex = trackIndex;
            focusedClipIndex = trackIndex;
            currentTime = _playlistViewModel.TickToSeconds(tick);
            SeekToTime(currentTime);
            PlaylistViewControl.SetPlayheadTime(currentTime);
            EnableControls(true);

            if (isRightButton)
                ShowPlaylistContextMenu(onClip: false, onEmptyTrack: true);
        }

        private void ShowPlaylistContextMenu(bool onClip = false, bool onEmptyTrack = false)
        {
            _contextMenu?.SetClipHintsVisible(onClip);
            _contextMenu?.SetEmptyTrackMode(onEmptyTrack);
            _contextMenu?.UpdateMenuState();
            _contextMenu!.IsOpen = true;
        }

        private void OnPlaylistClipRangeSelected(Models.TrackItemViewModel clip, double startSec, double endSec)
        {
            SetPlaylistTimeSelection(clip.Id, startSec, endSec);
        }

        private void SetPlaylistTimeSelection(Guid clipId, double startSec, double endSec)
        {
            _playlistRangeClipId = clipId;
            _playlistRangeStartSec = Math.Min(startSec, endSec);
            _playlistRangeEndSec = Math.Max(startSec, endSec);

            PlaylistViewControl.SetTimeSelection(clipId, _playlistRangeStartSec, _playlistRangeEndSec);
            SelectionManager.SelectionStart = _playlistRangeStartSec;
            SelectionManager.SelectionEnd = _playlistRangeEndSec;

            if (TimeRulerControl != null)
            {
                TimeRulerControl.SelectionStart = _playlistRangeStartSec;
                TimeRulerControl.SelectionEnd = _playlistRangeEndSec;
                TimeRulerControl.UpdateSelectionHighlight();
            }

            var clipVm = _playlistViewModel.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (clipVm != null)
            {
                selectedTrackIndex = clipVm.TrackIndex;
                focusedClipIndex = clipVm.TrackIndex;
            }
            SetStatusText($"Выделено: {FormatTime(_playlistRangeEndSec - _playlistRangeStartSec)}");
            EnableControls(true);
        }

        private bool TryGetPlaylistSampleRange(
            out Models.TrackItemViewModel? item, out int startSample, out int length)
        {
            item = null;
            startSample = 0;
            length = 0;
            if (!HasPlaylistTimeSelection() || !_playlistRangeClipId.HasValue) return false;

            item = _playlistViewModel.AudioClips.FirstOrDefault(c => c.Id == _playlistRangeClipId.Value);
            if (item == null) return false;

            var samples = LoadClipSamples(item);
            if (samples.Length == 0) return false;

            double clipStart = _playlistViewModel.TickToSeconds(item.StartTick);
            double relStart = _playlistRangeStartSec - clipStart;
            double relEnd = _playlistRangeEndSec - clipStart;
            int rate = item.SampleRate * Math.Max(1, item.Channels);

            startSample = (int)(relStart * rate);
            int endSample = (int)(relEnd * rate);
            startSample = Math.Clamp(startSample, 0, samples.Length);
            endSample = Math.Clamp(endSample, startSample, samples.Length);
            length = endSample - startSample;
            return length > 0;
        }

        public void CommitPlaylistState(bool rebuildVisuals = false, IReadOnlySet<Guid>? forceFullRedrawIds = null)
        {
            var durationChanged = ReconcileClipDurationsFromSamples();
            if (forceFullRedrawIds != null)
            {
                foreach (var id in forceFullRedrawIds)
                    durationChanged.Add(id);
            }
            PlaylistViewControl?.RefreshContentLayout(durationChanged);
            SyncTimeRuler();
            RebuildMixer();
            TotalTimeText.Text = FormatTime(GetTotalDuration());
            EnableControls(HasPlayableContent());
            UpdateCommandButtons();
            if (rebuildVisuals)
                PlaylistViewControl.InvalidateAll();
        }

        private void SyncPlaylistClipLayouts(IReadOnlySet<Guid>? forceFullRedrawIds = null)
        {
            forceFullRedrawIds ??= new HashSet<Guid>();
            foreach (var clip in _playlistViewModel.AudioClips)
            {
                bool fullRedraw = forceFullRedrawIds.Contains(clip.Id);
                PlaylistViewControl.UpdateClipLayout(clip, layoutOnly: !fullRedraw);
            }
        }

        public void RefreshPlaylistAfterEdit(bool rebuildVisuals = false) =>
            CommitPlaylistState(rebuildVisuals);

        private List<(Models.TrackItemViewModel Clip, float[] Samples)> RemoveClipsContainingDropPoint(
            int trackIndex,
            double dropTick,
            bool refresh = true)
        {
            var removed = new List<(Models.TrackItemViewModel, float[])>();
            foreach (var existing in PlaylistIntegrity
                         .GetClipsContainingPoint(_playlistViewModel, trackIndex, dropTick)
                         .ToList())
            {
                var samples = LoadClipSamples(existing);
                removed.Add((CloneClipMeta(existing), (float[])samples.Clone()));
                RemovePlaylistClipInternal(existing.Id, refresh: false);
            }

            if (refresh && removed.Count > 0)
                CommitPlaylistState();
            return removed;
        }

        public void ApplyPlaylistClipLayout(Guid clipId, double? startTick, int? trackIndex, double? durationTicks)
        {
            var clip = _playlistViewModel.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return;

            if (startTick.HasValue || trackIndex.HasValue)
            {
                _playlistViewModel.ResolveClipPlacement(
                    clip,
                    startTick ?? clip.StartTick,
                    trackIndex ?? clip.TrackIndex);
            }

            if (durationTicks.HasValue)
            {
                _playlistViewModel.ApplyClipResize(clip, durationTicks.Value);
            }

            HashSet<Guid>? redraw = durationTicks.HasValue ? new HashSet<Guid> { clipId } : null;
            CommitPlaylistState(forceFullRedrawIds: redraw);
        }

        public void InsertPlaylistClip(
            Models.TrackItemViewModel clip,
            float[] samples,
            List<(Models.TrackItemViewModel Clip, float[] Samples)>? replaced = null,
            bool refresh = true)
        {
            PlaylistIntegrity.NormalizeClipBounds(clip, _playlistViewModel);
            _clipSamplesCache[clip.Id] = (float[])samples.Clone();
            PlaylistIntegrity.EnsureDurationFromSamples(clip, samples, _playlistViewModel);

            if (_playlistViewModel.AudioClips.All(c => c.Id != clip.Id))
            {
                var peaks = ComputePeaksForClip(samples, 5000);
                PlaylistViewControl.InsertClip(clip, peaks);
            }
            else
            {
                RefreshPlaylistClipPeaks(clip);
                PlaylistViewControl.UpdateClipLayout(clip);
            }

            if (refresh)
                CommitPlaylistState();
        }

        public void SelectPlaylistClip(Guid clipId) =>
            PlaylistViewControl?.SelectClip(clipId, raiseEvent: true);

        public void SelectPlaylistClips(IEnumerable<Guid> clipIds) =>
            PlaylistViewControl?.SelectClips(clipIds, raiseEvent: true);

        public void RemovePlaylistClipInternal(Guid clipId, bool refresh = true)
        {
            _clipSamplesCache.Remove(clipId);
            PlaylistViewControl.RemoveClip(clipId);
            if (_playlistRangeClipId == clipId)
                ClearSelection();
            if (refresh)
                CommitPlaylistState();
        }

        public void SplicePlaylistSamplesInternal(Guid clipId, int startSample, float[] removed, bool saveToClipboard)
        {
            var item = _playlistViewModel.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (item == null || removed.Length == 0) return;

            var samples = LoadClipSamples(item);
            if (startSample + removed.Length > samples.Length) return;

            if (saveToClipboard)
                SetSampleClipboard(removed, item.SampleRate, item.Channels);

            var result = new float[samples.Length - removed.Length];
            Array.Copy(samples, 0, result, 0, startSample);
            Array.Copy(samples, startSample + removed.Length, result, startSample,
                samples.Length - startSample - removed.Length);
            _clipSamplesCache[clipId] = result;
            UpdateClipDurationFromSamples(item, result);
            RefreshPlaylistClipPeaks(item);
            CommitPlaylistState();
        }

        public void InsertPlaylistSamplesInternal(Guid clipId, int startSample, float[] data)
        {
            var item = _playlistViewModel.AudioClips.FirstOrDefault(c => c.Id == clipId);
            if (item == null || data.Length == 0) return;

            var samples = LoadClipSamples(item);
            startSample = Math.Clamp(startSample, 0, samples.Length);
            var result = new float[samples.Length + data.Length];
            Array.Copy(samples, 0, result, 0, startSample);
            Array.Copy(data, 0, result, startSample, data.Length);
            Array.Copy(samples, startSample, result, startSample + data.Length, samples.Length - startSample);
            _clipSamplesCache[clipId] = result;
            UpdateClipDurationFromSamples(item, result);
            RefreshPlaylistClipPeaks(item);
            CommitPlaylistState();
        }

        public void SetPlaylistClipboardWholeClip(Models.TrackItemViewModel clip, float[] samples, bool wasCut)
        {
            _playlistClipboard.Kind = Models.PlaylistClipboard.ContentKind.WholeClip;
            _playlistClipboard.Samples = (float[])samples.Clone();
            _playlistClipboard.SampleRate = clip.SampleRate;
            _playlistClipboard.Channels = clip.Channels;
            _playlistClipboard.Name = clip.Name;
            _playlistClipboard.FilePath = clip.FilePath;
            _playlistClipboard.DurationTicks = clip.DurationTicks;
            _playlistClipboard.WasCut = wasCut;
            ClipboardData = _playlistClipboard.Samples;
            ClipboardChannels = clip.Channels;
            ClipboardSampleRate = clip.SampleRate;
        }

        private void SetSampleClipboard(float[] samples, int sampleRate, int channels)
        {
            _playlistClipboard.Kind = Models.PlaylistClipboard.ContentKind.Samples;
            _playlistClipboard.Samples = (float[])samples.Clone();
            _playlistClipboard.SampleRate = sampleRate;
            _playlistClipboard.Channels = channels;
            _playlistClipboard.WasCut = false;
            ClipboardData = _playlistClipboard.Samples;
            ClipboardChannels = channels;
            ClipboardSampleRate = sampleRate;
        }

        private void UpdateClipDurationFromSamples(Models.TrackItemViewModel clip, float[] samples)
        {
            int rate = clip.SampleRate * Math.Max(1, clip.Channels);
            if (rate <= 0) return;
            double sec = samples.Length / (double)rate;
            clip.DurationTicks = Math.Max(Models.TrackItemViewModel.PPQN / 4.0,
                _playlistViewModel.SecondsToTick(sec));
        }

        private void OnPlaylistClipMoved(Models.TrackItemViewModel clip,
            double oldTick, int oldTrack, double newTick, int newTrack)
        {
            CommitPlaylistState();
            if (Math.Abs(oldTick - newTick) < 0.01 && oldTrack == newTrack)
                return;
            _commandManager.Record(new Commands.MovePlaylistClipCommand(
                this, clip.Id, oldTick, oldTrack, newTick, newTrack));
        }

        private void OnPlaylistClipsMoved(
            IReadOnlyList<(Models.TrackItemViewModel Clip, double OldTick, int OldTrack, double NewTick, int NewTrack)> moves)
        {
            CommitPlaylistState();
            var actual = moves
                .Where(m => Math.Abs(m.OldTick - m.NewTick) >= 0.01 || m.OldTrack != m.NewTrack)
                .Select(m => (m.Clip.Id, m.OldTick, m.OldTrack, m.NewTick, m.NewTrack))
                .ToList();
            if (actual.Count == 0) return;
            _commandManager.Record(new Commands.MovePlaylistClipsCommand(this, actual));
        }

        private void OnPlaylistClipResized(Models.TrackItemViewModel clip, double oldDur, double newDur)
        {
            CommitPlaylistState();
            if (Math.Abs(oldDur - newDur) < 0.01)
                return;
            _commandManager.Record(new Commands.ResizePlaylistClipCommand(
                this, clip.Id, oldDur, newDur));
        }

        private Models.TrackItemViewModel CloneClipMeta(Models.TrackItemViewModel c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            FilePath = c.FilePath,
            SampleRate = c.SampleRate,
            Channels = c.Channels,
            SourceDurationSeconds = c.SourceDurationSeconds,
            StartTick = c.StartTick,
            DurationTicks = c.DurationTicks,
            TrackIndex = c.TrackIndex,
            Color = c.Color
        };

        private Models.TrackItemViewModel NewClipFromClipboard(double startTick, int trackIndex)
        {
            var cb = _playlistClipboard;
            var clip = new Models.TrackItemViewModel
            {
                Id = Guid.NewGuid(),
                Name = cb.Name,
                FilePath = cb.FilePath,
                SampleRate = cb.SampleRate,
                Channels = cb.Channels,
                SourceDurationSeconds = cb.Samples.Length / (double)(cb.SampleRate * Math.Max(1, cb.Channels)),
                DurationTicks = cb.DurationTicks > 0
                    ? cb.DurationTicks
                    : _playlistViewModel.SecondsToTick(cb.Samples.Length / (double)(cb.SampleRate * Math.Max(1, cb.Channels))),
                Color = "#FF7881FF"
            };
            _playlistViewModel.ResolveClipPlacement(clip, startTick, trackIndex);
            return clip;
        }

        private Models.TrackItemViewModel? GetSelectedPlaylistClip() =>
            PlaylistViewControl?.GetSelectedClip();

        private AudioClip? BuildAudioClipFromPlaylist(Models.TrackItemViewModel item)
        {
            var samples = LoadClipSamples(item);
            if (samples.Length == 0) return null;

            return new AudioClip
            {
                Samples = samples,
                SampleRate = item.SampleRate,
                Channels = item.Channels,
                Name = item.Name,
                SourceFile = item.FilePath,
                TrackIndex = item.TrackIndex
            };
        }

        private void RefreshPlaylistClipPeaks(Models.TrackItemViewModel clip)
        {
            var samples = LoadClipSamples(clip);
            if (samples.Length == 0) return;
            PlaylistViewControl.UpdatePeaks(clip.Id, ComputePeaksForClip(samples, 5000));
        }

        private static float[] ComputePeaksForClip(float[] samples, int targetCount)
        {
            if (samples.Length == 0) return Array.Empty<float>();
            var peaks = new float[targetCount];
            int step = Math.Max(1, samples.Length / targetCount);
            for (int i = 0; i < targetCount; i++)
            {
                float max = 0;
                int start = i * step;
                int end = Math.Min(start + step, samples.Length);
                for (int j = start; j < end; j++)
                {
                    float abs = Math.Abs(samples[j]);
                    if (abs > max) max = abs;
                }
                peaks[i] = max;
            }
            return peaks;
        }

        private void DeleteSelectedPlaylistClip()
        {
            var clips = PlaylistViewControl?.GetSelectedClips().ToList();
            if (clips == null || clips.Count == 0) return;

            var snapshots = clips
                .Select(c => (CloneClipMeta(c), (float[])LoadClipSamples(c).Clone()))
                .ToList();

            _commandManager.Execute(new Commands.RemovePlaylistClipsCommand(this, snapshots));

            SetStatusText(clips.Count == 1
                ? $"Удалён клип: {clips[0].Name}"
                : $"Удалено клипов: {clips.Count}");
        }

        private float[] GetClipSamplesForDuration(Models.TrackItemViewModel clip)
        {
            if (!TryGetCachedClipSamples(clip, out var samples))
                return Array.Empty<float>();

            int frameRate = clip.SampleRate * Math.Max(1, clip.Channels);
            if (frameRate <= 0) return Array.Empty<float>();

            int maxFrames = (int)Math.Round(_playlistViewModel.TickToSeconds(clip.DurationTicks) * frameRate);
            maxFrames = Math.Clamp(maxFrames, 0, samples.Length);
            if (maxFrames == samples.Length) return samples;

            var trimmed = new float[maxFrames];
            Array.Copy(samples, trimmed, maxFrames);
            return trimmed;
        }

        private (Models.TrackItemViewModel merged, float[] samples)? BuildMergedClipFromSelection()
        {
            if (!CanMergeSelectedPlaylistClips()) return null;

            var clips = PlaylistViewControl!.GetSelectedClips().ToList();
            double startTick = clips[0].StartTick;
            double endTick = clips.Max(c => c.EndTick);
            double totalDurationTicks = endTick - startTick;

            int sr = clips[0].SampleRate;
            int ch = Math.Max(1, clips[0].Channels);
            int frameRate = sr * ch;

            int totalFrames = (int)Math.Round(_playlistViewModel.TickToSeconds(totalDurationTicks) * frameRate);
            totalFrames = Math.Max(totalFrames, 1);
            var mergedSamples = new float[totalFrames];

            foreach (var clip in clips)
            {
                var clipSamples = GetClipSamplesForDuration(clip);
                if (clipSamples.Length == 0) continue;

                double offsetSec = _playlistViewModel.TickToSeconds(clip.StartTick - startTick);
                int offsetFrames = (int)Math.Round(offsetSec * frameRate);
                offsetFrames = Math.Clamp(offsetFrames, 0, Math.Max(0, totalFrames - 1));

                int copyLen = Math.Min(clipSamples.Length, totalFrames - offsetFrames);
                if (copyLen > 0)
                    Array.Copy(clipSamples, 0, mergedSamples, offsetFrames, copyLen);
            }

            string name = clips.Count == 2
                ? $"{clips[0].Name} + {clips[1].Name}"
                : $"Склеено ({clips.Count})";

            var mergedClip = new Models.TrackItemViewModel
            {
                Id = Guid.NewGuid(),
                Name = name,
                FilePath = clips[0].FilePath,
                SampleRate = sr,
                Channels = ch,
                SourceDurationSeconds = totalFrames / (double)frameRate,
                StartTick = startTick,
                DurationTicks = totalDurationTicks,
                TrackIndex = clips[0].TrackIndex,
                Color = clips[0].Color
            };

            return (mergedClip, mergedSamples);
        }

        public void MergeSelectedClips_Click(object sender, RoutedEventArgs e)
        {
            var reason = GetMergeDisabledReason();
            if (reason != null)
            {
                MessageBox.Show(reason, "Склеить клипы", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var built = BuildMergedClipFromSelection();
            if (built == null)
            {
                MessageBox.Show("Не удалось подготовить склеенный клип.", "Склеить клипы",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (mergedClip, mergedSamples) = built.Value;

            var removedClips = new List<Models.TrackItemViewModel>();
            var removedSamples = new List<float[]>();

            foreach (var clip in PlaylistViewControl!.GetSelectedClips())
            {
                removedClips.Add(CloneClipMeta(clip));
                removedSamples.Add((float[])GetClipSamplesForDuration(clip).Clone());
            }

            _commandManager.Execute(new Commands.MergePlaylistClipsCommand(
                this, removedClips, removedSamples, mergedClip, mergedSamples));

            SyncPlaylistToEngine();
            PlaylistViewControl.InvalidateAll();
            PlaylistViewControl.SelectClip(mergedClip.Id, raiseEvent: true);
            SetStatusText($"Склеено клипов: {removedClips.Count} (Ctrl+Z — отменить)");
            EnableControls(true);
        }

        private void EnqueuePlaylistFiles(IReadOnlyList<string> paths, int trackIndex)
        {
            if (paths.Count == 0)
                return;
            AddPlaylistFileSequential(paths, trackIndex, 0);
        }

        private void AddPlaylistFileSequential(IReadOnlyList<string> paths, int trackIndex, int index)
        {
            if (index >= paths.Count)
                return;

            if (!TryAddFileToPlaylist(paths[index], 0, trackIndex, onCompleted: () =>
                    AddPlaylistFileSequential(paths, trackIndex, index + 1)))
            {
                AddPlaylistFileSequential(paths, trackIndex, index + 1);
            }
        }

        private bool TryAddFileToPlaylist(
            string path,
            double tickPos = 0,
            int? trackIndex = null,
            Action? onCompleted = null,
            bool replaceClipsAtDropPoint = false)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                SetStatusText("Не удалось загрузить файл");
                onCompleted?.Invoke();
                return false;
            }

            int track = trackIndex ?? Math.Max(0, Math.Min(_playlistViewModel.NumTracks - 1, selectedTrackIndex));
            StartActivity("Загрузка");
            Task.Run(() => BuildPlaylistClipPayload(path, tickPos, track, autoPlace: false))
                .ContinueWith(t =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (t.IsFaulted || t.Result == null)
                            {
                                SetStatusText("Не удалось загрузить файл");
                                return;
                            }

                            var payload = t.Result;
                            var replaced = replaceClipsAtDropPoint
                                           && PlaylistIntegrity
                                               .GetClipsContainingPoint(_playlistViewModel, track, tickPos)
                                               .Any()
                                ? RemoveClipsContainingDropPoint(track, tickPos, refresh: false)
                                : new List<(Models.TrackItemViewModel, float[])>();

                            double preferredTick = _playlistViewModel.GetPreferredInsertTick(
                                track, tickPos, payload.Clip.DurationTicks, replaceClipsAtDropPoint);
                            _playlistViewModel.ResolveClipPlacement(payload.Clip, preferredTick, track);
                            PlaylistIntegrity.EnsureDurationFromSamples(
                                payload.Clip, payload.Samples, _playlistViewModel);
                            _clipSamplesCache[payload.Clip.Id] = payload.Samples;
                            PlaylistViewControl.InsertClip(payload.Clip, payload.Peaks);
                            PlaylistViewControl.SelectClip(payload.Clip.Id);
                            CommitPlaylistState(forceFullRedrawIds: new HashSet<Guid> { payload.Clip.Id });
                            PlaylistViewControl.RefreshViewportLayout();
                            _commandManager.Record(new Commands.AddPlaylistClipCommand(
                                this, CloneClipMeta(payload.Clip), payload.Samples, replaced));
                            SetStatusText($"Добавлен: {payload.Clip.Name}");
                            StopActivity($"Добавлен: {payload.Clip.Name}");
                        }
                        finally
                        {
                            onCompleted?.Invoke();
                        }
                    });
                });
            return true;
        }

        private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas canvas && canvas.Tag != null)
            {
                selectedTrackIndex = (int)canvas.Tag;
                UpdateTrackLabels();
                EnableControls(true);
            }
        }

        private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
        {
        }

        private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
        }
        
        private void UpdatePlayheadPosition()
        {
        }

        private void TimelineCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
        }

        private void UpdateCommandButtons()
        {
            BtnUndo.IsEnabled = _commandManager.CanUndo;
            BtnRedo.IsEnabled = _commandManager.CanRedo;
        }
        
        public void EnableControls(bool enable)
        {
            bool hasAudio = HasPlayableContent();
            BtnPlay.IsEnabled = enable && hasAudio;
            BtnStop.IsEnabled = enable;
            BtnRestart.IsEnabled = enable && hasAudio;
            BtnApply.IsEnabled = enable && (HasSelectedPlaylistClip() || tracks.Any(t => t.Samples.Length > 0));

            bool canEdit = enable && HasSelection();
            BtnCut.IsEnabled = canEdit;
            BtnCopy.IsEnabled = canEdit;
            BtnDelete.IsEnabled = canEdit;
            BtnPaste.IsEnabled = enable && HasClipboard();
            UpdateCommandButtons();
        }
        
        private void AddAudio_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Аудио|*.wav;*.mp3;*.flac;*.ogg;*.aiff;*.m4a|Все файлы|*.*",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true)
                return;

            int track = Math.Max(0, Math.Min(_playlistViewModel.NumTracks - 1, selectedTrackIndex));
            EnqueuePlaylistFiles(dialog.FileNames, track);
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardProject())
                return;

            ClearPlaylistProject();
            _currentProjectPath = null;
            Title = "Bnote — Новый проект";
            SetStatusText("Новый проект");
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardProject())
                return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = $"Проект BF Studio|*{ProjectService.Extension}|Все файлы|*.*",
                InitialDirectory = ProjectService.ProjectsDirectory
            };
            if (dialog.ShowDialog() == true)
                LoadProjectFromFile(dialog.FileName);
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentProjectPath))
            {
                SaveProjectAs_Click(sender, e);
                return;
            }

            try
            {
                SaveProjectToFile(_currentProjectPath);
                SetStatusText($"Сохранено: {Path.GetFileName(_currentProjectPath)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить проект:\n{ex.Message}", "Сохранение",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = $"Проект BF Studio|*{ProjectService.Extension}|Все файлы|*.*",
                InitialDirectory = ProjectService.ProjectsDirectory,
                FileName = string.IsNullOrEmpty(_currentProjectPath)
                    ? Path.GetFileName(ProjectService.GetDefaultProjectPath("Untitled"))
                    : Path.GetFileName(_currentProjectPath)
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                SaveProjectToFile(dialog.FileName);
                _currentProjectPath = dialog.FileName;
                Title = $"Bnote — {Path.GetFileNameWithoutExtension(dialog.FileName)}";
                SetStatusText($"Сохранено: {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить проект:\n{ex.Message}", "Сохранение",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HelpAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Bnote by PRYTEK Vision\n\nDAW с плейлистом, микшером и браузером аудио.",
                "О программе",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private bool ConfirmDiscardProject()
        {
            if (!_playlistViewModel.AudioClips.Any())
                return true;

            var result = MessageBox.Show(
                "Текущий проект не сохранён. Продолжить и потерять изменения?",
                "Проект",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        private void ClearPlaylistProject()
        {
            if (isPlaying)
                Stop_Click(this, new RoutedEventArgs());

            var clipIds = _playlistViewModel.AudioClips.Select(c => c.Id).ToList();
            foreach (var id in clipIds)
                RemovePlaylistClipInternal(id, refresh: false);

            _playlistViewModel.AudioClips.Clear();
            _clipSamplesCache.Clear();
            PlaylistViewControl.InvalidateAll();
            CommitPlaylistState(rebuildVisuals: true);
        }

        private void SaveProjectToFile(string projectFilePath)
        {
            string name = Path.GetFileNameWithoutExtension(projectFilePath);
            var snapshot = ProjectService.CreateSnapshot(_playlistViewModel, _playlistViewModel.AudioClips, name);
            ProjectService.Save(projectFilePath, snapshot, copyAudioFiles: true);
        }

        private void LoadProjectFromFile(string projectFilePath)
        {
            var project = ProjectService.Load(projectFilePath);
            ClearPlaylistProject();

            _playlistViewModel.Bpm = project.Bpm;
            _playlistViewModel.NumTracks = Math.Max(1, project.NumTracks);
            _currentProjectPath = projectFilePath;
            Title = $"Bnote — {project.Name}";

            foreach (var pc in project.Clips)
            {
                if (string.IsNullOrWhiteSpace(pc.FilePath) || !File.Exists(pc.FilePath))
                    continue;

                var template = new Models.TrackItemViewModel
                {
                    Id = pc.Id == Guid.Empty ? Guid.NewGuid() : pc.Id,
                    Name = pc.Name,
                    SampleRate = pc.SampleRate,
                    Channels = pc.Channels
                };

                var payload = BuildPlaylistClipPayload(pc.FilePath, pc.StartTick, pc.TrackIndex, autoPlace: false, template);
                if (payload == null)
                    continue;

                _clipSamplesCache[payload.Clip.Id] = payload.Samples;
                PlaylistViewControl.InsertClip(payload.Clip, payload.Peaks);
            }

            PlaylistViewControl.InvalidateAll();
            CommitPlaylistState(rebuildVisuals: true);
            SetStatusText($"Открыт проект: {project.Name}");
        }

        private static bool IsTextInputFocused()
        {
            return Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.TextBox;
        }

        private void LoadFileToTrack(string path)
        {
            // Захватываем текущий трек СРАЗУ, не позже!
            int trackIndex = selectedTrackIndex >= 0 ? selectedTrackIndex : 0;
            if (trackIndex >= tracks.Count) trackIndex = 0;
            
            // Оптимизация: запускаем загрузку асинхронно с захваченным индексом
            Task.Run(() => LoadFileAsync(path, trackIndex));
        }
        
        private void LoadFileToTrackOnTrack(string path, int trackIndex)
        {
            if (trackIndex < 0) trackIndex = 0;
            if (trackIndex >= tracks.Count) trackIndex = 0;
            
            // Use Command for undo/redo
            var command = new LoadFileCommand(this, path, trackIndex);
            _commandManager.Execute(command);
        }
        
        // Synchronous file loading for commands
        public void LoadFileToTrackSync(string path, int trackIndex)
        {
            try
            {
                var track = tracks[trackIndex];
                
                using var reader = new AudioFileReader(path);
                var allSamples = new List<float>();
                var buffer = new float[131072];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                        allSamples.Add(buffer[i]);
                }
                
                var samples = allSamples.ToArray();
                allSamples.Clear();
                
                track.Samples = samples;
                track.SampleRate = reader.WaveFormat.SampleRate;
                track.Channels = reader.WaveFormat.Channels;
                track.SourceFile = path;
                track.Name = Path.GetFileName(path);
                track.StartTime = 0;
                
                // Update waveform cache
                int peakCount = samples.Length > 10_000_000 ? 500 : (samples.Length > 5_000_000 ? 750 : 1000);
                _waveformPeaks[trackIndex] = ComputePeaks(samples, peakCount);
            }
            catch (Exception ex)
            {
                Log($"LoadFileToTrackSync error: {ex.Message}");
                Log($"LoadFileToTrackSync stack: {ex.StackTrace}");
                SetStatusText($"Error loading file: {ex.Message}");
            }
        }
        
        private void LoadFileAsync(string path, int trackIndex)
        {
            try
            {
                // Check memory before loading
                long memUsed = GC.GetTotalMemory(false);
                if (memUsed > 2_000_000_000) // > 2GB
                {
                    Dispatcher.Invoke(() =>
                    {
                        SetStatusText("Мало памяти! Очистите треки.");
                    });
                    return;
                }
                
                Dispatcher.Invoke(() =>
                {
                    StartActivity("Загрузка");
                });
                
                float[] samples;
                int sampleRate;
                int channels;
                
                // Read audio file
                using (var reader = new AudioFileReader(path))
                {
                    sampleRate = reader.WaveFormat.SampleRate;
                    channels = reader.WaveFormat.Channels;
                    
                    // Get actual duration
                    var duration = reader.TotalTime;
                    
                    // Check duration (max 2 hours)
                    if (duration.TotalSeconds > 7200)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            SetStatusText("Файл слишком длинный (макс 2 часа)");
                        });
                        return;
                    }
                    
                    // Use List<float> for variable-length files (MP3/AAC)
                    int estimatedSize = (int)(duration.TotalSeconds * sampleRate * channels);
                    var allSamples = new List<float>(estimatedSize);
                    
                    var buffer = new float[131072]; // 128K buffer
                    int read;
                    long totalRead = 0;
                    
                    Dispatcher.Invoke(() =>
                    {
                        UpdateActivityProgress("Декодирование...");
                    });
                    
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            allSamples.Add(buffer[i]);
                        }
                        totalRead += read;
                        
                        // Update progress every ~5 seconds
                        if (totalRead % (sampleRate * channels * 5) < buffer.Length)
                        {
                            double secondsRead = totalRead / (double)(sampleRate * channels);
                            int percent = duration.TotalSeconds > 0 
                                ? (int)(secondsRead * 100 / duration.TotalSeconds) 
                                : 0;
                            if (percent > 99) percent = 99;
                    Dispatcher.Invoke(() =>
                    {
                        UpdateActivityProgress($"Декодирование... {percent}% ({secondsRead:F0}/{duration.TotalSeconds:F0}с)");
                    });
                        }
                    }
                    
                    samples = allSamples.ToArray();
                    allSamples.Clear(); // Free List memory
                }
                
                Dispatcher.Invoke(() =>
                {
                    SetStatusText("Расчёт waveform...");
                });
                
                // For large files reduce peak count
                int peakCount = samples.Length > 10_000_000 ? 500 : (samples.Length > 5_000_000 ? 750 : 1000);
                
                // Compute peaks for waveform
                var peaks = ComputePeaks(samples, peakCount);
                
                // Update UI in main thread
                Dispatcher.Invoke(() =>
                {
                    if (trackIndex >= tracks.Count) trackIndex = 0;
                    
                    var track = tracks[trackIndex];
                    
                    // Save current state for undo BEFORE changing
                    var previousSamples = track.Samples.Length > 0 ? (float[])track.Samples.Clone() : null;
                    var previousSampleRate = track.SampleRate;
                    var previousChannels = track.Channels;
                    
                    // Apply new data
                    track.Samples = samples;
                    track.SampleRate = sampleRate;
                    track.Channels = channels;
                    track.SourceFile = path;
                    track.Name = Path.GetFileName(path);
                    track.StartTime = 0;
                    
                    // Cache waveform
                    _waveformPeaks[trackIndex] = peaks;
                    
                    RebuildMixer();
                    DrawTimeline(rebuildTracks: true);
                    UpdateTrackLabels();
                    
                    // Create command for undo and add to history (but don't re-execute)
                    var command = new LoadFileCommand(this, path, trackIndex);
                    // Set the previous state that was captured before loading
                    command.SetPreviousState(previousSamples, previousSampleRate, previousChannels, path, Path.GetFileName(path));
                    // Execute it to add to undo stack
                    _commandManager.Execute(command);
                    
                    SetStatusText($"Загружено: {track.Name}");
                    CurrentTimeText.Text = "00:00";
                    TotalTimeText.Text = FormatTime(track.Duration);
                    EnableControls(true);
                    StopActivity($"Загружен: {track.Name}");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    SetStatusText($"Ошибка: {ex.Message}");
                    StopActivity();
                });
            }
        }
        
        // Оптимизация: вычисление пиков для waveform
        private float[] ComputePeaks(float[] samples, int peakCount)
        {
            if (samples == null || samples.Length == 0 || peakCount <= 0) 
                return Array.Empty<float>();
            
            if (peakCount > 5000) peakCount = 5000;
            
            var peaks = new float[peakCount];
            int samplesPerPeak = Math.Max(1, samples.Length / peakCount);
            
            for (int i = 0; i < peakCount; i++)
            {
                int start = i * samplesPerPeak;
                int end = Math.Min(start + samplesPerPeak, samples.Length);
                
                float max = 0, sum = 0;
                int count = 0;
                for (int j = start; j < end; j++)
                {
                    float abs = Math.Abs(samples[j]);
                    if (abs > max) max = abs;
                    sum += abs;
                    count++;
                }
                float avg = count > 0 ? sum / count : 0;
                peaks[i] = avg * 0.6f + max * 0.4f;
            }
            
            // Smooth peaks with moving average
            var smoothed = new float[peakCount];
            int window = Math.Max(1, peakCount / 100);
            for (int i = 0; i < peakCount; i++)
            {
                float s = 0;
                int n = 0;
                for (int j = Math.Max(0, i - window); j <= Math.Min(peakCount - 1, i + window); j++)
                {
                    s += peaks[j];
                    n++;
                }
                smoothed[i] = s / n;
            }
            
            return smoothed;
        }
        
        private void ConvertToWav(string inputPath, string outputPath)
        {
            using var reader = new AudioFileReader(inputPath);
            using var writer = new WaveFileWriter(outputPath, new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels));
            
            var buffer = new float[4096];
            int read;
            
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var byteBuffer = new byte[read * 2];
                for (int i = 0; i < read; i++)
                {
                    float s = Math.Clamp(buffer[i], -1, 1);
                    short sample = (short)(s * 32767);
                    byteBuffer[i * 2] = (byte)(sample & 0xFF);
                    byteBuffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }
                writer.Write(byteBuffer, 0, byteBuffer.Length);
            }
        }

        public void RebuildMixer()
        {
            if (_playlistViewModel.AudioClips.Any(c => !string.IsNullOrEmpty(c.FilePath)))
            {
                SyncPlaylistToEngine();
                return;
            }

            if (tracks.Count < _waveformPeaks.Count)
            {
                for (int i = tracks.Count; i < _waveformPeaks.Count; i++)
                {
                    _waveformPeaks.Remove(i);
                    _waveformBitmaps.Remove(i);
                }
            }

            _audio.LoadTracks(tracks);
        }
        
        private void SeekToTime(double targetTime)
        {
            targetTime = Math.Max(0, Math.Min(targetTime, GetTotalDuration()));
            currentTime = targetTime;
            PlaylistViewControl.SetPlayheadTime(currentTime);
            CurrentTimeText.Text = FormatTime(currentTime);

            _audio.Seek((float)targetTime);

            UpdatePlayheadUI();
        }
        
        // Оптимизация: вынесено обновление UI playhead
        private void UpdatePlayheadUI()
        {
            double playheadX = currentTime * pixelsPerSecond;
            
            for (int trackIdx = 0; trackIdx < tracks.Count; trackIdx++)
            {
                // Обновляем playhead линии
                if (_playheadLines.TryGetValue(trackIdx, out var lines))
                {
                    foreach (var line in lines)
                    {
                        line.X1 = playheadX;
                        line.X2 = playheadX;
                    }
                }
                
                // Обновляем индикатор конца трека
                if (_endOfTrackLines.TryGetValue(trackIdx, out var endLine) && trackIdx < tracks.Count)
                {
                    var track = tracks[trackIdx];
                    double endX = track.Duration * pixelsPerSecond;
                    endLine.X1 = endX;
                    endLine.X2 = endX;
                }
            }
            
            CurrentTimeText.Text = FormatTime(currentTime);
            TotalTimeText.Text = FormatTime(GetTotalDuration());
        }

        // Оптимизация: кэш ссылок на playhead линии
        private readonly Dictionary<int, List<Line>> _playheadLines = new();
        private readonly Dictionary<int, Line> _endOfTrackLines = new();
        
        private void OnRenderFrame(object? sender, EventArgs e)
        {
            UpdatePlayheadFromEngine();
        }

        private void UpdatePlayheadFromEngine()
        {
            if (PlaylistViewControl.IsDraggingPlayhead)
                return;

            if (!isPlaying && !_audio.IsPlaying)
            {
                PlaylistViewControl.SetPlayheadTime(currentTime);
                return;
            }

            // Синхронизация playhead с AudioEngine (источник истины)
            _audio.UpdateTime();
            currentTime = _audio.CurrentTime;
            
            // Обновляем UI playhead напрямую
            double playheadX = currentTime * pixelsPerSecond;
            
            for (int trackIdx = 0; trackIdx < tracks.Count; trackIdx++)
            {
                if (_playheadLines.TryGetValue(trackIdx, out var lines))
                {
                    foreach (var line in lines)
                    {
                        line.X1 = playheadX;
                        line.X2 = playheadX;
                    }
                }
            }
            
            string timeText = FormatTime(currentTime);
            if (timeText != _lastPlayheadTimeText)
            {
                _lastPlayheadTimeText = timeText;
                CurrentTimeText.Text = timeText;
            }
            PlaylistViewControl.SetPlayheadTime(currentTime);
        }
        
        // Оптимизация: регистрация playhead линий для кэширования
        private void RegisterPlayheadLine(int trackIndex, Line line)
        {
            if (!_playheadLines.ContainsKey(trackIndex))
                _playheadLines[trackIndex] = new List<Line>();
            
            _playheadLines[trackIndex].Add(line);
        }
        
        private bool isDraggingPlayhead = false;
        private double dragStartX;
        private double dragStartTime;
        
        private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas canvas && canvas.Tag is int trackIndex)
            {
                var pos = e.GetPosition(canvas);
                double playheadX = currentTime * pixelsPerSecond;
                
                // Если кликнули очень близко к плейхеду (<8px) — перетаскивание плейхеда
                if (isPlaying && Math.Abs(pos.X - playheadX) < 8)
                {
                    isDraggingPlayhead = true;
                    dragStartX = pos.X;
                    dragStartTime = currentTime;
                    canvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                
                // Если не играет — можно перетаскивать плейхед в любом месте
                if (!isPlaying && Math.Abs(pos.X - playheadX) < 20)
                {
                    isDraggingPlayhead = true;
                    dragStartX = pos.X;
                    dragStartTime = currentTime;
                    canvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                
                // Если есть активное выделение — проверяем клик по ручкам
                _handleDrag = HandleDrag.None;
                if (_selectionStartTime >= 0 && _selectionEndTime >= 0)
                {
                    double scrollOff = TracksScroller.HorizontalOffset;
                    double selLeft = Math.Min(_selectionStartTime, _selectionEndTime) * pixelsPerSecond - scrollOff;
                    double selRight = Math.Max(_selectionStartTime, _selectionEndTime) * pixelsPerSecond - scrollOff;
                    
                    if (Math.Abs(pos.X - selLeft) < 10)
                    {
                        _handleDrag = HandleDrag.Left;
                        canvas.CaptureMouse();
                        e.Handled = true;
                        return;
                    }
                    if (Math.Abs(pos.X - selRight) < 10)
                    {
                        _handleDrag = HandleDrag.Right;
                        canvas.CaptureMouse();
                        e.Handled = true;
                        return;
                    }
                }
                
                // Сбрасываем старое выделение при клике вне ручек
                ClearSelection();
                
                // Если трек пустой — не начинаем новое выделение
                if (trackIndex < tracks.Count)
                {
                    var clip = tracks[trackIndex];
                    bool hasAudio = clip.Samples != null && clip.Samples.Length > 0;
                    if (!hasAudio)
                    {
                        e.Handled = true;
                        return;
                    }
                }
                
                // Start selection
                _isSelecting = true;
                
                // Координаты относительно SelectionCanvas (видимая область без скролла)
                var canvasPos = e.GetPosition(SelectionCanvas);
                _selectionStartPoint = new Point(canvasPos.X, canvasPos.Y);
                
                // Показываем оверлей сразу
                if (_selectionOverlay != null)
                {
                    _selectionOverlay.Left = canvasPos.X;
                    _selectionOverlay.Right = canvasPos.X;
                    _selectionOverlay.IsVisible = true;
                }
                
                selectedTrackIndex = trackIndex;
                focusedClipIndex = trackIndex;
                _selectingTrackIndex = trackIndex;
                
                canvas.CaptureMouse();
                e.Handled = true;
            }
        }
        
        private bool IsOnPlayhead(Point pos, Canvas canvas)
        {
            double playheadX = currentTime * pixelsPerSecond;
            return Math.Abs(pos.X - playheadX) < 20;
        }
        
        private void WaveformCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // Drag selection handle (левая или правая ручка)
            if (_handleDrag != HandleDrag.None && sender is Canvas && sender is Canvas hCanvas)
            {
                var pos = e.GetPosition(hCanvas);
                double newTime = pos.X / pixelsPerSecond;
                
                if (_handleDrag == HandleDrag.Left)
                {
                    _selectionStartTime = Math.Max(0, Math.Min(newTime, _selectionEndTime - 0.05));
                }
                else if (_handleDrag == HandleDrag.Right)
                {
                    _selectionEndTime = Math.Max(_selectionStartTime + 0.05, newTime);
                }
                
                // Sync SelectionManager
                double s = Math.Min(_selectionStartTime, _selectionEndTime);
                double e2 = Math.Max(_selectionStartTime, _selectionEndTime);
                SelectionManager.SelectionStart = s;
                SelectionManager.SelectionEnd = e2;
                UpdateSelectionUI();
                
                double dur = e2 - s;
                SetStatusText($"Выделение: {FormatTime(dur)}");
                return;
            }
            
            // Update playhead ONLY if not currently selecting
            if (!_isSelecting && isDraggingPlayhead && sender is Canvas && sender is Canvas playheadCanvas)
            {
                var pos = e.GetPosition(playheadCanvas);
                double deltaX = pos.X - dragStartX;
                double deltaTime = deltaX / pixelsPerSecond;
                double newTime = dragStartTime + deltaTime;
                newTime = Math.Max(0, Math.Min(newTime, GetTotalDuration()));
                
                if (Math.Abs(newTime - currentTime) > 0.001)
                {
                    currentTime = newTime;
                    SeekToTime(currentTime);
                    CurrentTimeText.Text = FormatTime(currentTime);
                    TotalTimeText.Text = FormatTime(GetTotalDuration());
                }
            }
            
            // Update selection on overlay while dragging (работает и во время playback)
            if (_isSelecting)
            {
                var canvasPos = e.GetPosition(SelectionCanvas);
                double left = Math.Min(_selectionStartPoint.X, canvasPos.X);
                double right = Math.Max(_selectionStartPoint.X, canvasPos.X);
                
                if (_selectionOverlay != null)
                {
                    _selectionOverlay.Left = left;
                    _selectionOverlay.Right = right;
                }
                
                // Show live duration in status
                double duration = (right - left) / pixelsPerSecond;
                SetStatusText($"Выделение: {FormatTime(duration)}");
            }
        }
        
        private void WaveformCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDraggingPlayhead)
            {
                isDraggingPlayhead = false;
                if (sender is Canvas canvas)
                {
                    canvas.ReleaseMouseCapture();
                }
                return;
            }
            
            if (_handleDrag != HandleDrag.None)
            {
                _handleDrag = HandleDrag.None;
                if (sender is Canvas canvas)
                {
                    canvas.ReleaseMouseCapture();
                }
                double dur = Math.Abs(_selectionEndTime - _selectionStartTime);
                SetStatusText($"Выделено: {FormatTime(dur)}");
                return;
            }
            
            // End selection
            if (_isSelecting)
            {
                _isSelecting = false;
                if (sender is Canvas canvas)
                {
                    canvas.ReleaseMouseCapture();
                }
                
                // Convert pixels to time — вычитаем ширину лейбла, добавляем скролл
                if (_selectionOverlay != null && _selectionOverlay.IsVisible)
                {
                    double scrollOffset = TracksScroller.HorizontalOffset;
                    _selectionStartTime = (_selectionOverlay.Left - TrackLabelWidth + scrollOffset) / pixelsPerSecond;
                    _selectionEndTime = (_selectionOverlay.Right - TrackLabelWidth + scrollOffset) / pixelsPerSecond;
                    
                    double duration = Math.Abs(_selectionEndTime - _selectionStartTime);
                    
                    // Minimum 50ms
                    if (duration < 0.05)
                    {
                        ClearSelection();
                        return;
                    }
                    
                    // Sync SelectionManager
                    double selStart = Math.Min(_selectionStartTime, _selectionEndTime);
                    double selEnd = Math.Max(_selectionStartTime, _selectionEndTime);
                    SelectionManager.SelectionStart = selStart;
                    SelectionManager.SelectionEnd = selEnd;
                    
                    UpdateSelectionUI();
                    EnableControls(true);
                    
                    SetStatusText($"Выделено: {FormatTime(duration)}");
                }
            }
        }
        
        // FL Studio style: Right-click shows context menu
        private void WaveformCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas canvas)
            {
                int trackIndex = canvas.Tag is int ti ? ti : selectedTrackIndex;
                if (trackIndex < 0) trackIndex = 0;
                
                selectedTrackIndex = trackIndex;
                focusedClipIndex = trackIndex;
                
                // Update context menu state
                _contextMenu?.UpdateMenuState();
                
                // Show context menu at cursor position
                _contextMenu!.IsOpen = true;
                
                e.Handled = true;
            }
        }

        private void SetPlayIcon(bool playing)
        {
            if (BtnPlay.Content is FontAwesome.Sharp.IconBlock icon)
                icon.Icon = playing ? FontAwesome.Sharp.IconChar.Pause : FontAwesome.Sharp.IconChar.Play;
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (isPlaying)
            {
                _audio.Pause();
                isPlaying = false;
                SetPlayIcon(false);
                CompositionTarget.Rendering -= OnRenderFrame;
                SetStatusText("Пауза");
            }
            else
            {
                StartPlayback();
            }
        }

        private void StartPlayback()
        {
            if (!HasPlayableContent())
                return;

            if (currentTime >= GetTotalDuration())
                currentTime = 0;

            _audio.Seek((float)currentTime);
            PlaylistViewControl.SetPlayheadTime(currentTime);
            _audio.Play();
            isPlaying = true;
            SetPlayIcon(true);
            CompositionTarget.Rendering -= OnRenderFrame;
            CompositionTarget.Rendering += OnRenderFrame;
            SetStatusText("Воспроизведение...");
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => StopTransport(resetToStart: true);

        public void Restart_Click(object sender, RoutedEventArgs e)
        {
            if (!HasPlayableContent())
                return;

            if (isPlaying)
            {
                _audio.Stop();
                isPlaying = false;
                SetPlayIcon(false);
                CompositionTarget.Rendering -= OnRenderFrame;
            }

            currentTime = 0;
            _audio.Seek(0);
            PlaylistViewControl.SetPlayheadTime(0);
            CurrentTimeText.Text = FormatTime(0);
            StartPlayback();
        }

        private void StopTransport(bool resetToStart)
        {
            if (isPlaying)
            {
                _audio.UpdateTime();
                currentTime = _audio.CurrentTime;
            }

            _audio.Stop();

            if (resetToStart)
            {
                currentTime = 0;
                _audio.Seek(0);
                PlaylistViewControl.SetPlayheadTime(0);
                SetStatusText("Остановлено");
            }
            else
            {
                _audio.Seek((float)currentTime);
                PlaylistViewControl.SetPlayheadTime(currentTime);
                SetStatusText("Пауза");
            }

            isPlaying = false;
            SetPlayIcon(false);
            CompositionTarget.Rendering -= OnRenderFrame;
            DrawTimeline(rebuildTracks: false);
            CurrentTimeText.Text = FormatTime(currentTime);
            TotalTimeText.Text = FormatTime(GetTotalDuration());
        }

        private void LoopToggle_Click(object sender, RoutedEventArgs e)
        {
            _isLoopEnabled = !_isLoopEnabled;
            _audio.SetLoopMode(_isLoopEnabled);
            if (BtnLoop?.Content is FontAwesome.Sharp.IconBlock icon)
                icon.Foreground = _isLoopEnabled ? new SolidColorBrush(Color.FromRgb(120, 129, 255)) : new SolidColorBrush(Color.FromRgb(170, 170, 170));
            SetStatusText(_isLoopEnabled ? "Повтор включён" : "Повтор выключен");
        }

        private const double RingDiameter = 22;
        private const double RingStrokeThickness = 2.5;

        private static double RingCircumference =>
            Math.PI * (RingDiameter - RingStrokeThickness);

        private void SetRingProgress(double progress)
        {
            _ringProgress = Math.Clamp(progress, 0, 1);
            if (RingTrack == null || RingProgress == null) return;

            RingTrack.StrokeDashArray = null;

            if (_ringProgress <= 0.001)
            {
                RingProgress.Visibility = Visibility.Collapsed;
                RingProgress.StrokeDashArray = null;
                return;
            }

            RingProgress.Visibility = Visibility.Visible;

            if (_ringProgress >= 0.999)
            {
                RingProgress.StrokeDashArray = null;
                return;
            }

            double c = RingCircumference;
            double visible = _ringProgress * c;
            RingProgress.StrokeDashArray = new DoubleCollection { visible, c - visible };
            RingProgress.StrokeDashOffset = 0;
        }

        private void OnRingRendering(object? sender, EventArgs e)
        {
            if (!_ringAnimActive) return;
            double elapsed = (DateTime.UtcNow - _ringAnimStartUtc).TotalSeconds;
            double eased = 0.9 * (1 - Math.Exp(-elapsed * 1.4));
            double progress = Math.Max(eased, _ringReportedProgress);
            if (_isOperationActive)
                progress = Math.Min(0.95, progress);
            SetRingProgress(progress);
        }

        private void StartRingAnimation()
        {
            _ringReportedProgress = 0;
            _ringAnimStartUtc = DateTime.UtcNow;
            _ringAnimActive = true;
            CompositionTarget.Rendering -= OnRingRendering;
            CompositionTarget.Rendering += OnRingRendering;
            SetRingProgress(0.06);
        }

        private void StopRingAnimation()
        {
            _ringAnimActive = false;
            CompositionTarget.Rendering -= OnRingRendering;
        }

        private void ShowRingIdle()
        {
            StopRingAnimation();
            _ringIdleTimer?.Stop();
            _ringReportedProgress = 0;
            SetRingProgress(0);
        }

        private void FlashRingComplete()
        {
            StopRingAnimation();
            SetRingProgress(1);
            _ringIdleTimer?.Stop();
            _ringIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _ringIdleTimer.Tick += (_, _) =>
            {
                _ringIdleTimer?.Stop();
                ShowRingIdle();
            };
            _ringIdleTimer.Start();
        }

        // ========== Status bar: текст скрыт, детали — в popup при наведении на кольцо ==========
        private void SetStatusText(string text)
        {
            if (_isOperationActive) return;
            _lastStatusDetail = text;
            if (StatusText != null) StatusText.Text = "Готово";
        }

        // ========== Activity / Infotip with animated dots ==========
        private string GetActivityDotsText()
        {
            string[] dots = { ".", ". .", ". . ." };
            return _activityBaseText + dots[_dotsPhase % 3];
        }

        private void StartActivity(string description)
        {
            _currentOperationDescription = description;
            _activityBaseText = description;
            _dotsPhase = 0;
            _isOperationActive = true;
            StartRingAnimation();

            if (_dotsTimer == null)
            {
                _dotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _dotsTimer.Tick += (_, _) =>
                {
                    _dotsPhase = (_dotsPhase + 1) % 3;
                    if (ActivityPopup?.IsOpen == true && PopupTitle != null)
                        PopupTitle.Text = GetActivityDotsText();
                };
            }
            _dotsTimer.Start();

            if (ActivityPopup != null && ActivityPopup.IsOpen)
            {
                if (PopupTitle != null) PopupTitle.Text = GetActivityDotsText();
                if (PopupDescription != null) PopupDescription.Text = description;
            }

            if (_popupUpdateTimer == null)
            {
                _popupUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _popupUpdateTimer.Tick += (_, _) =>
                {
                    if (ActivityPopup != null && ActivityPopup.IsOpen && _isOperationActive)
                    {
                        if (PopupTitle != null) PopupTitle.Text = GetActivityDotsText();
                        if (PopupDescription != null) PopupDescription.Text = _currentOperationDescription;
                    }
                };
            }
            _popupUpdateTimer.Start();
        }

        private void StopActivity(string completionMessage = "")
        {
            _isOperationActive = false;
            _dotsTimer?.Stop();
            _popupUpdateTimer?.Stop();
            if (string.IsNullOrEmpty(completionMessage))
                completionMessage = _currentOperationDescription;
            _currentOperationDescription = completionMessage;
            _lastStatusDetail = completionMessage;
            FlashRingComplete();
        }

        private void UpdateActivityProgress(string detail)
        {
            _currentOperationDescription = detail;
            var match = System.Text.RegularExpressions.Regex.Match(detail, @"(\d+)\s*%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                _ringReportedProgress = Math.Clamp(pct / 100.0, 0.05, 0.99);
            if (ActivityPopup != null && ActivityPopup.IsOpen)
            {
                PopupTitle.Text = GetActivityDotsText();
                PopupDescription.Text = detail;
            }
        }

        private void SetupActivityPopupHover()
        {
            if (ActivityPopupBorder == null) return;
            ActivityPopupBorder.MouseEnter += (_, _) => CancelActivityPopupClose();
            ActivityPopupBorder.MouseLeave += (_, _) => ScheduleActivityPopupClose();
        }

        private void CancelActivityPopupClose()
        {
            _activityPopupCloseTimer?.Stop();
        }

        private void ScheduleActivityPopupClose()
        {
            CancelActivityPopupClose();
            if (_activityPopupCloseTimer == null)
            {
                _activityPopupCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _activityPopupCloseTimer.Tick += (_, _) =>
                {
                    _activityPopupCloseTimer!.Stop();
                    if (ActivityRing?.IsMouseOver == true) return;
                    if (ActivityPopupBorder?.IsMouseOver == true) return;
                    if (ActivityPopup != null)
                        ActivityPopup.IsOpen = false;
                    _popupUpdateTimer?.Stop();
                };
            }
            _activityPopupCloseTimer.Start();
        }

        private void UpdateActivityPopupContent()
        {
            if (_isOperationActive)
            {
                PopupTitle.Text = GetActivityDotsText();
                PopupDescription.Text = _currentOperationDescription;
            }
            else
            {
                PopupTitle.Text = "Готово";
                PopupDescription.Text = string.IsNullOrEmpty(_lastStatusDetail)
                    ? _currentOperationDescription
                    : _lastStatusDetail;
            }
        }

        private void SpinnerGrid_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (ActivityPopup == null) return;
            CancelActivityPopupClose();
            UpdateActivityPopupContent();
            ActivityPopup.IsOpen = true;
            if (_isOperationActive && _popupUpdateTimer != null && !_popupUpdateTimer.IsEnabled)
                _popupUpdateTimer.Start();
        }

        private void SpinnerGrid_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ScheduleActivityPopupClose();
        }

        public void Cut_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPlaylistSampleRange(out var plItem, out int plStart, out int plLen))
            {
                var samples = LoadClipSamples(plItem!);
                var removed = new float[plLen];
                Array.Copy(samples, plStart, removed, 0, plLen);
                _commandManager.Execute(new Commands.SplicePlaylistSamplesCommand(
                    this, plItem!.Id, plStart, removed, saveToClipboard: true));
                ClearSelection();
                SetStatusText($"Вырезано: {FormatTime((double)plLen / (plItem.SampleRate * plItem.Channels))}");
                EnableControls(true);
                return;
            }

            if (HasSelectedPlaylistClip() && !HasPlaylistTimeSelection())
            {
                var item = GetSelectedPlaylistClip()!;
                var samples = LoadClipSamples(item);
                _commandManager.Execute(new Commands.CutWholePlaylistClipCommand(this, item, samples));
                ClearSelection();
                SetStatusText($"Вырезано: {item.Name} — выберите дорожку и вставьте (Ctrl+V)");
                EnableControls(true);
                return;
            }

            if (!SelectionManager.HasSelection || focusedClipIndex < 0) return;

            var track = tracks[focusedClipIndex];
            var range = SelectionManager.GetSampleRange(track);
            if (range == null) return;

            var (startSample, endSample) = range.Value;
            int length = endSample - startSample;
            if (length <= 0) return;

            var command = new CutCommand(this, focusedClipIndex, startSample, length, 
                track.Channels, track.SampleRate);
            _commandManager.Execute(command);
            
            // Сбрасываем кэш waveform и спектрограммы
            _waveformPeaks.Remove(focusedClipIndex);
            _waveformBitmaps.Remove(focusedClipIndex);
            _spectrogramCache.Remove(focusedClipIndex);
            _spectrogramBitmaps.Remove(focusedClipIndex);
            DrawTimeline(rebuildTracks: true);
            
            SelectionManager.ClearSelection();
            ClearSelection();
            SetStatusText($"Вырезано: {FormatTime((double)length / (track.SampleRate * track.Channels))}");
            EnableControls(true);
        }

        public void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetPlaylistSampleRange(out var plItem, out int plStart, out int plLen))
            {
                var samples = LoadClipSamples(plItem!);
                var chunk = new float[plLen];
                Array.Copy(samples, plStart, chunk, 0, plLen);
                SetSampleClipboard(chunk, plItem!.SampleRate, plItem.Channels);
                SetStatusText($"Скопировано: {FormatTime((double)plLen / (plItem.SampleRate * plItem.Channels))}");
                EnableControls(true);
                return;
            }

            if (HasSelectedPlaylistClip())
            {
                var item = GetSelectedPlaylistClip()!;
                var samples = LoadClipSamples(item);
                SetPlaylistClipboardWholeClip(item, samples, wasCut: false);
                SetStatusText($"Скопировано: {item.Name}");
                EnableControls(true);
                return;
            }

            if (!SelectionManager.HasSelection || focusedClipIndex < 0) return;

            var track = tracks[focusedClipIndex];
            var range = SelectionManager.GetSampleRange(track);
            if (range == null) return;

            var (startSample, endSample) = range.Value;
            int length = endSample - startSample;
            if (length <= 0) return;

            ClipboardData = new float[length];
            Array.Copy(track.Samples, startSample, ClipboardData, 0, length);
            ClipboardChannels = track.Channels;
            ClipboardSampleRate = track.SampleRate;

            SetStatusText($"Скопировано: {FormatTime((double)length / (ClipboardSampleRate * ClipboardChannels))}");
            EnableControls(true);
        }

        public void Paste_Click(object sender, RoutedEventArgs e)
        {
            if (!HasClipboard()) return;

            if (_playlistClipboard.Kind == Models.PlaylistClipboard.ContentKind.WholeClip)
            {
                int trackIdx = selectedTrackIndex >= 0 ? selectedTrackIndex : 0;
                if (trackIdx >= _playlistViewModel.NumTracks) trackIdx = 0;
                double tick = _playlistViewModel.SecondsToTick(currentTime);
                var newClip = NewClipFromClipboard(tick, trackIdx);
                bool wasCut = _playlistClipboard.WasCut;
                var data = (float[])_playlistClipboard.Samples.Clone();
                _commandManager.Execute(new Commands.PasteWholePlaylistClipCommand(
                    this, newClip, data, wasCut));
                PlaylistViewControl.SelectClip(newClip.Id);
                SetStatusText($"Вставлено: {newClip.Name}");
                EnableControls(true);
                return;
            }

            if (_playlistClipboard.Kind == Models.PlaylistClipboard.ContentKind.Samples)
            {
                var target = GetSelectedPlaylistClip();
                if (target != null)
                {
                    double clipStart = _playlistViewModel.TickToSeconds(target.StartTick);
                    double rel = Math.Max(0, currentTime - clipStart);
                    int rate = target.SampleRate * Math.Max(1, target.Channels);
                    int insertAt = (int)(rel * rate);
                    var data = (float[])_playlistClipboard.Samples.Clone();
                    _commandManager.Execute(new Commands.PastePlaylistSamplesCommand(
                        this, target.Id, insertAt, data));
                    SetStatusText($"Вставлено: {FormatTime((double)data.Length / rate)}");
                    EnableControls(true);
                    return;
                }
            }

            if (ClipboardData == null || ClipboardData.Length == 0) return;
            if (selectedTrackIndex < 0) selectedTrackIndex = 0;

            var track = tracks[selectedTrackIndex];
            double pasteTime = SelectionManager.HasSelection ? 
                Math.Min(SelectionManager.SelectionStart, SelectionManager.SelectionEnd) : 
                currentTime;
            
            int pasteSample = (int)(pasteTime * track.SampleRate * track.Channels);
            pasteSample = Math.Max(0, Math.Min(pasteSample, track.Samples.Length));

            var command = new PasteCommand(this, selectedTrackIndex, pasteSample, ClipboardData);
            _commandManager.Execute(command);
            
            // Сбрасываем кэш waveform и спектрограммы
            _waveformPeaks.Remove(selectedTrackIndex);
            _waveformBitmaps.Remove(selectedTrackIndex);
            _spectrogramCache.Remove(selectedTrackIndex);
            _spectrogramBitmaps.Remove(selectedTrackIndex);
            DrawTimeline(rebuildTracks: true);
            
            SetStatusText($"Вставлено: {FormatTime((double)ClipboardData.Length / (ClipboardSampleRate * ClipboardChannels))}");
            EnableControls(true);
        }

        public void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (HasPlaylistTimeSelection() &&
                TryGetPlaylistSampleRange(out var plItem, out int plStart, out int plLen))
            {
                var samples = LoadClipSamples(plItem!);
                var removed = new float[plLen];
                Array.Copy(samples, plStart, removed, 0, plLen);
                _commandManager.Execute(new Commands.SplicePlaylistSamplesCommand(
                    this, plItem!.Id, plStart, removed, saveToClipboard: false));
                ClearSelection();
                SetStatusText("Фрагмент удалён");
                EnableControls(true);
                return;
            }

            if (HasSelectedPlaylistClip())
            {
                DeleteSelectedPlaylistClip();
                EnableControls(true);
                return;
            }

            if (!SelectionManager.HasSelection || focusedClipIndex < 0) return;
            
            var track = tracks[focusedClipIndex];
            var range = SelectionManager.GetSampleRange(track);
            if (range == null) return;
            
            var (startSample, endSample) = range.Value;
            int length = endSample - startSample;
            if (length <= 0) return;

            var command = new DeleteCommand(this, focusedClipIndex, startSample, length);
            _commandManager.Execute(command);
            
            // Сбрасываем кэш waveform и спектрограммы
            _waveformPeaks.Remove(focusedClipIndex);
            _waveformBitmaps.Remove(focusedClipIndex);
            _spectrogramCache.Remove(focusedClipIndex);
            _spectrogramBitmaps.Remove(focusedClipIndex);
            DrawTimeline(rebuildTracks: true);
            
            SelectionManager.ClearSelection();
            ClearSelection();
            SetStatusText("Удалено");
            EnableControls(true);
        }

        public void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_commandManager.CanUndo)
            {
                string undone = _commandManager.LastUndoDescription ?? "действие";
                _commandManager.Undo();
                _spectrogramCache.Clear();
                _spectrogramBitmaps.Clear();
                _waveformPeaks.Clear();
                _waveformBitmaps.Clear();
                DrawTimeline(rebuildTracks: true);
                RefreshPlaylistAfterEdit();
                SetStatusText($"Отменено: {undone}");
            }
        }

        public void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_commandManager.CanRedo)
            {
                _commandManager.Redo();
                _spectrogramCache.Clear();
                _spectrogramBitmaps.Clear();
                _waveformPeaks.Clear();
                _waveformBitmaps.Clear();
                DrawTimeline(rebuildTracks: true);
                RefreshPlaylistAfterEdit();
                SetStatusText($"Повторено: {_commandManager.LastRedoDescription}");
            }
        }

        private void AddTrack_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistViewModel.NumTracks >= 32)
            {
                SetStatusText($"Достигнут лимит треков (32)");
                return;
            }
            PlaylistViewControl.AddTrack();
            SetStatusText($"Добавлена дорожка {_playlistViewModel.NumTracks}");
            SyncTimeRuler();
        }

        private void RemoveTrack_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistViewModel.NumTracks <= 1) return;
            PlaylistViewControl.RemoveTrack();
            SetStatusText("Дорожка удалена");
            SyncTimeRuler();
        }

        public void ClearTrack_Click(object sender, RoutedEventArgs e)
        {
            int trackIdx = GetSelectedPlaylistClip()?.TrackIndex ?? selectedTrackIndex;
            if (trackIdx >= 0 && _playlistViewModel.AudioClips.Any(c => c.TrackIndex == trackIdx))
            {
                var onTrack = _playlistViewModel.AudioClips.Where(c => c.TrackIndex == trackIdx).ToList();
                foreach (var c in onTrack)
                    _clipSamplesCache.Remove(c.Id);
                PlaylistViewControl.RemoveClipsOnTrack(trackIdx);
                CommitPlaylistState();
                SetStatusText($"Дорожка {trackIdx + 1} очищена");
                return;
            }

            if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
            var track = tracks[selectedTrackIndex];
            if (track.Samples.Length == 0) return;

            track.Samples = Array.Empty<float>();
            track.SourceFile = null;
            track.Name = $"Дорожка {selectedTrackIndex + 1}";

            _waveformPeaks.Remove(selectedTrackIndex);
            _waveformBitmaps.Remove(selectedTrackIndex);
            _spectrogramCache.Remove(selectedTrackIndex);
            _spectrogramBitmaps.Remove(selectedTrackIndex);
            ClearSelection();
            DrawTimeline(rebuildTracks: true);
            SetStatusText($"Трек {selectedTrackIndex + 1} очищен");
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            SetStatusText("Экспорт...");
            MessageBox.Show("Функция экспорта будет добавлена", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatusText("Готово");
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            pixelsPerSecond = Math.Min(500, pixelsPerSecond * 1.5);
            ZoomSlider.Value = pixelsPerSecond;
            // ZoomSlider_Changed will call DrawTimeline
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            pixelsPerSecond = Math.Max(5, pixelsPerSecond / 1.5);
            ZoomSlider.Value = pixelsPerSecond;
            // ZoomSlider_Changed will call DrawTimeline
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ZoomSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Log($"ZoomSlider_Changed: {e.NewValue}");
            if (ZoomSlider != null && ZoomValue != null)
            {
                pixelsPerSecond = ZoomSlider.Value;
                ZoomValue.Text = $"{pixelsPerSecond:F0}%";

                // Fast update: resize canvases, grid lines, ruler — no spectrogram redraw
                UpdateZoomLayout(redrawContent: false);

                PlaylistViewControl.SetZoom(pixelsPerSecond);
                SyncTimeRuler();
                if (currentTime > 0 || isPlaying)
                    PlaylistViewControl.SetPlayheadTime(currentTime);

                // Debounce full redraw (spectrogram) until slider settles for 150ms
                if (_zoomDebounce == null)
                {
                    _zoomDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    _zoomDebounce.Tick += (s, args) =>
                    {
                        _zoomDebounce.Stop();
                        UpdateZoomLayout(); // full redraw with debounced width
                    };
                }
                _zoomDebounce.Stop();
                _zoomDebounce.Start();
            }
        }

        private void ToggleView_Click(object sender, RoutedEventArgs e)
        {
            _showSpectrogram = !_showSpectrogram;
            if (BtnToggleView != null)
            {
                var icon = BtnToggleView.Content as FontAwesome.Sharp.IconBlock;
                if (icon != null)
                    icon.Icon = _showSpectrogram ? FontAwesome.Sharp.IconChar.WaveSquare : FontAwesome.Sharp.IconChar.ChartLine;
            }
            _waveformPeaks.Clear();
            _waveformBitmaps.Clear();
            PlaylistViewControl.SetViewMode(_showSpectrogram);
            SetStatusText(_showSpectrogram ? "Режим: спектрограмма" : "Режим: waveform");
        }

        private void SyncTimeRuler()
        {
            var pl = _playlistViewModel;
            TimeRulerControl.Bpm = pl.Bpm;
            TimeRulerControl.PixelsPerSecond = pl.ZoomX * pl.TicksPerSecond;
            TimeRulerControl.TotalDuration = GetTotalDuration();
            TimeRulerControl.ScrollOffset = PlaylistViewControl.HorizontalScrollOffset;
            TimeRulerControl.UpdateTicks();
            TotalTimeText.Text = FormatTime(GetTotalDuration());
        }

        private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
        }

        private void Pan_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
        }

        private void ApplyEffects_Click(object sender, RoutedEventArgs e)
        {
            OpenInstrumentsWindow();
        }
        
        public void OpenInstrumentsWindow()
        {
            try
            {
                AudioClip? track = null;
                var playlistClip = GetSelectedPlaylistClip();
                if (playlistClip != null)
                {
                    track = BuildAudioClipFromPlaylist(playlistClip);
                    _instrumentsPlaylistClipId = playlistClip.Id;
                    selectedTrackIndex = playlistClip.TrackIndex;
                }
                else
                {
                    _instrumentsPlaylistClipId = null;
                    if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count)
                    {
                        MessageBox.Show("Выберите клип на плейлисте", "Instruments",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    track = tracks[selectedTrackIndex];
                }

                if (track?.Samples == null || track.Samples.Length == 0)
                {
                    MessageBox.Show("Сначала загрузите аудио в клип", "Instruments",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_instrumentsWindow != null && _instrumentsWindow.IsVisible)
                {
                    _instrumentsWindow.Activate();
                    _instrumentsWindow.LoadTrack(track);
                    return;
                }

                _instrumentsWindow = new InstrumentsWindow();
                _instrumentsWindow.Owner = this;
                _instrumentsWindow.LoadTrack(track);
                
                // Подписка на событие применения эффектов
                _instrumentsWindow.ApplyRequested += () =>
                {
                    Dispatcher.Invoke(() => ApplyEffectsFromInstrumentsWindow());
                };
                
                // Подписка на Preview
                _instrumentsWindow.PreviewRequested += () =>
                {
                    Dispatcher.Invoke(() => PreviewTrackWithEffects());
                };
                
                // Обработчик закрытия
                _instrumentsWindow.Closed += (s, args) =>
                {
                    _instrumentsWindow = null;
                };
                
                _instrumentsWindow.Show(); // Не ShowDialog - можно работать с главным окном
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(@"C:\Temp\audiostream_debug.log",
                        $"{DateTime.Now:HH:mm:ss.fff} [Instruments] {ex}\r\n");
                }
                catch { }

                MessageBox.Show(
                    "Не удалось открыть окно инструментов:\n" + ex.Message,
                    "Инструменты",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        // Обновление InstrumentsWindow при смене трека
        public void UpdateInstrumentsWindow()
        {
            if (_instrumentsWindow == null || !_instrumentsWindow.IsVisible) return;

            var playlistClip = GetSelectedPlaylistClip();
            if (playlistClip != null)
            {
                var track = BuildAudioClipFromPlaylist(playlistClip);
                if (track?.Samples?.Length > 0)
                {
                    _instrumentsPlaylistClipId = playlistClip.Id;
                    _instrumentsWindow.LoadTrack(track);
                }
                return;
            }

            if (selectedTrackIndex >= 0 && selectedTrackIndex < tracks.Count)
            {
                var track = tracks[selectedTrackIndex];
                if (track?.Samples?.Length > 0)
                {
                    _instrumentsPlaylistClipId = null;
                    _instrumentsWindow.LoadTrack(track);
                }
            }
        }
        
        // Проверка доступности NativeAudio
        private static bool _nativeAudioAvailable = false;
        private static bool CheckNativeAudio()
        {
            try
            {
                // Пробуем создать пустой effect chain
                var fx = NativeAudio.CreateEffectChain(44100, 2);
                if (fx != IntPtr.Zero)
                {
                    NativeAudio.DeleteEffectChain(fx);
                    _nativeAudioAvailable = true;
                    return true;
                }
            }
            catch
            {
                _nativeAudioAvailable = false;
            }
            return false;
        }
        
        // Применить эффекты напрямую
        public void ApplyEffectsFromInstrumentsWindow()
        {
            if (_instrumentsWindow == null || !_instrumentsWindow.IsVisible) return;

            AudioClip? track = ResolveInstrumentsTargetTrack();
            if (track?.Samples == null || track.Samples.Length == 0) return;

            if (!_nativeAudioAvailable)
            {
                if (!CheckNativeAudio())
                {
                    SetStatusText("⚠ Effects unavailable (DLL not found)");
                    return;
                }
            }

            try
            {
                ApplyInstrumentsChanges(track, _instrumentsWindow);
                if (_instrumentsPlaylistClipId.HasValue)
                {
                    var item = _playlistViewModel.AudioClips
                        .FirstOrDefault(c => c.Id == _instrumentsPlaylistClipId.Value);
                    if (item != null)
                        RefreshPlaylistClipPeaks(item);
                }
            }
            catch (Exception ex)
            {
                SetStatusText("⚠ Effect error: " + ex.Message);
            }
        }

        private AudioClip? ResolveInstrumentsTargetTrack()
        {
            if (_instrumentsPlaylistClipId.HasValue)
            {
                var item = _playlistViewModel.AudioClips
                    .FirstOrDefault(c => c.Id == _instrumentsPlaylistClipId.Value);
                return item != null ? BuildAudioClipFromPlaylist(item) : null;
            }

            if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return null;
            return tracks[selectedTrackIndex];
        }
        
        // Preview трека с эффектами
        public void PreviewTrackWithEffects()
        {
            var track = ResolveInstrumentsTargetTrack();
            if (track?.Samples == null || track.Samples.Length == 0) return;
            
            // Создаём копию сэмплов для preview
            float[] previewSamples = (float[])track.Samples.Clone();
            
            // Проверяем доступность NativeAudio
            if (!_nativeAudioAvailable && !CheckNativeAudio())
            {
                // Preview без эффектов
                _audio.PlayPreview(previewSamples, track.SampleRate, track.Channels);
                isPlaying = true;
                BtnPlay.Content = "⏸";
                CompositionTarget.Rendering += OnRenderFrame;
                SetStatusText("▶ Preview (no effects - DLL missing)");
                return;
            }
            
            try
            {
                // Применяем эффекты к копии
                IntPtr fx = NativeAudio.CreateEffectChain(track.SampleRate, track.Channels);
                
                if (_instrumentsWindow != null)
                {
                    NativeAudio.SetLowPass(fx, _instrumentsWindow.LowPassEnabled, _instrumentsWindow.LowPassCutoff);
                    NativeAudio.SetHighPass(fx, _instrumentsWindow.HighPassEnabled, _instrumentsWindow.HighPassCutoff);
                    NativeAudio.SetGain(fx, _instrumentsWindow.GainEnabled, _instrumentsWindow.GainDb);
                    NativeAudio.SetEcho(fx, _instrumentsWindow.EchoEnabled, _instrumentsWindow.EchoDelay,
                        _instrumentsWindow.EchoFeedback / 100f, _instrumentsWindow.EchoMix / 100f);
                    NativeAudio.SetReverb(fx, _instrumentsWindow.ReverbEnabled,
                        _instrumentsWindow.ReverbWet / 100f, _instrumentsWindow.ReverbRoom / 100f);
                }
                
                NativeAudio.ProcessBuffer(fx, previewSamples, previewSamples.Length);
                NativeAudio.DeleteEffectChain(fx);
                
                SetStatusText("▶ Preview: " + track.Name);
            }
            catch (Exception ex)
            {
                SetStatusText("Preview error: " + ex.Message);
            }
            
            // Воспроизводим
            _audio.PlayPreview(previewSamples, track.SampleRate, track.Channels);
            isPlaying = true;
            BtnPlay.Content = "⏸";
            CompositionTarget.Rendering += OnRenderFrame;
        }
        
        private void ApplyInstrumentsChanges(AudioClip track, InstrumentsWindow window)
        {
            try
            {
                IntPtr fx = NativeAudio.CreateEffectChain(track.SampleRate, track.Channels);

                NativeAudio.SetLowPass(fx, window.LowPassEnabled, window.LowPassCutoff);
                NativeAudio.SetHighPass(fx, window.HighPassEnabled, window.HighPassCutoff);
                NativeAudio.SetGain(fx, window.GainEnabled, window.GainDb);
                NativeAudio.SetEcho(fx, window.EchoEnabled, window.EchoDelay,
                    window.EchoFeedback / 100f, window.EchoMix / 100f);
                NativeAudio.SetReverb(fx, window.ReverbEnabled,
                    window.ReverbWet / 100f, window.ReverbRoom / 100f);

                NativeAudio.ProcessBuffer(fx, track.Samples, track.Samples.Length);

                NativeAudio.DeleteEffectChain(fx);

                RebuildMixer();
                DrawTimeline(rebuildTracks: true);
                SetStatusText("Effects applied to: " + track.Name);
            }
            catch (Exception ex)
            {
                SetStatusText("⚠ Effect error: " + ex.Message);
            }
        }
        
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            SetStatusText("Готово");
        }

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int min = (int)(seconds / 60);
            int sec = (int)(seconds % 60);
            int ms = (int)((seconds % 1) * 100);
            return $"{min:D2}:{sec:D2}.{ms:D2}";
        }
        
        // ========== Drag & Drop Support (FL Studio Style) ==========
        private int _dragHoveredTrackIndex = -1;
        private Line? _dropIndicatorLine = null;
        private int _trackIndexBeforeDrag = -1; // Запоминаем выбор ДО drag
        private bool _isDraggingFile = false; // Флаг что идёт drag-drop
        
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(FileItem)))
            {
                e.Effects = DragDropEffects.Copy;
                
                // Запоминаем текущий выбор ДО начала drag
                _trackIndexBeforeDrag = selectedTrackIndex;
                _isDraggingFile = true;
                
                SetStatusText("Отпустите файл на трек для загрузки");
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
        
        private void OnDragLeave(object sender, DragEventArgs e)
        {
            ClearDropIndicators();
            _isDraggingFile = false;
            HideTrackHighlight();
            SetStatusText("Готово");
        }
        
        // Preview обработчики - перехватывают drag дочерних элементов
        private void OnPreviewDragEnter(object sender, DragEventArgs e)
        {
            OnDragEnter(sender, e);
        }
        
        private void OnPreviewDragLeave(object sender, DragEventArgs e)
        {
            OnDragLeave(sender, e);
        }
        
        private void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            UpdateDragOverState(e);
        }
        
        private void OnDragOver(object sender, DragEventArgs e)
        {
            UpdateDragOverState(e);
        }
        
        private void UpdateDragOverState(DragEventArgs e)
        {
            // Проверяем и внешние файлы, и внутренние FileItem
            bool hasFileDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
            bool hasFileItem = e.Data.GetDataPresent(typeof(FileItem));
            
            if (hasFileDrop || hasFileItem)
            {
                e.Effects = DragDropEffects.Copy;
                
                // Используем HitTest для точного определения трека под курсором
                var mousePos = e.GetPosition(TracksScroller);
                var hitResult = VisualTreeHelper.HitTest(TracksScroller, mousePos);
                
                int hoveredTrack = -1;
                
                if (hitResult != null)
                {
                    // Ищем ближайший Border с Tag = trackIndex
                    var element = hitResult.VisualHit as DependencyObject;
                    while (element != null)
                    {
                        if (element is Border border && border.Tag is int trackIdx)
                        {
                            hoveredTrack = trackIdx;
                            break;
                        }
                        element = VisualTreeHelper.GetParent(element);
                    }
                }
                
                // Fallback: если HitTest не нашёл, используем расчёт по Y
                if (hoveredTrack < 0)
                {
                    double scrollOffset = TracksScroller.VerticalOffset;
                    double adjustedY = mousePos.Y + scrollOffset;
                    hoveredTrack = (int)(adjustedY / (TrackHeight + TrackMargin));
                }
                
                // Ограничиваем диапазон
                if (hoveredTrack < 0) hoveredTrack = 0;
                if (hoveredTrack >= tracks.Count) hoveredTrack = tracks.Count - 1;
                
                if (hoveredTrack != _dragHoveredTrackIndex)
                {
                    _dragHoveredTrackIndex = hoveredTrack;
                    HighlightTrack(hoveredTrack); // FL Studio: подсветка трека
                }
                
                // Обновляем ghost позицию если он есть
                _ghostAdorner?.UpdatePosition(e.GetPosition(TracksBorder));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
        
        // FL Studio Style: подсветка трека при наведении
        private void HighlightTrack(int trackIndex)
        {
            HideTrackHighlight();
            
            if (trackIndex < 0 || trackIndex >= tracks.Count) return;
            
            // Устанавливаем свойство IsDragOver на модели
            tracks[trackIndex].IsDragOver = true;
            
            // Перерисовываем визуально
            UpdateTrackLabels();
            
            // Обновляем статус
            var track = tracks[trackIndex];
            if (track.Samples.Length > 0)
                SetStatusText($"Трек {trackIndex + 1}: заменит \"{Path.GetFileName(track.SourceFile)}\"");
            else
                SetStatusText($"Трек {trackIndex + 1}: пустой — загрузить");
        }
        
        private void HideTrackHighlight()
        {
            // Сбрасываем все IsDragOver
            foreach (var track in tracks)
            {
                track.IsDragOver = false;
            }
            UpdateTrackLabels();
        }
        
        private void UpdateDropIndicator()
        {
            ClearDropIndicators();
            
            if (_dragHoveredTrackIndex < 0 || _dragHoveredTrackIndex >= tracks.Count)
                return;
            
            var trackTop = _dragHoveredTrackIndex * (TrackHeight + TrackMargin);
            
            _dropIndicatorLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                StrokeThickness = 3,
                X1 = 0,
                Y1 = trackTop + TrackHeight / 2,
                X2 = 1000,
                Y2 = trackTop + TrackHeight / 2,
                IsHitTestVisible = false
            };
            TracksContainer.Children.Add(_dropIndicatorLine);
            
            // Не меняем selectedTrackIndex! Подсветка делается через IsDragOver
            UpdateTrackLabels();
            
            if (tracks[_dragHoveredTrackIndex].Samples.Length > 0)
                SetStatusText($"Трек {_dragHoveredTrackIndex + 1}: заменит файл");
            else
                SetStatusText($"Трек {_dragHoveredTrackIndex + 1}: пустой");
        }
        
        private void ClearDropIndicators()
        {
            _dragHoveredTrackIndex = -1;
            if (_dropIndicatorLine != null && TracksContainer.Children.Contains(_dropIndicatorLine))
            {
                TracksContainer.Children.Remove(_dropIndicatorLine);
                _dropIndicatorLine = null;
            }
        }
        
        private void TracksBorder_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(TracksContainer);
            int clickedTrack = (int)(point.Y / (TrackHeight + TrackMargin));
            if (clickedTrack >= 0 && clickedTrack < tracks.Count)
            {
                selectedTrackIndex = clickedTrack;
                focusedClipIndex = clickedTrack;
                _contextMenu?.UpdateMenuState();
                _contextMenu!.IsOpen = true;
                e.Handled = true;
            }
        }

        private void TracksBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(TracksContainer);
            int clickedTrack = (int)(point.Y / (TrackHeight + TrackMargin));
            
            // Всегда сбрасываем выделение при клике вне waveform
            ClearSelection();
            
            if (clickedTrack >= 0 && clickedTrack < tracks.Count)
            {
                selectedTrackIndex = clickedTrack;
                UpdateTrackLabels();
                
                // Обновляем окно инструментов если открыто
                UpdateInstrumentsWindow();
                
                if (tracks[clickedTrack].Samples.Length > 0)
                {
                    var fileName = Path.GetFileName(tracks[clickedTrack].SourceFile ?? "");
                    SetStatusText($"Выбран трек {clickedTrack + 1}: \"{fileName}\"");
                }
                else
                {
                    SetStatusText($"Выбран трек {clickedTrack + 1}: пустой");
                }
            }
            else
            {
                // Клик в пустое место (ниже треков) — просто сбрасываем выделение
                SetStatusText("Выделение сброшено");
                UpdateTrackLabels();
            }
        }
        
        private void OnDrop(object sender, DragEventArgs e)
        {
            // Сохраняем индекс трека ДО очистки!
            int targetTrack = _dragHoveredTrackIndex;
            
            // Если _dragHoveredTrackIndex невалиден, вычисляем из позиции курсора
            if (targetTrack < 0 || targetTrack >= tracks.Count)
            {
                // Вычисляем трек из текущей позиции мыши
                var pos = e.GetPosition(TracksBorder);
                double scrollOffset = TracksScroller.VerticalOffset;
                double adjustedY = pos.Y + scrollOffset;
                targetTrack = (int)(adjustedY / (TrackHeight + TrackMargin));
                
                // Ограничиваем
                if (targetTrack < 0) targetTrack = 0;
                if (targetTrack >= tracks.Count) targetTrack = tracks.Count - 1;
            }
            
            // Запоминаем какой трек был выбран ДО drag для восстановления
            int previousTrack = _trackIndexBeforeDrag >= 0 ? _trackIndexBeforeDrag : 0;
            
            // Очищаем индикаторы ПОСЛЕ получения targetTrack
            ClearDropIndicators();
            HideTrackHighlight();
            HideGhostAdorner();
            
            // Обработка внешних файлов (drag из проводника)
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (AudioExtensions.Contains(ext))
                        LoadFileToTrackOnTrack(file, targetTrack);
                }
                SetStatusText($"Загружено в трек {targetTrack + 1}");
            }
            // Обработка FileItem из TreeView (FL Studio ghost drag)
            else if (e.Data.GetDataPresent(typeof(FileItem)))
            {
                var fileItem = (FileItem)e.Data.GetData(typeof(FileItem));
                LoadFileToTrackOnTrack(fileItem.FullPath, targetTrack);
                SetStatusText($"Загружено: {fileItem.Name} -> Трек {targetTrack + 1}");
            }
            
            // ВОССТАНАВЛИВАЕМ выбор который был ДО drag
            selectedTrackIndex = previousTrack;
            UpdateTrackLabels();
            
            _isDraggingFile = false;
            _draggedFileItem = null;
        }
        
        // ========== FL Studio Style Track Drag-Drop ==========
        // Drag-drop на КАЖДЫЙ labelPanel конкретного трека
        
        // Вспомогательный метод для поиска trackIndex из любого sender
        private int GetTrackIndexFromSender(object sender)
        {
            if (sender == null) return -1;
            
            var element = sender as DependencyObject;
            if (element == null) return -1;
            
            // Сначала проверяем Tag у самого sender
            if (sender is FrameworkElement fe && fe.Tag is int directTag)
                return directTag;
            
            // Иначе ищем вверх по дереву Border с Tag
            while (element != null)
            {
                if (element is Border border && border.Tag is int trackIdx)
                    return trackIdx;
                element = VisualTreeHelper.GetParent(element);
            }
            
            return -1;
        }
        
        private void LabelPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(FileItem)))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                
                int trackIndex = GetTrackIndexFromSender(sender);
                
                if (trackIndex >= 0 && trackIndex < tracks.Count)
                {
                    // Сбрасываем все и устанавливаем для этого трека
                    foreach (var t in tracks) { t.IsDropTarget = false; t.IsDropHighlighted = false; }
                    tracks[trackIndex].IsDropTarget = true;
                    tracks[trackIndex].IsDropHighlighted = true;
                    
                    UpdateTrackLabels();
                    
                    _dragHoveredTrackIndex = trackIndex;
                    _trackIndexBeforeDrag = selectedTrackIndex;
                    
                    if (tracks[trackIndex].Samples.Length > 0)
                        SetStatusText($"Трек {trackIndex + 1}: заменит \"{Path.GetFileName(tracks[trackIndex].SourceFile)}\"");
                    else
                        SetStatusText($"Трек {trackIndex + 1}: пустой — загрузить");
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        
        private void LabelPanel_DragLeave(object sender, DragEventArgs e)
        {
            // НЕ сбрасываем! Рамка и подсветка остаются, будут обновлены в DragOver другого трека
            e.Handled = true;
        }
        
        private void LabelPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(FileItem)))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                
                int trackIndex = GetTrackIndexFromSender(sender);
                if (trackIndex >= 0 && trackIndex < tracks.Count)
                {
                    // Проверяем - изменился ли трек под курсором
                    int? currentDropTarget = tracks.FirstOrDefault(t => t.IsDropTarget)?.TrackIndex;
                    
                    if (currentDropTarget == null || currentDropTarget != trackIndex)
                    {
                        // Сбрасываем все IsDropTarget и IsDropHighlighted
                        foreach (var t in tracks) { t.IsDropTarget = false; t.IsDropHighlighted = false; }
                        // Устанавливаем для нового трека
                        tracks[trackIndex].IsDropTarget = true;
                        tracks[trackIndex].IsDropHighlighted = true;
                        
                        _dragHoveredTrackIndex = trackIndex;
                        UpdateTrackLabels();
                    }
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        
        private void LabelPanel_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            
            int trackIndex = GetTrackIndexFromSender(sender);
            
            if (trackIndex >= 0 && trackIndex < tracks.Count)
            {
                // Сбрасываем IsDropTarget (рамка исчезает), но оставляем IsDropHighlighted (подсветка остаётся)
                foreach (var t in tracks) t.IsDropTarget = false;
                // IsDropHighlighted остаётся!
                
                int previousTrack = _trackIndexBeforeDrag >= 0 ? _trackIndexBeforeDrag : selectedTrackIndex;
                int targetTrack = trackIndex;
                
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLower();
                        if (AudioExtensions.Contains(ext))
                        {
                            int loadTrack = targetTrack;
                            int restoreTrack = previousTrack;
                            Task.Run(() => LoadFileAsyncAndKeepHighlight(file, loadTrack, restoreTrack));
                        }
                    }
                    SetStatusText($"Загружается в трек {targetTrack + 1}...");
                }
                else if (e.Data.GetDataPresent(typeof(FileItem)))
                {
                    var fileItem = (FileItem)e.Data.GetData(typeof(FileItem));
                    int restoreTrack = previousTrack;
                    Task.Run(() => LoadFileAsyncAndKeepHighlight(fileItem.FullPath, targetTrack, restoreTrack));
                    SetStatusText($"Загружается: {fileItem.Name} -> Трек {targetTrack + 1}");
                }
            }
            
            _isDraggingFile = false;
        }
        
        private void LoadFileAsyncAndKeepHighlight(string path, int targetTrack, int restoreTrack)
        {
            LoadFileAsync(path, targetTrack);
            
            Dispatcher.Invoke(() =>
            {
                // Сбрасываем ВСЕ IsDragOver
                foreach (var track in tracks)
                {
                    track.IsDragOver = false;
                }
                
                // IsDropHighlighted остаётся! Сбросится только при клике на трек
                
                selectedTrackIndex = restoreTrack;
                UpdateTrackLabels();
            });
        }
        
        private void Clip_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (sender is FrameworkElement element && element.DataContext is AudioClip clip)
                {
                    DragDrop.DoDragDrop(element, clip, DragDropEffects.Move);
                }
            }
        }
        
        private void Clip_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(AudioClip)) && sender is FrameworkElement element)
            {
                var clip = (AudioClip)e.Data.GetData(typeof(AudioClip));
                var pos = e.GetPosition(element);
                
                // Move clip to new position
                clip.StartTime = pos.X / pixelsPerSecond;
                
                RebuildMixer();
                DrawTimeline(rebuildTracks: true);
            }
        }

        // ========== End Drag & Drop ==========
    }
}
