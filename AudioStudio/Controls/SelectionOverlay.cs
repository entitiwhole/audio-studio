using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AudioStudio.Controls
{
    /// <summary>
    /// Визуальный контрол для выделения области на waveform
    /// FL Studio style: полупрозрачная заливка + ручки перетаскивания
    /// </summary>
    public class SelectionOverlay : Canvas
    {
        // Основные элементы
        private readonly Rectangle _selectionFill;
        private readonly Rectangle _selectionBorder;
        
        // Ручки перетаскивания (Handles)
        private readonly SelectionHandle _leftHandle;
        private readonly SelectionHandle _rightHandle;
        
        // Линии границ выделения
        private readonly Line _leftBoundary;
        private readonly Line _rightBoundary;
        
        // Перетаскивание всей области
        private bool _isDraggingRegion;
        private Point _dragStartPoint;
        private double _dragStartLeft;
        private double _dragStartRight;
        
        // Публичные свойства
        public double SelectionLeft
        {
            get => (double)GetValue(SelectionLeftProperty);
            set => SetValue(SelectionLeftProperty, value);
        }
        
        public double SelectionRight
        {
            get => (double)GetValue(SelectionRightProperty);
            set => SetValue(SelectionRightProperty, value);
        }
        
        public static readonly DependencyProperty SelectionLeftProperty =
            DependencyProperty.Register(nameof(SelectionLeft), typeof(double), 
                typeof(SelectionOverlay), new FrameworkPropertyMetadata(0.0, OnSelectionChanged));
                
        public static readonly DependencyProperty SelectionRightProperty =
            DependencyProperty.Register(nameof(SelectionRight), typeof(double), 
                typeof(SelectionOverlay), new FrameworkPropertyMetadata(0.0, OnSelectionChanged));
        
        public new bool IsVisible
        {
            get => _selectionFill.Visibility == Visibility.Visible;
            set
            {
                var visibility = value ? Visibility.Visible : Visibility.Collapsed;
                _selectionFill.Visibility = visibility;
                _selectionBorder.Visibility = visibility;
                _leftHandle.Visibility = visibility;
                _rightHandle.Visibility = visibility;
                _leftBoundary.Visibility = visibility;
                _rightBoundary.Visibility = visibility;
            }
        }
        
        public SelectionOverlay()
        {
            // Полупрозрачная заливка выделенной области
            _selectionFill = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(50, 120, 129, 255)),
                IsHitTestVisible = true,
                Cursor = Cursors.SizeAll
            };
            
            // Рамка выделения
            _selectionBorder = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            
            // Левая граница (жирная линия)
            _leftBoundary = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 2,
                IsHitTestVisible = false,
                Y1 = 0
            };
            
            // Правая граница (жирная линия)
            _rightBoundary = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 2,
                IsHitTestVisible = false,
                Y1 = 0
            };
            
            // Левая ручка (треугольник)
            _leftHandle = new SelectionHandle
            {
                HandleType = HandleType.Left,
                Width = 12,
                Height = 20,
                Cursor = Cursors.SizeWE
            };
            
            // Правая ручка (треугольник)
            _rightHandle = new SelectionHandle
            {
                HandleType = HandleType.Right,
                Width = 12,
                Height = 20,
                Cursor = Cursors.SizeWE
            };
            
            // Добавляем элементы на canvas
            Children.Add(_selectionFill);
            Children.Add(_selectionBorder);
            Children.Add(_leftBoundary);
            Children.Add(_rightBoundary);
            Children.Add(_leftHandle);
            Children.Add(_rightHandle);
            
            // События перетаскивания
            _selectionFill.MouseLeftButtonDown += OnRegionMouseDown;
            _selectionFill.MouseMove += OnRegionMouseMove;
            _selectionFill.MouseLeftButtonUp += OnRegionMouseUp;
            
            _leftHandle.MouseLeftButtonDown += OnLeftHandleMouseDown;
            _leftHandle.MouseMove += OnLeftHandleMouseMove;
            _leftHandle.MouseLeftButtonUp += OnHandleMouseUp;
            
            _rightHandle.MouseLeftButtonDown += OnRightHandleMouseDown;
            _rightHandle.MouseMove += OnRightHandleMouseMove;
            _rightHandle.MouseLeftButtonUp += OnHandleMouseUp;
            
            // Начальное состояние
            IsVisible = false;
        }
        
        private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SelectionOverlay overlay)
            {
                overlay.UpdateLayout();
            }
        }
        
        public new void UpdateLayout()
        {
            double left = SelectionLeft;
            double right = SelectionRight;
            double width = Math.Max(0, right - left);
            double height = ActualHeight > 0 ? ActualHeight : 100;
            
            // Обновляем заливку
            if (width > 0)
            {
                Canvas.SetLeft(_selectionFill, left);
                _selectionFill.Width = width;
                _selectionFill.Height = height;
            }
            
            // Обновляем рамку
            Canvas.SetLeft(_selectionBorder, left);
            _selectionBorder.Width = width;
            _selectionBorder.Height = height;
            
            // Обновляем границы
            _leftBoundary.X1 = left;
            _leftBoundary.X2 = left;
            _leftBoundary.Y2 = height;
            
            _rightBoundary.X1 = right;
            _rightBoundary.X2 = right;
            _rightBoundary.Y2 = height;
            
            // Обновляем ручки (центрируем по вертикали)
            Canvas.SetLeft(_leftHandle, left - 6);
            Canvas.SetTop(_leftHandle, (height - 20) / 2);
            
            Canvas.SetLeft(_rightHandle, right - 6);
            Canvas.SetTop(_rightHandle, (height - 20) / 2);
            
            // Показываем/скрываем
            IsVisible = width > 0;
        }
        
        // Перетаскивание всей области выделения
        private void OnRegionMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingRegion = true;
            _dragStartPoint = e.GetPosition(this);
            _dragStartLeft = SelectionLeft;
            _dragStartRight = SelectionRight;
            _selectionFill.CaptureMouse();
            e.Handled = true;
        }
        
        private void OnRegionMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingRegion)
            {
                var pos = e.GetPosition(this);
                double delta = pos.X - _dragStartPoint.X;
                
                SelectionLeft = _dragStartLeft + delta;
                SelectionRight = _dragStartRight + delta;
                
                UpdateLayout();
                
                // Вызываем событие для обновления времени
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        private void OnRegionMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingRegion = false;
            _selectionFill.ReleaseMouseCapture();
        }
        
        // Перетаскивание левой ручки
        private bool _isDraggingLeftHandle;
        private Point _handleDragStart;
        private double _handleStartLeft;
        
        public event EventHandler? SelectionChanged;
        
        private void OnLeftHandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingLeftHandle = true;
            _handleDragStart = e.GetPosition(this);
            _handleStartLeft = SelectionLeft;
            _leftHandle.CaptureMouse();
            e.Handled = true;
        }
        
        private void OnLeftHandleMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingLeftHandle)
            {
                var pos = e.GetPosition(this);
                double delta = pos.X - _handleDragStart.X;
                SelectionLeft = Math.Max(0, _handleStartLeft + delta);
                UpdateLayout();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        // Перетаскивание правой ручки
        private bool _isDraggingRightHandle;
        private double _handleStartRight;
        
        private void OnRightHandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingRightHandle = true;
            _handleDragStart = e.GetPosition(this);
            _handleStartRight = SelectionRight;
            _rightHandle.CaptureMouse();
            e.Handled = true;
        }
        
        private void OnRightHandleMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingRightHandle)
            {
                var pos = e.GetPosition(this);
                double delta = pos.X - _handleDragStart.X;
                SelectionRight = _handleStartRight + delta;
                UpdateLayout();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        private void OnHandleMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingLeftHandle = false;
            _isDraggingRightHandle = false;
            if (sender is FrameworkElement element)
                element.ReleaseMouseCapture();
        }
    }
    
    /// <summary>
    /// Ручка выделения (треугольная форма как в FL Studio)
    /// </summary>
    public class SelectionHandle : FrameworkElement
    {
        public HandleType HandleType { get; set; }
        
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            
            double w = ActualWidth;
            double h = ActualHeight;
            
            if (w <= 0 || h <= 0) return;
            
            // Создаём геометрию треугольника
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                if (HandleType == HandleType.Left)
                {
                    // Треугольник влево
                    ctx.BeginFigure(new Point(w, 0), true, true);
                    ctx.LineTo(new Point(0, h / 2), true, true);
                    ctx.LineTo(new Point(w, h), true, true);
                }
                else
                {
                    // Треугольник вправо
                    ctx.BeginFigure(new Point(0, 0), true, true);
                    ctx.LineTo(new Point(w, h / 2), true, true);
                    ctx.LineTo(new Point(0, h), true, true);
                }
            }
            
            // Рисуем треугольник
            drawingContext.DrawGeometry(
                new SolidColorBrush(Color.FromRgb(120, 129, 255)), 
                new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 255)), 1),
                geometry);
        }
    }
    
    public enum HandleType
    {
        Left,
        Right
    }
}
