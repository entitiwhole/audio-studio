using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AudioStudio.Controls
{
    /// <summary>
    /// Визуальный overlay для выделения на отдельном слое поверх треков.
    /// Не теряется при UpdateTrackLabels(). Ручки — визуальные, логика перетаскивания на waveform canvas.
    /// </summary>
    public class SelectionOverlaySimple
    {
        private readonly Canvas _parentCanvas;
        private readonly Rectangle _fillRect;
        private readonly Rectangle _borderRect;
        private readonly Line _leftLine;
        private readonly Line _rightLine;
        private readonly Polygon _leftHandle;
        private readonly Polygon _rightHandle;
        private readonly TextBlock _durationLabel;
        
        private bool _isVisible;
        private double _left;
        private double _right;
        
        public double Left
        {
            get => _left;
            set { _left = Math.Max(0, value); UpdatePositions(); }
        }
        
        public double Right
        {
            get => _right;
            set { _right = value; UpdatePositions(); }
        }
        
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                var v = value ? Visibility.Visible : Visibility.Collapsed;
                _fillRect.Visibility = v;
                _borderRect.Visibility = v;
                _leftLine.Visibility = v;
                _rightLine.Visibility = v;
                _leftHandle.Visibility = v;
                _rightHandle.Visibility = v;
                _durationLabel.Visibility = v;
            }
        }
        
        public SelectionOverlaySimple(Canvas parentCanvas)
        {
            _parentCanvas = parentCanvas;
            
            _fillRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(50, 120, 129, 255)),
                IsHitTestVisible = false
            };
            _parentCanvas.Children.Add(_fillRect);
            
            _borderRect = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            _parentCanvas.Children.Add(_borderRect);
            
            _leftLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 3,
                IsHitTestVisible = false
            };
            _parentCanvas.Children.Add(_leftLine);
            
            _rightLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 3,
                IsHitTestVisible = false
            };
            _parentCanvas.Children.Add(_rightLine);
            
            _leftHandle = new Polygon
            {
                Points = new PointCollection { new Point(0, -12), new Point(14, 0), new Point(0, 12) },
                Fill = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 255)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            _parentCanvas.Children.Add(_leftHandle);
            
            _rightHandle = new Polygon
            {
                Points = new PointCollection { new Point(14, -12), new Point(0, 0), new Point(14, 12) },
                Fill = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 255)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            _parentCanvas.Children.Add(_rightHandle);
            
            _durationLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 255)),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(180, 45, 45, 50)),
                Padding = new Thickness(4, 2, 4, 2),
                IsHitTestVisible = false
            };
            _parentCanvas.Children.Add(_durationLabel);
            
            IsVisible = false;
        }
        
        private void UpdatePositions()
        {
            if (_left < 0 || _right < 0) return;
            
            double width = _right - _left;
            double height = _parentCanvas.ActualHeight;
            if (height < 1) height = 400;
            
            if (width < 2)
            {
                IsVisible = false;
                return;
            }
            
            IsVisible = true;
            
            Canvas.SetLeft(_fillRect, _left);
            Canvas.SetTop(_fillRect, 0);
            _fillRect.Width = width;
            _fillRect.Height = height;
            
            Canvas.SetLeft(_borderRect, _left);
            Canvas.SetTop(_borderRect, 0);
            _borderRect.Width = width;
            _borderRect.Height = height;
            
            _leftLine.X1 = _left;
            _leftLine.X2 = _left;
            _leftLine.Y1 = 0;
            _leftLine.Y2 = height;
            
            _rightLine.X1 = _right;
            _rightLine.X2 = _right;
            _rightLine.Y1 = 0;
            _rightLine.Y2 = height;
            
            double midY = height / 2;
            Canvas.SetLeft(_leftHandle, _left);
            Canvas.SetTop(_leftHandle, midY);
            
            Canvas.SetLeft(_rightHandle, _right);
            Canvas.SetTop(_rightHandle, midY);
            
            double secs = (_right - _left);
            _durationLabel.Text = FormatDuration(secs);
            Canvas.SetLeft(_durationLabel, _left + (width - 60) / 2);
            Canvas.SetTop(_durationLabel, 4);
        }
        
        private static string FormatDuration(double seconds)
        {
            int min = (int)(seconds / 60);
            int sec = (int)(seconds % 60);
            int ms = (int)((seconds % 1) * 100);
            if (min > 0)
                return $"{min}:{sec:D2}.{ms:D2}";
            return $"{sec}.{ms:D2}s";
        }
        
        public void Remove()
        {
            _parentCanvas.Children.Remove(_fillRect);
            _parentCanvas.Children.Remove(_borderRect);
            _parentCanvas.Children.Remove(_leftLine);
            _parentCanvas.Children.Remove(_rightLine);
            _parentCanvas.Children.Remove(_leftHandle);
            _parentCanvas.Children.Remove(_rightHandle);
            _parentCanvas.Children.Remove(_durationLabel);
        }
    }
}
