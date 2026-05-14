using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AudioStudio.Controls
{
    /// <summary>
    /// Временная линейка с отображением выделения
    /// Показывает метки времени и подсветку выделенного диапазона
    /// </summary>
    public class TimeRuler : Canvas
    {
        // Фоновые деления
        private readonly StackPanel _tickMarks;
        
        // Подсветка выделенного диапазона
        private readonly Rectangle _highlightRect;
        
        // Метки времени
        private readonly TextBlock _startTimeLabel;
        private readonly TextBlock _endTimeLabel;
        private readonly TextBlock _durationLabel;
        
        // Маркеры начала и конца
        private readonly Line _startMarker;
        private readonly Line _endMarker;
        
        // Текущие деления
        private readonly Stack<Line> _majorTicks = new();
        private readonly Stack<TextBlock> _tickLabels = new();
        
        // Привязка к pixelsPerSecond
        public double PixelsPerSecond { get; set; } = 50;
        
        // Смещение линейки вправо (ширина лейбла трека)
        public double Offset { get; set; }
        
        // Горизонтальный скролл (синхрон с waveform)
        public double ScrollOffset { get; set; }
        
        // Общая длительность всех треков (для продления делений)
        public double TotalDuration { get; set; }
        
        public double SelectionStart
        {
            get => (double)GetValue(SelectionStartProperty);
            set => SetValue(SelectionStartProperty, value);
        }
        
        public double SelectionEnd
        {
            get => (double)GetValue(SelectionEndProperty);
            set => SetValue(SelectionEndProperty, value);
        }
        
        public static readonly DependencyProperty SelectionStartProperty =
            DependencyProperty.Register(nameof(SelectionStart), typeof(double), 
                typeof(TimeRuler), new PropertyMetadata(-1.0, OnSelectionChanged));
                
        public static readonly DependencyProperty SelectionEndProperty =
            DependencyProperty.Register(nameof(SelectionEnd), typeof(double), 
                typeof(TimeRuler), new PropertyMetadata(-1.0, OnSelectionChanged));
        
        private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimeRuler ruler)
            {
                ruler.UpdateSelectionHighlight();
            }
        }
        
        public TimeRuler()
        {
            Height = 24;
            ClipToBounds = true;
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 40));
            
            // Подсветка выделенного диапазона на линейке
            _highlightRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, 120, 129, 255)),
                Height = 20,
                RadiusX = 2,
                RadiusY = 2,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_highlightRect);
            
            // Маркер начала
            _startMarker = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 2,
                Y1 = 0,
                Y2 = 24,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_startMarker);
            
            // Маркер конца
            _endMarker = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 2,
                Y1 = 0,
                Y2 = 24,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_endMarker);
            
            // Метка начала
            _startTimeLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 255)),
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_startTimeLabel);
            
            // Метка конца
            _endTimeLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 255)),
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_endTimeLabel);
            
            // Метка длительности
            _durationLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_durationLabel);
            
            // Панель для делений
            _tickMarks = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            
            // Подписываемся на изменение размера
            SizeChanged += (s, e) => UpdateTicks();
        }
        
        /// <summary>
        /// Обновить деления на линейке
        /// </summary>
        public void UpdateTicks()
        {
            // Очищаем старые деления
            foreach (var tick in _majorTicks)
            {
                Children.Remove(tick);
            }
            _majorTicks.Clear();
            
            foreach (var label in _tickLabels)
            {
                Children.Remove(label);
            }
            _tickLabels.Clear();
            
            if (ActualWidth <= 0 || PixelsPerSecond <= 0) return;
            
            // Определяем интервал делений в зависимости от масштаба
            double interval;
            if (PixelsPerSecond >= 200)
                interval = 0.1; // 100ms
            else if (PixelsPerSecond >= 100)
                interval = 0.5; // 500ms
            else if (PixelsPerSecond >= 50)
                interval = 1; // 1 second
            else if (PixelsPerSecond >= 20)
                interval = 5; // 5 seconds
            else
                interval = 10; // 10 seconds
            
            double totalTime = Math.Max(TotalDuration, ActualWidth / PixelsPerSecond);
            
            for (double t = 0; t <= totalTime; t += interval)
            {
                double x = t * PixelsPerSecond + Offset - ScrollOffset;
                
                // Большое деление
                var tick = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = Height - 8,
                    Y2 = Height,
                    Stroke = new SolidColorBrush(Color.FromArgb(100, 150, 150, 150)),
                    StrokeThickness = 1
                };
                Children.Add(tick);
                _majorTicks.Push(tick);
                
                // Метка времени
                if (t >= 0)
                {
                    var label = new TextBlock
                    {
                        Text = FormatTimeShort(t),
                        Foreground = new SolidColorBrush(Color.FromArgb(150, 150, 150, 150)),
                        FontSize = 8,
                        FontFamily = new FontFamily("Consolas")
                    };
                    Canvas.SetLeft(label, x + 2);
                    Canvas.SetTop(label, 4);
                    Children.Add(label);
                    _tickLabels.Push(label);
                }
            }
        }
        
        /// <summary>
        /// Обновить подсветку выделения
        /// </summary>
        public void UpdateSelectionHighlight()
        {
            if (SelectionStart < 0 || SelectionEnd < 0 || SelectionEnd <= SelectionStart)
            {
                _highlightRect.Visibility = Visibility.Collapsed;
                _startMarker.Visibility = Visibility.Collapsed;
                _endMarker.Visibility = Visibility.Collapsed;
                _startTimeLabel.Visibility = Visibility.Collapsed;
                _endTimeLabel.Visibility = Visibility.Collapsed;
                _durationLabel.Visibility = Visibility.Collapsed;
                return;
            }
            
            double startX = SelectionStart * PixelsPerSecond + Offset - ScrollOffset;
            double endX = SelectionEnd * PixelsPerSecond + Offset - ScrollOffset;
            double width = endX - startX;
            
            if (width < 2) return;
            
            // Подсветка на линейке
            Canvas.SetLeft(_highlightRect, startX);
            _highlightRect.Width = width;
            _highlightRect.Visibility = Visibility.Visible;
            
            // Маркер начала
            _startMarker.X1 = startX;
            _startMarker.X2 = startX;
            _startMarker.Visibility = Visibility.Visible;
            
            // Маркер конца
            _endMarker.X1 = endX;
            _endMarker.X2 = endX;
            _endMarker.Visibility = Visibility.Visible;
            
            // Метка начала
            _startTimeLabel.Text = FormatTimeShort(SelectionStart);
            Canvas.SetLeft(_startTimeLabel, Math.Max(0, startX + 2));
            Canvas.SetTop(_startTimeLabel, 2);
            _startTimeLabel.Visibility = Visibility.Visible;
            
            // Метка конца
            _endTimeLabel.Text = FormatTimeShort(SelectionEnd);
            double endLabelX = endX - 40;
            Canvas.SetLeft(_endTimeLabel, Math.Max(0, endLabelX));
            Canvas.SetTop(_endTimeLabel, 2);
            _endTimeLabel.Visibility = Visibility.Visible;
            
            // Длительность посередине
            _durationLabel.Text = FormatTime(SelectionEnd - SelectionStart);
            double labelWidth = 50;
            Canvas.SetLeft(_durationLabel, startX + (width - labelWidth) / 2);
            Canvas.SetTop(_durationLabel, 14);
            _durationLabel.Visibility = Visibility.Visible;
        }
        
        private string FormatTimeShort(double seconds)
        {
            int min = (int)(seconds / 60);
            int sec = (int)(seconds % 60);
            if (min > 0)
                return $"{min}:{sec:D2}";
            return $"{sec}s";
        }
        
        private string FormatTime(double seconds)
        {
            int min = (int)(seconds / 60);
            int sec = (int)(seconds % 60);
            int ms = (int)((seconds % 1) * 100);
            return $"{min:D2}:{sec:D2}.{ms:D2}";
        }
    }
}
