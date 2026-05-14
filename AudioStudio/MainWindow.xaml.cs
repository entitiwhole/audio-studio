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
        private readonly string icon;
        private readonly string fileName;
        private Point _offset = new Point(15, -30);
        
        public GhostAdorner(UIElement adornedElement, string fileIcon, string fileNameText) 
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
            
            stackPanel.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 18,
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
                icon,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"),
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
        public string Icon => GetFileIcon(Extension);
        public string Duration { get; set; } = "";
        public long Size { get; set; }
        public List<object> Children { get; set; } = new(); // для совместимости с TreeView
        public string DisplayName => Name;

        public static string GetFileIcon(string ext)
        {
            return ext.ToLower() switch
            {
                ".wav" => "🔊",
                ".mp3" => "🎵",
                ".flac" => "🎶",
                ".ogg" => "🎼",
                ".m4a" => "🎧",
                ".aiff" or ".aif" => "🎹",
                _ => "📄"
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
        public string Icon => "📁";
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
        
        private DispatcherTimer? playTimer;
        private DispatcherTimer? _previewTimer;
        private bool isPlaying;
        private double currentTime;
        
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
        private readonly Dictionary<int, float[]> _spectrogramCache = new();
        
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
        
        public bool HasSelection() => SelectionManager?.HasSelection ?? false;
        
        public bool HasClipboard() => ClipboardData != null && ClipboardData.Length > 0;
        
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
                
                StatusText.Text = $"Выделено: {FormatTime(duration)}";
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
            BtnCut.IsEnabled = false;
            BtnCopy.IsEnabled = false;
            BtnDelete.IsEnabled = false;
            StatusText.Text = "Готов к работе";
        }
        
        /// <summary>
        /// Clear selection and hide overlay
        /// </summary>
        public void ClearSelection()
        {
            _selectionStartTime = -1;
            _selectionEndTime = -1;
            
            // Clear SelectionManager too
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
            TimeRulerControl.UpdateTicks();
            if (TimeRulerControl.SelectionStart >= 0)
                TimeRulerControl.UpdateSelectionHighlight();
            
            if (_selectionStartTime >= 0 && _selectionEndTime >= 0)
                UpdateSelectionUI();
        }
        
        public void SelectAll()
        {
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
                    StatusText.Text = "Выделено: весь клип";
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
            
            // Optimized playback timer (60 FPS for smooth UI)
            playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            playTimer.Tick += (s, e) => UpdatePlayheadFromEngine();
            
            // Subscribe to stop event
            _audio.OnPlaybackStopped += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    isPlaying = false;
                    BtnPlay.Content = "▶";
                    playTimer.Stop();
                    currentTime = 0;
                    DrawTimeline();
                    StatusText.Text = "Воспроизведение завершено";
                });
            };
            
            KeyDown += MainWindow_KeyDown;
            KeyDown += MainWindow_KeyDown_Global;
            KeyUp += MainWindow_KeyUp_Global;
            
            Loaded += (s, e) =>
            {
                // Create selection overlay on the separate layer (чисто визуальный)
                _selectionOverlay = new SelectionOverlaySimple(SelectionCanvas);
                
                // Выравниваем TimeRuler под начало waveform (после лейбла)
                TimeRulerControl.Offset = TrackLabelWidth;
                TimeRulerControl.ScrollOffset = 0;
                TimeRulerControl.TotalDuration = GetTotalDuration();
                TimeRulerControl.UpdateTicks();
                
                DrawTimeline();
                UpdateTrackLabels();
                InitializeBrowser();
                
                // Attach drag-drop handlers to TreeView
                SetupTreeViewDragDrop();
            };
            
            SizeChanged += (s, e) => 
            {
                if (!_isResizing) DrawTimeline();
            };
        }
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                DragMove();
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
                MaximizeBtn.Content = "☐";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeBtn.Content = "❐";
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
            DrawTimeline();
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
        
        private void ShowGhostAdorner(string icon, string fileName)
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
                
                StatusText.Text = $"Загружено дисков: {drives.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки дисков: {ex.Message}";
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
                    StatusText.Text = $"Выбран: {file.Name} | Двойной клик или drag -> Трек {currentTrack + 1} (заменит \"{currentFile}\")";
                }
                else
                {
                    StatusText.Text = $"Выбран: {file.Name} | Двойной клик или drag -> Трек {currentTrack + 1}";
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
                    // Загружаем файл на выбранный трек
                    int targetTrack = selectedTrackIndex >= 0 ? selectedTrackIndex : 0;
                    if (targetTrack >= tracks.Count) targetTrack = 0;
                    
                    LoadFileToTrackOnTrack(file.FullPath, targetTrack);
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
                
                StatusText.Text = $"Загружено файлов: {fileCount}";
            }
            catch (UnauthorizedAccessException)
            {
                StatusText.Text = "Нет доступа к папке";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
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
            try
            {
                using var reader = new AudioFileReader(path);
                var duration = reader.TotalTime;
                return duration.Hours > 0 
                    ? $"{duration.Hours:D1}:{duration.Minutes:D2}:{duration.Seconds:D2}" 
                    : $"{duration.Minutes:D1}:{duration.Seconds:D2}";
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
                StatusText.Text = $"Браузер: {_rootPath}";
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

Воспроизведение:
  Space     - Воспроизведение/Пауза
  Enter     - Стоп и в начало

Редактирование:
  Ctrl+X    - Вырезать
  Ctrl+C    - Копировать
  Ctrl+V    - Вставить
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

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.X: Cut_Click(sender, e); break;
                    case Key.C: Copy_Click(sender, e); break;
                    case Key.V: Paste_Click(sender, e); break;
                    case Key.Z: Undo_Click(sender, e); break;
                    case Key.Y: Redo_Click(sender, e); break;
                    case Key.D: SelectAll(); break;
                }
            }
            else if (e.Key == Key.Delete)
            {
                Delete_Click(sender, e);
            }
            else if (e.Key == Key.Space)
            {
                Play_Click(sender, e);
                e.Handled = true;
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
                DrawTimeline();
            }
            else if (e.Key == Key.End)
            {
                currentTime = GetTotalDuration();
                UpdatePreviewPosition();
                DrawTimeline();
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
                    DrawWaveformInCanvas(waveformCanvas, track, trackWidth);
                    
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
        }
        
        private void DrawWaveformInCanvas(Canvas canvas, AudioClip clip, double width)
        {
            if (clip.Samples == null || clip.Samples.Length == 0) return;
            
            double displayWidth = Math.Min(Math.Max(1, width), 5000);
            
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
                int startSample = f * hop * channels;
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
            data = specData;
        }

        private void DrawSpectrogramInCanvas(Canvas canvas, AudioClip clip, double width)
        {
            if (clip.Samples == null || clip.Samples.Length == 0) return;

            double displayWidth = Math.Min(Math.Max(1, width), 5000);
            int w = Math.Min(Math.Max(1, (int)displayWidth), 5000);
            int h = Math.Max(1, TrackHeight - 4);

            EnsureSpectrogramCache(clip, out float[] cacheEntry, out int numFrames, out int fftSize);
            int bins = fftSize / 2;
            int dataOffset = 2;

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
                WriteableBitmap bmp;
                if (specImage?.Source is WriteableBitmap existing && existing.PixelWidth == w && existing.PixelHeight == h)
                {
                    bmp = existing;
                }
                else
                {
                    bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
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
                }

                bmp.Lock();
                unsafe
                {
                    IntPtr buffer = bmp.BackBuffer;
                    int* pixels = (int*)buffer.ToPointer();
                    int stride = bmp.BackBufferStride;

                    int bgColor = unchecked((int)0xFF1E1E1E);
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
                bmp.Unlock();
            }
            catch { }
        }

        private static int HeatMapColor(float t)
        {
            byte r, g, b;
            if (t < 0.25f)
            {
                float v = t / 0.25f;
                r = 0; g = (byte)(v * 60); b = (byte)(80 + v * 175);
            }
            else if (t < 0.5f)
            {
                float v = (t - 0.25f) / 0.25f;
                r = 0; g = (byte)(60 + v * 195); b = (byte)(255 - v * 80);
            }
            else if (t < 0.75f)
            {
                float v = (t - 0.5f) / 0.25f;
                r = (byte)(v * 255); g = (byte)(255); b = (byte)(175 - v * 175);
            }
            else
            {
                float v = (t - 0.75f) / 0.25f;
                r = (byte)(255); g = (byte)(255 - v * 200); b = (byte)(0);
            }
            return (255 << 24) | (b << 16) | (g << 8) | r;
        }

        // ========== Browser/Tracks Splitter ==========
        private Point _splitterStart;
        private double _splitterStartWidth;
        private bool _isSplitterDragging = false;
        
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
                
                // Ограничения
                newWidth = Math.Max(150, newWidth);
                newWidth = Math.Min(ActualWidth - 200, newWidth);
                
                BrowserColumn.Width = new GridLength(newWidth);
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
            
            DrawTimeline();
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
            DrawTimeline();
        }
        
        public void DrawTimeline()
        {
            UpdateTrackLabels();
            TimeRulerControl.TotalDuration = GetTotalDuration();
            TimeRulerControl.UpdateTicks();
        }

        private double GetTotalDuration()
        {
            double max = 0;
            foreach (var track in tracks)
            {
                double end = track.StartTime + track.Duration;
                if (end > max) max = end;
            }
            return Math.Max(max, 10);
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
            BtnPlay.IsEnabled = enable && tracks.Any(t => t.Samples.Length > 0);
            BtnStop.IsEnabled = enable;
            BtnApply.IsEnabled = enable && tracks.Any(t => t.Samples.Length > 0);
            BtnCut.IsEnabled = enable && SelectionManager.HasSelection;
            BtnCopy.IsEnabled = enable && SelectionManager.HasSelection;
            BtnPaste.IsEnabled = enable && ClipboardData != null;
            BtnDelete.IsEnabled = enable && SelectionManager.HasSelection;
            UpdateCommandButtons();
        }
        
        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog 
            { 
                Filter = "Audio Files|*.wav;*.mp3;*.flac|All Files|*.*" 
            };
            if (dialog.ShowDialog() == true)
            {
                LoadFileToTrack(dialog.FileName);
            }
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
                StatusText.Text = $"Error loading file: {ex.Message}";
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
                        StatusText.Text = "Мало памяти! Очистите треки.";
                    });
                    return;
                }
                
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Чтение файла...";
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
                            StatusText.Text = "Файл слишком длинный (макс 2 часа)";
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
                        StatusText.Text = "Декодирование...";
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
                                StatusText.Text = $"Декодирование... {percent}% ({secondsRead:F0}/{duration.TotalSeconds:F0}с)";
                            });
                        }
                    }
                    
                    samples = allSamples.ToArray();
                    allSamples.Clear(); // Free List memory
                }
                
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Расчёт waveform...";
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
                    DrawTimeline();
                    UpdateTrackLabels();
                    
                    // Create command for undo and add to history (but don't re-execute)
                    var command = new LoadFileCommand(this, path, trackIndex);
                    // Set the previous state that was captured before loading
                    command.SetPreviousState(previousSamples, previousSampleRate, previousChannels, path, Path.GetFileName(path));
                    // Execute it to add to undo stack
                    _commandManager.Execute(command);
                    
                    StatusText.Text = $"Загружено: {track.Name}";
                    CurrentTimeText.Text = "00:00";
                    TotalTimeText.Text = FormatTime(track.Duration);
                    EnableControls(true);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = $"Ошибка: {ex.Message}";
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
            // Очищаем старые waveform при перезагрузке
            if (tracks.Count < _waveformPeaks.Count)
            {
                // Удаляем пики удалённых треков
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
            if (tracks.All(t => t.Samples.Length == 0)) return;
            
            // Используем AudioEngine для seek
            _audio.Seek((float)targetTime);
            currentTime = targetTime;
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
        
        private void UpdatePlayheadFromEngine()
        {
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
            
            CurrentTimeText.Text = FormatTime(currentTime);
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
                StatusText.Text = $"Выделение: {FormatTime(dur)}";
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
                StatusText.Text = $"Выделение: {FormatTime(duration)}";
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
                StatusText.Text = $"Выделено: {FormatTime(dur)}";
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
                    
                    StatusText.Text = $"Выделено: {FormatTime(duration)}";
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
                playTimer.Stop();
                StatusText.Text = "Пауза";
            }
            else
            {
                if (currentTime >= GetTotalDuration())
                    currentTime = 0;
                
                _audio.Play();
                isPlaying = true;
                SetPlayIcon(true);
                playTimer.Start();
                StatusText.Text = "Воспроизведение...";
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _audio.Stop();
            isPlaying = false;
            SetPlayIcon(false);
            playTimer.Stop();
            currentTime = 0;
            DrawTimeline();
            StatusText.Text = "Остановлено";
            CurrentTimeText.Text = "00:00";
            TotalTimeText.Text = FormatTime(GetTotalDuration());
        }

        public void Cut_Click(object sender, RoutedEventArgs e)
        {
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
            DrawTimeline();
            
            SelectionManager.ClearSelection();
            ClearSelection();
            StatusText.Text = $"Вырезано: {FormatTime((double)length / (track.SampleRate * track.Channels))}";
        }

        public void Copy_Click(object sender, RoutedEventArgs e)
        {
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

            StatusText.Text = $"Скопировано: {FormatTime((double)length / (ClipboardSampleRate * ClipboardChannels))}";
        }

        public void Paste_Click(object sender, RoutedEventArgs e)
        {
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
            DrawTimeline();
            
            StatusText.Text = $"Вставлено: {FormatTime((double)ClipboardData.Length / (ClipboardSampleRate * ClipboardChannels))}";
        }

        public void Delete_Click(object sender, RoutedEventArgs e)
        {
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
            DrawTimeline();
            
            SelectionManager.ClearSelection();
            ClearSelection();
            StatusText.Text = "Удалено";
        }

        public void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_commandManager.CanUndo)
            {
                _commandManager.Undo();
                _spectrogramCache.Clear();
                _waveformPeaks.Clear();
                _waveformBitmaps.Clear();
                DrawTimeline();
                EnableControls(true);
                StatusText.Text = $"Отменено: {_commandManager.LastUndoDescription}";
            }
        }

        public void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_commandManager.CanRedo)
            {
                _commandManager.Redo();
                _spectrogramCache.Clear();
                _waveformPeaks.Clear();
                _waveformBitmaps.Clear();
                DrawTimeline();
                EnableControls(true);
                StatusText.Text = $"Повторено: {_commandManager.LastRedoDescription}";
            }
        }

        private void AddTrack_Click(object sender, RoutedEventArgs e)
        {
            if (tracks.Count >= MaxTracks)
            {
                StatusText.Text = $"Достигнут лимит треков ({MaxTracks})";
                return;
            }
            
            var command = new AddTrackCommand(this);
            _commandManager.Execute(command);
            StatusText.Text = $"Добавлена дорожка {tracks.Count}";
        }

        private void RemoveTrack_Click(object sender, RoutedEventArgs e)
        {
            if (tracks.Count <= 1) return;
            
            var command = new RemoveTrackCommand(this, tracks.Count - 1);
            _commandManager.Execute(command);
            
            if (selectedTrackIndex >= tracks.Count)
                selectedTrackIndex = tracks.Count - 1;
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            StatusText.Text = "Дорожка удалена";
        }

        public void ClearTrack_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
            var track = tracks[selectedTrackIndex];
            if (track.Samples.Length == 0) return;

            track.Samples = Array.Empty<float>();
            track.SourceFile = null;
            track.Name = $"Дорожка {selectedTrackIndex + 1}";

            _waveformPeaks.Remove(selectedTrackIndex);
            _waveformBitmaps.Remove(selectedTrackIndex);
            _spectrogramCache.Remove(selectedTrackIndex);
            ClearSelection();
            DrawTimeline();
            StatusText.Text = $"Трек {selectedTrackIndex + 1} очищен";
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Сохранение проекта...";
            MessageBox.Show("Функция сохранения проекта будет добавлена", "Сохранение", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Готов к работе";
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Экспорт...";
            MessageBox.Show("Функция экспорта будет добавлена", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Готов к работе";
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            pixelsPerSecond = Math.Min(500, pixelsPerSecond * 1.5);
            ZoomSlider.Value = pixelsPerSecond;
            DrawTimeline();
            StatusText.Text = $"Масштаб: {pixelsPerSecond:F0} пикс/сек";
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            pixelsPerSecond = Math.Max(5, pixelsPerSecond / 1.5);
            ZoomSlider.Value = pixelsPerSecond;
            DrawTimeline();
            StatusText.Text = $"Масштаб: {pixelsPerSecond:F0} пикс/сек";
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            pixelsPerSecond = 50;
            ZoomSlider.Value = 50;
            DrawTimeline();
            StatusText.Text = "Масштаб сброшен";
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
            DrawTimeline();
            StatusText.Text = _showSpectrogram ? "Режим: спектрограмма" : "Режим: waveform";
        }

        private void ZoomSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ZoomSlider != null && ZoomValue != null)
            {
                pixelsPerSecond = ZoomSlider.Value;
                ZoomValue.Text = $"{pixelsPerSecond:F0}%";
                
                DrawTimeline();
                UpdatePlayheadUI();
            }
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
                if (tracks == null || tracks.Count == 0)
                {
                    MessageBox.Show("No tracks available", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count)
                {
                    MessageBox.Show("Select a track first", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                var track = tracks[selectedTrackIndex];
                if (track == null || track.Samples == null || track.Samples.Length == 0)
                {
                    MessageBox.Show("Load audio to track first", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // FL Studio style: если окно уже открыто, просто активируем его
                if (_instrumentsWindow != null && _instrumentsWindow.IsVisible)
                {
                    _instrumentsWindow.Activate();
                    _instrumentsWindow.LoadTrack(track);
                    return;
                }
                
                // Создаём новое окно (не модальное)
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
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        // Обновление InstrumentsWindow при смене трека
        public void UpdateInstrumentsWindow()
        {
            if (_instrumentsWindow == null || !_instrumentsWindow.IsVisible) return;
            
            if (selectedTrackIndex >= 0 && selectedTrackIndex < tracks.Count)
            {
                var track = tracks[selectedTrackIndex];
                if (track?.Samples?.Length > 0)
                    _instrumentsWindow.LoadTrack(track);
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
            if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
            
            var track = tracks[selectedTrackIndex];
            if (track?.Samples == null || track.Samples.Length == 0) return;
            
            // Проверяем доступность NativeAudio
            if (!_nativeAudioAvailable)
            {
                if (!CheckNativeAudio())
                {
                    StatusText.Text = "⚠ Effects unavailable (DLL not found)";
                    return;
                }
            }
            
            try
            {
                ApplyInstrumentsChanges(track, _instrumentsWindow);
            }
            catch (Exception ex)
            {
                StatusText.Text = "⚠ Effect error: " + ex.Message;
            }
        }
        
        // Preview трека с эффектами
        public void PreviewTrackWithEffects()
        {
            if (selectedTrackIndex < 0 || selectedTrackIndex >= tracks.Count) return;
            
            var track = tracks[selectedTrackIndex];
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
                playTimer.Start();
                StatusText.Text = "▶ Preview (no effects - DLL missing)";
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
                
                StatusText.Text = "▶ Preview: " + track.Name;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Preview error: " + ex.Message;
            }
            
            // Воспроизводим
            _audio.PlayPreview(previewSamples, track.SampleRate, track.Channels);
            isPlaying = true;
            BtnPlay.Content = "⏸";
            playTimer.Start();
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
                DrawTimeline();
                StatusText.Text = "Effects applied to: " + track.Name;
            }
            catch (Exception ex)
            {
                StatusText.Text = "⚠ Effect error: " + ex.Message;
            }
        }
        
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Готов к работе";
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
                
                StatusText.Text = "Отпустите файл на трек для загрузки";
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
            StatusText.Text = "Готов к работе";
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
                StatusText.Text = $"Трек {trackIndex + 1}: заменит \"{Path.GetFileName(track.SourceFile)}\"";
            else
                StatusText.Text = $"Трек {trackIndex + 1}: пустой — загрузить";
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
                StatusText.Text = $"Трек {_dragHoveredTrackIndex + 1}: заменит файл";
            else
                StatusText.Text = $"Трек {_dragHoveredTrackIndex + 1}: пустой";
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
                    StatusText.Text = $"Выбран трек {clickedTrack + 1}: \"{fileName}\"";
                }
                else
                {
                    StatusText.Text = $"Выбран трек {clickedTrack + 1}: пустой";
                }
            }
            else
            {
                // Клик в пустое место (ниже треков) — просто сбрасываем выделение
                StatusText.Text = "Выделение сброшено";
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
                StatusText.Text = $"Загружено в трек {targetTrack + 1}";
            }
            // Обработка FileItem из TreeView (FL Studio ghost drag)
            else if (e.Data.GetDataPresent(typeof(FileItem)))
            {
                var fileItem = (FileItem)e.Data.GetData(typeof(FileItem));
                LoadFileToTrackOnTrack(fileItem.FullPath, targetTrack);
                StatusText.Text = $"Загружено: {fileItem.Name} -> Трек {targetTrack + 1}";
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
                        StatusText.Text = $"Трек {trackIndex + 1}: заменит \"{Path.GetFileName(tracks[trackIndex].SourceFile)}\"";
                    else
                        StatusText.Text = $"Трек {trackIndex + 1}: пустой — загрузить";
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
                    StatusText.Text = $"Загружается в трек {targetTrack + 1}...";
                }
                else if (e.Data.GetDataPresent(typeof(FileItem)))
                {
                    var fileItem = (FileItem)e.Data.GetData(typeof(FileItem));
                    int restoreTrack = previousTrack;
                    Task.Run(() => LoadFileAsyncAndKeepHighlight(fileItem.FullPath, targetTrack, restoreTrack));
                    StatusText.Text = $"Загружается: {fileItem.Name} -> Трек {targetTrack + 1}";
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
                DrawTimeline();
            }
        }

        // ========== End Drag & Drop ==========
    }
}
