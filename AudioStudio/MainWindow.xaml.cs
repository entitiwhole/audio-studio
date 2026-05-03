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
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Path = System.IO.Path;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

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
        
        private Stack<List<AudioClip>> undoStack = new();
        private Stack<List<AudioClip>> redoStack = new();
        
        private const int TrackHeight = 100;
        private const int TrackMargin = 3;
        private const int LabelWidth = 70;
        private const double MinPixelsPerSecond = 5;
        private const int MaxTracks = 50; // Ограничение на количество треков
        private const int MaxClipsPerTrack = 100; // Ограничение на клипы

        // ========== ОПТИМИЗАЦИЯ: Поля для оптимизации ==========
        private readonly WaveformCache _waveformCache = new();
        private readonly Dictionary<int, WriteableBitmap> _waveformBitmaps = new();
        private readonly Dictionary<int, float[]> _waveformPeaks = new();
        private bool _isUpdatingPlayhead = false;
        private readonly double _lastPlayheadUpdate = 0;
        
        // Браузер файлов
        private string _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        private string _currentPath = "";
        private List<FileItem> _currentFiles = new();
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aiff", ".aif", ".wma", ".aac" };

        public MainWindow()
        {
            InitializeComponent();
            
            tracks.Add(CreateEmptyTrack(0));
            tracks.Add(CreateEmptyTrack(1));
            
            // Оптимизированный таймер воспроизведения (60 FPS для плавного UI)
            playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            playTimer.Tick += (s, e) => UpdatePlayheadFromEngine();
            
            // Подписка на событие остановки
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
                DrawTimeline();
                UpdateTrackLabels();
                InitializeBrowser();
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
                // Захватываем текущий трек СРАЗУ, не позже!
                int targetTrack = selectedTrackIndex >= 0 ? selectedTrackIndex : 0;
                if (targetTrack >= tracks.Count) targetTrack = 0;
                
                if (tracks[targetTrack].Samples.Length > 0)
                {
                    var currentFile = Path.GetFileName(tracks[targetTrack].SourceFile ?? "");
                    StatusText.Text = $"Файл \"{file.Name}\" -> Трек {targetTrack + 1} (заменит \"{currentFile}\")";
                }
                else
                {
                    StatusText.Text = $"Файл \"{file.Name}\" -> Трек {targetTrack + 1}";
                }
                
                // Передаём захваченный индекс вместо использования selectedTrackIndex в async методе
                LoadFileToTrackOnTrack(file.FullPath, targetTrack);
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
        
        private void SelectAll()
        {
            if (selectedTrackIndex >= 0 && selectedTrackIndex < tracks.Count)
            {
                var track = tracks[selectedTrackIndex];
                if (track.Samples.Length > 0)
                {
                    selectionStart = track.StartTime;
                    selectionEnd = track.StartTime + track.Duration;
                    track.IsSelected = true;
                    DrawTimeline();
                    UpdateSelectionDisplay();
                    EnableControls(true);
                    StatusText.Text = "Выделено: весь клип";
                }
            }
        }

        private void UpdateTrackLabels()
        {
            TracksPanel.Children.Clear();
            _playheadLines.Clear(); // Оптимизация: очищаем кэш playhead
            _endOfTrackLines.Clear(); // Очищаем индикаторы конца
            
            foreach (var track in tracks)
            {
                bool isSelected = track.TrackIndex == selectedTrackIndex;
                
                var trackRow = new Grid
                {
                    Height = TrackHeight,
                    Margin = new Thickness(0, 0, 0, 1),
                    Tag = track.TrackIndex
                };
                
                trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                var labelPanel = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Cursor = Cursors.Hand,
                    Tag = track.TrackIndex,
                    AllowDrop = true // Разрешаем drop на этот трек (FL Studio стиль)
                };
                
                // Drag-drop обработчики для конкретного трека (FL Studio стиль)
                labelPanel.DragEnter += Track_DragEnter;
                labelPanel.DragLeave += Track_DragLeave;
                labelPanel.DragOver += Track_DragOver;
                labelPanel.Drop += Track_Drop;
                
                var labelGrid = new Grid();
                
                labelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                labelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                labelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                var trackNumber = new TextBlock
                {
                    Text = $"Track {track.TrackIndex + 1}",
                    Foreground = isSelected ? 
                        new SolidColorBrush(Color.FromRgb(120, 129, 255)) : 
                        new SolidColorBrush(Color.FromRgb(150, 150, 155)),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(8, 5, 0, 0)
                };
                
                var fileName = new TextBlock
                {
                    Text = track.Samples.Length > 0 ? 
                        (Path.GetFileName(track.SourceFile) ?? "Файл") : 
                        "Пусто",
                    Foreground = isSelected ? 
                        new SolidColorBrush(Color.FromRgb(180, 180, 185)) : 
                        new SolidColorBrush(Color.FromRgb(120, 120, 125)),
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
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 155)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 8, 8),
                    Tag = track.TrackIndex
                };
                playButton.Click += (s, e) => 
                {
                    selectedTrackIndex = track.TrackIndex;
                    UpdateTrackLabels();
                };
                
                Grid.SetRow(trackNumber, 0);
                Grid.SetRow(fileName, 1);
                Grid.SetRow(playButton, 2);
                Grid.SetColumn(playButton, 1);
                
                labelGrid.Children.Add(trackNumber);
                labelGrid.Children.Add(fileName);
                labelGrid.Children.Add(playButton);
                
                labelPanel.Child = labelGrid;
                labelPanel.MouseLeftButtonDown += (s, args) =>
                {
                    selectedTrackIndex = (int)((Border)s).Tag;
                    UpdateTrackLabels();
                };
                
                Grid.SetColumn(labelPanel, 0);
                
                var waveformPanel = new Border
                {
                    Background = isSelected ? 
                        new SolidColorBrush(Color.FromArgb(255, 35, 35, 40)) :
                        new SolidColorBrush(Color.FromRgb(30, 30, 35)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                    BorderThickness = new Thickness(0),
                    Tag = track.TrackIndex,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ClipToBounds = true
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
            if (clip.Samples.Length == 0) return;
            
            // Оптимизация: используем кэшированные пики если есть
            float[] peaks;
            if (_waveformPeaks.TryGetValue(clip.TrackIndex, out peaks))
            {
                DrawWaveformFromPeaks(canvas, peaks, width, clip.TrackIndex);
            }
            else
            {
                // Вычисляем пики и кэшируем
                // Количество пиков = ширина в пикселях для точного соответствия
                peaks = ComputePeaks(clip.Samples, (int)width);
                _waveformPeaks[clip.TrackIndex] = peaks;
                DrawWaveformFromPeaks(canvas, peaks, width, clip.TrackIndex);
            }
        }
        
        // GPU-ускоренный waveform с использованием WriteableBitmap
        private void DrawWaveformFromPeaks(Canvas canvas, float[] peaks, double width, int trackIndex)
        {
            canvas.Children.Clear();
            
            int w = Math.Max(1, (int)width);
            int h = Math.Max(1, TrackHeight - 4);
            
            try
            {
                // Создаём WriteableBitmap для GPU-отрисовки
                var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                
                bmp.Lock();
                
                unsafe
                {
                    IntPtr buffer = bmp.BackBuffer;
                    int* pixels = (int*)buffer.ToPointer();
                    int stride = bmp.BackBufferStride;
                    
                    // Цвета (BGRA format)
                    int bgColor = unchecked((int)0xFF1E1E1E);    // Тёмный фон
                    int fgColor = unchecked((int)0xFFFF7881);   // Цвет waveform
                    int centerColor = unchecked((int)0x40969696); // Центральная линия
                    
                    // Очищаем буфер
                    for (int i = 0; i < w * h; i++)
                    {
                        pixels[i] = bgColor;
                    }
                    
                    // Рисуем waveform
                    double step = (double)peaks.Length / w;
                    int midY = h / 2;
                    
                    for (int x = 0; x < w; x++)
                    {
                        int peakIdx = Math.Min((int)(x * step), peaks.Length - 1);
                        if (peakIdx < 0) peakIdx = 0;
                        
                        float peak = peaks[peakIdx];
                        int amplitude = Math.Min(h / 2, (int)(peak * h / 2));
                        
                        // Рисуем вертикальную линию для каждого столбика
                        for (int y = midY - amplitude; y <= midY + amplitude; y++)
                        {
                            if (y >= 0 && y < h)
                            {
                                int offset = y * (stride / 4) + x;
                                pixels[offset] = fgColor;
                            }
                        }
                        
                        // Центральная линия
                        if (midY >= 0 && midY < h)
                        {
                            int offset = midY * (stride / 4) + x;
                            pixels[offset] = centerColor;
                        }
                    }
                }
                
                // Применяем изменения
                bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
                bmp.Unlock();
                
                // Создаём Image и добавляем на canvas
                var image = new System.Windows.Controls.Image
                {
                    Source = bmp,
                    Width = w,
                    Height = h
                };
                
                Canvas.SetLeft(image, 0);
                Canvas.SetTop(image, 2);
                
                canvas.Children.Add(image);
            }
            catch
            {
                // Fallback: простая отрисовка если GPU не доступен
                var centerLine = new Line
                {
                    X1 = 0,
                    Y1 = h / 2 + 2,
                    X2 = w,
                    Y2 = h / 2 + 2,
                    Stroke = new SolidColorBrush(Color.FromArgb(150, 150, 150, 150)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(centerLine);
            }
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
        
        private void DrawTimeline()
        {
            UpdateTrackLabels();
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

        private void UpdateSelectionDisplay()
        {
        }

        private void ClearSelection()
        {
            selectionStart = -1;
            selectionEnd = -1;
            UpdateSelectionDisplay();
            BtnCut.IsEnabled = false;
            BtnCopy.IsEnabled = false;
            BtnDelete.IsEnabled = false;
        }

        private void EnableControls(bool enable)
        {
            BtnPlay.IsEnabled = enable && tracks.Any(t => t.Samples.Length > 0);
            BtnStop.IsEnabled = enable;
            BtnApply.IsEnabled = enable && tracks.Any(t => t.Samples.Length > 0);
            BtnCut.IsEnabled = enable && HasSelection();
            BtnCopy.IsEnabled = enable && HasSelection();
            BtnPaste.IsEnabled = enable && clipboard != null;
            BtnDelete.IsEnabled = enable && HasSelection();
            BtnUndo.IsEnabled = undoStack.Count > 0;
            BtnRedo.IsEnabled = redoStack.Count > 0;
        }

        private bool HasSelection()
        {
            return selectionStart >= 0 && selectionEnd >= 0 && Math.Abs(selectionEnd - selectionStart) > 0.05;
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
        
        // Перегрузка для явного указания трека (из TreeView, drag-drop)
        private void LoadFileToTrackOnTrack(string path, int trackIndex)
        {
            if (trackIndex < 0) trackIndex = 0;
            if (trackIndex >= tracks.Count) trackIndex = 0;
            
            Task.Run(() => LoadFileAsync(path, trackIndex));
        }
        
        private void LoadFileAsync(string path, int trackIndex)
        {
            try
            {
                // Проверка памяти перед загрузкой
                long memUsed = GC.GetTotalMemory(false);
                if (memUsed > 500_000_000) // > 500MB
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "Мало памяти! Очистите треки.";
                    });
                    return;
                }
                
                Dispatcher.Invoke(() =>
                {
                    SaveUndoState();
                    StatusText.Text = "Загрузка...";
                });
                
                float[] samples;
                int sampleRate;
                int channels;
                
                // Читаем аудио файл
                using (var reader = new AudioFileReader(path))
                {
                    sampleRate = reader.WaveFormat.SampleRate;
                    channels = reader.WaveFormat.Channels;
                    
                    // Ограничение размера файла (100MB)
                    if (reader.Length > 100_000_000)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = "Файл слишком большой (макс 100MB)";
                        });
                        return;
                    }
                    
                    // Оптимизация: выделяем память сразу
                    var totalSamples = (int)(reader.Length / sizeof(float));
                    samples = new float[totalSamples];
                    
                    var buffer = new float[8192]; // Увеличенный буфер
                    int offset = 0;
                    int read;
                    
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        Array.Copy(buffer, 0, samples, offset, read);
                        offset += read;
                    }
                }
                
                // Вычисляем пики для waveform (в фоне)
                var peaks = ComputePeaks(samples, 1000);
                
                // Обновляем UI в главном потоке
                Dispatcher.Invoke(() =>
                {
                    if (trackIndex >= tracks.Count) trackIndex = 0;
                    
                    var track = tracks[trackIndex];
                    track.Samples = samples;
                    track.SampleRate = sampleRate;
                    track.Channels = channels;
                    track.SourceFile = path;
                    track.Name = Path.GetFileName(path);
                    track.StartTime = 0;
                    
                    // Кэшируем waveform
                    _waveformPeaks[trackIndex] = peaks;
                    
                    RebuildMixer();
                    DrawTimeline();
                    UpdateTrackLabels();
                    
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
            if (samples.Length == 0) return Array.Empty<float>();
            
            var peaks = new float[peakCount];
            int samplesPerPeak = Math.Max(1, samples.Length / peakCount);
            
            for (int i = 0; i < peakCount; i++)
            {
                int start = i * samplesPerPeak;
                int end = Math.Min(start + samplesPerPeak, samples.Length);
                
                float max = 0;
                for (int j = start; j < end; j++)
                {
                    float abs = Math.Abs(samples[j]);
                    if (abs > max) max = abs;
                }
                peaks[i] = max;
            }
            
            return peaks;
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

        private void RebuildMixer()
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
            if (sender is Canvas canvas)
            {
                var pos = e.GetPosition(canvas);
                double playheadX = currentTime * pixelsPerSecond;
                
                if (Math.Abs(pos.X - playheadX) < 20 || IsOnPlayhead(pos, canvas))
                {
                    isDraggingPlayhead = true;
                    dragStartX = pos.X;
                    dragStartTime = currentTime;
                    canvas.CaptureMouse();
                    
                    if (isPlaying)
                    {
                        _audio.Pause();
                    }
                    
                    e.Handled = true;
                }
            }
        }
        
        private bool IsOnPlayhead(Point pos, Canvas canvas)
        {
            double playheadX = currentTime * pixelsPerSecond;
            return Math.Abs(pos.X - playheadX) < 20;
        }
        
        private void WaveformCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDraggingPlayhead && sender is Canvas canvas)
            {
                var pos = e.GetPosition(canvas);
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
                    
                    double playheadX = currentTime * pixelsPerSecond;
                    foreach (var child in TracksPanel.Children)
                    {
                        if (child is Grid grid)
                        {
                            foreach (var element in grid.Children)
                            {
                                if (element is Border border && border.Child is Canvas c)
                                {
                                    foreach (var line in c.Children.OfType<Line>())
                                    {
                                        line.X1 = playheadX;
                                        line.X2 = playheadX;
                                    }
                                }
                            }
                        }
                    }
                }
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
                
                _audio.Play();
                isPlaying = true;
                BtnPlay.Content = "⏸";
                playTimer?.Start();
                StatusText.Text = "Воспроизведение...";
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (isPlaying)
            {
                _audio.Pause();
                isPlaying = false;
                BtnPlay.Content = "▶";
                playTimer.Stop();
                StatusText.Text = "Пауза";
            }
            else
            {
                if (currentTime >= GetTotalDuration())
                    currentTime = 0;
                
                _audio.Play();
                isPlaying = true;
                BtnPlay.Content = "⏸";
                playTimer.Start();
                StatusText.Text = "Воспроизведение...";
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _audio.Stop();
            isPlaying = false;
            BtnPlay.Content = "▶";
            playTimer.Stop();
            currentTime = 0;
            DrawTimeline();
            StatusText.Text = "Остановлено";
            CurrentTimeText.Text = "00:00";
            TotalTimeText.Text = FormatTime(GetTotalDuration());
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            if (!HasSelection() || focusedClipIndex < 0) return;
            
            SaveUndoState();
            
            var track = tracks[focusedClipIndex];
            double start = Math.Min(selectionStart, selectionEnd);
            double end = Math.Max(selectionStart, selectionEnd);
            
            int startSample = (int)(start * track.SampleRate * track.Channels);
            int endSample = (int)(end * track.SampleRate * track.Channels);
            startSample = Math.Max(0, startSample);
            endSample = Math.Min(track.Samples.Length, endSample);
            
            int length = endSample - startSample;
            clipboard = new float[length];
            Array.Copy(track.Samples, startSample, clipboard, 0, length);
            clipboardChannels = track.Channels;
            clipboardSampleRate = track.SampleRate;
            
            var newSamples = new float[track.Samples.Length - length];
            Array.Copy(track.Samples, 0, newSamples, 0, startSample);
            Array.Copy(track.Samples, endSample, newSamples, startSample, track.Samples.Length - endSample);
            track.Samples = newSamples;
            
            ClearSelection();
            RebuildMixer();
            DrawTimeline();
            UpdateTrackLabels();
            
            StatusText.Text = $"Вырезано: {FormatTime((double)length / (clipboardSampleRate * clipboardChannels))}";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!HasSelection() || focusedClipIndex < 0) return;
            
            var track = tracks[focusedClipIndex];
            double start = Math.Min(selectionStart, selectionEnd);
            double end = Math.Max(selectionStart, selectionEnd);
            
            int startSample = (int)(start * track.SampleRate * track.Channels);
            int endSample = (int)(end * track.SampleRate * track.Channels);
            startSample = Math.Max(0, startSample);
            endSample = Math.Min(track.Samples.Length, endSample);
            
            int length = endSample - startSample;
            clipboard = new float[length];
            Array.Copy(track.Samples, startSample, clipboard, 0, length);
            clipboardChannels = track.Channels;
            clipboardSampleRate = track.SampleRate;
            
            StatusText.Text = $"Скопировано: {FormatTime((double)length / (clipboardSampleRate * clipboardChannels))}";
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            if (clipboard == null || clipboard.Length == 0) return;
            if (selectedTrackIndex < 0) selectedTrackIndex = 0;
            
            SaveUndoState();
            
            var track = tracks[selectedTrackIndex];
            double pasteTime = selectionStart >= 0 ? Math.Min(selectionStart, selectionEnd) : 0;
            int pasteSample = (int)(pasteTime * track.SampleRate * track.Channels);
            pasteSample = Math.Max(0, Math.Min(pasteSample, track.Samples.Length));
            
            var newSamples = new float[track.Samples.Length + clipboard.Length];
            Array.Copy(track.Samples, 0, newSamples, 0, pasteSample);
            Array.Copy(clipboard, 0, newSamples, pasteSample, clipboard.Length);
            Array.Copy(track.Samples, pasteSample, newSamples, pasteSample + clipboard.Length, track.Samples.Length - pasteSample);
            
            track.Samples = newSamples;
            
            RebuildMixer();
            DrawTimeline();
            UpdateTrackLabels();
            
            StatusText.Text = $"Вставлено: {FormatTime((double)clipboard.Length / (clipboardSampleRate * clipboardChannels))}";
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!HasSelection() || focusedClipIndex < 0) return;
            
            SaveUndoState();
            
            var track = tracks[focusedClipIndex];
            double start = Math.Min(selectionStart, selectionEnd);
            double end = Math.Max(selectionStart, selectionEnd);
            
            int startSample = (int)(start * track.SampleRate * track.Channels);
            int endSample = (int)(end * track.SampleRate * track.Channels);
            startSample = Math.Max(0, startSample);
            endSample = Math.Min(track.Samples.Length, endSample);
            
            var newSamples = new float[track.Samples.Length - (endSample - startSample)];
            Array.Copy(track.Samples, 0, newSamples, 0, startSample);
            Array.Copy(track.Samples, endSample, newSamples, startSample, track.Samples.Length - endSample);
            track.Samples = newSamples;
            
            ClearSelection();
            RebuildMixer();
            DrawTimeline();
            UpdateTrackLabels();
            
            StatusText.Text = "Удалено";
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count == 0) return;
            
            redoStack.Push(CloneTracks());
            tracks = undoStack.Pop();
            
            RebuildMixer();
            DrawTimeline();
            UpdateTrackLabels();
            EnableControls(true);
            StatusText.Text = "Отменено";
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (redoStack.Count == 0) return;
            
            undoStack.Push(CloneTracks());
            tracks = redoStack.Pop();
            
            RebuildMixer();
            DrawTimeline();
            UpdateTrackLabels();
            EnableControls(true);
            StatusText.Text = "Повторено";
        }

        private List<AudioClip> CloneTracks()
        {
            return tracks.Select(t => new AudioClip
            {
                Samples = (float[])t.Samples.Clone(),
                SampleRate = t.SampleRate,
                Channels = t.Channels,
                StartTime = t.StartTime,
                SourceFile = t.SourceFile,
                TrackIndex = t.TrackIndex,
                Name = t.Name
            }).ToList();
        }

        private void SaveUndoState()
        {
            undoStack.Push(CloneTracks());
            redoStack.Clear();
        }

        private void AddTrack_Click(object sender, RoutedEventArgs e)
        {
            if (tracks.Count >= MaxTracks)
            {
                StatusText.Text = $"Достигнут лимит треков ({MaxTracks})";
                return;
            }
            
            SaveUndoState();
            tracks.Add(CreateEmptyTrack(tracks.Count));
            DrawTimeline();
            UpdateTrackLabels();
            StatusText.Text = $"Добавлена дорожка {tracks.Count}";
        }

        private void RemoveTrack_Click(object sender, RoutedEventArgs e)
        {
            if (tracks.Count <= 1) return;
            
            SaveUndoState();
            
            // Очищаем память удаляемого трека
            var lastTrack = tracks[tracks.Count - 1];
            if (lastTrack.Samples != null)
            {
                lastTrack.Samples = Array.Empty<float>();
            }
            _waveformPeaks.Remove(tracks.Count - 1);
            
            tracks.RemoveAt(tracks.Count - 1);
            
            if (selectedTrackIndex >= tracks.Count)
                selectedTrackIndex = tracks.Count - 1;
            
            RebuildMixer();
            DrawTimeline();
            UpdateTrackLabels();
            
            // Принудительная сборка мусора
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            StatusText.Text = "Дорожка удалена";
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

        private void ZoomSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ZoomSlider != null && ZoomValue != null)
            {
                pixelsPerSecond = ZoomSlider.Value;
                ZoomValue.Text = $"{pixelsPerSecond:F0}%";
                
                // Очищаем кэш waveform для перерисовки с новым масштабом
                _waveformPeaks.Clear();
                
                DrawTimeline();
                
                // Обновляем позиции playhead и конца трека после зума
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
                
                var instrumentsWindow = new InstrumentsWindow();
                if (this != null)
                    instrumentsWindow.Owner = this;
                instrumentsWindow.LoadTrack(track);
                instrumentsWindow.ShowDialog();
                
                if (instrumentsWindow.ChangesApplied)
                {
                    ApplyInstrumentsChanges(track, instrumentsWindow);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message + "\n" + ex.StackTrace, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ApplyInstrumentsChanges(AudioClip track, InstrumentsWindow window)
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
        
        // ========== Drag & Drop Support ==========
        private int _dragHoveredTrackIndex = -1;
        private Line? _dropIndicatorLine = null;
        private int _trackIndexBeforeDrag = -1; // Запоминаем выбор ДО drag
        private bool _isDraggingFile = false; // Флаг что идёт drag-drop
        
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
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
            StatusText.Text = "Готов к работе";
        }
        
        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                
                var point = e.GetPosition(TracksContainer);
                int hoveredTrack = (int)(point.Y / (TrackHeight + TrackMargin));
                
                // Ограничиваем диапазон
                if (hoveredTrack < 0) hoveredTrack = 0;
                if (hoveredTrack >= tracks.Count) hoveredTrack = tracks.Count - 1;
                
                if (hoveredTrack != _dragHoveredTrackIndex)
                {
                    _dragHoveredTrackIndex = hoveredTrack;
                    UpdateDropIndicator();
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
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
            
            // ВИЗУАЛЬНО показываем выбранный трек во время drag (подсветка)
            selectedTrackIndex = _dragHoveredTrackIndex;
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
        
        private void TracksBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(TracksContainer);
            int clickedTrack = (int)(point.Y / (TrackHeight + TrackMargin));
            if (clickedTrack >= 0 && clickedTrack < tracks.Count)
            {
                selectedTrackIndex = clickedTrack;
                UpdateTrackLabels();
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
        }
        
        private void OnDrop(object sender, DragEventArgs e)
        {
            ClearDropIndicators();
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                int targetTrack = _dragHoveredTrackIndex >= 0 ? _dragHoveredTrackIndex : (_trackIndexBeforeDrag >= 0 ? _trackIndexBeforeDrag : 0);
                if (targetTrack < 0) targetTrack = 0;
                if (targetTrack >= tracks.Count) targetTrack = 0;
                
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (AudioExtensions.Contains(ext))
                        LoadFileToTrackOnTrack(file, targetTrack);
                }
                
                // ВОССТАНАВЛИВАЕМ выбор который был ДО drag
                selectedTrackIndex = _trackIndexBeforeDrag >= 0 ? _trackIndexBeforeDrag : 0;
                UpdateTrackLabels();
                
                StatusText.Text = $"Загружено в трек {targetTrack + 1}";
            }
            
            _isDraggingFile = false;
        }
        
        // ========== FL Studio Style Track Drag-Drop ==========
        // Drag-drop на КОНКРЕТНЫЙ трек (как в FL Studio)
        
        private void Track_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                
                if (sender is Border border && border.Tag is int trackIndex)
                {
                    // Визуальная подсветка трека (FL Studio стиль)
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(120, 129, 255)); // Фиолетовая рамка
                    border.BorderThickness = new Thickness(3);
                    
                    // Запоминаем что drag вошёл
                    _dragHoveredTrackIndex = trackIndex;
                    _trackIndexBeforeDrag = selectedTrackIndex;
                    
                    if (tracks[trackIndex].Samples.Length > 0)
                        StatusText.Text = $"Трек {trackIndex + 1}: заменит файл";
                    else
                        StatusText.Text = $"Трек {trackIndex + 1}: пустой";
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        
        private void Track_DragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
            
            if (sender is Border border)
            {
                // Убираем подсветку
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)); // Обычная рамка
                border.BorderThickness = new Thickness(0, 0, 1, 0);
            }
        }
        
        private void Track_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        
        private void Track_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            
            if (sender is Border border && border.Tag is int trackIndex)
            {
                // Убираем подсветку
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66));
                border.BorderThickness = new Thickness(0, 0, 1, 0);
                
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    
                    // Загружаем в ЭТОТ трек
                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLower();
                        if (AudioExtensions.Contains(ext))
                        {
                            LoadFileToTrackOnTrack(file, trackIndex);
                        }
                    }
                    
                    // ВОССТАНАВЛИВАЕМ выбор который был ДО drag
                    selectedTrackIndex = _trackIndexBeforeDrag >= 0 ? _trackIndexBeforeDrag : trackIndex;
                    UpdateTrackLabels();
                    
                    StatusText.Text = $"Загружено в трек {trackIndex + 1}";
                }
            }
            
            _isDraggingFile = false;
            _dragHoveredTrackIndex = -1;
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
