using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AudioStudio
{
    /// <summary>
    /// FL Studio-style selection manager.
    /// Left-click = selection, Right-click = context menu
    /// </summary>
    public class SelectionManager
    {
        private readonly MainWindow _mainWindow;
        
        // Selection state
        private Rectangle? _selectionRect;
        private Point _selectionStart;
        private bool _isSelecting;
        
        public double SelectionStart { get; internal set; } = -1;
        public double SelectionEnd { get; internal set; } = -1;
        
        /// <summary>
        /// Check if there's an active selection (minimum 50ms)
        /// </summary>
        public bool HasSelection => SelectionStart >= 0 && SelectionEnd >= 0 && 
                                     Math.Abs(SelectionEnd - SelectionStart) > 0.05;

        public SelectionManager(MainWindow window)
        {
            _mainWindow = window;
        }
        
        /// <summary>
        /// Update selection while dragging
        /// </summary>
        public void UpdateSelection(Point currentPoint)
        {
            if (!_isSelecting) return;
            
            double left = Math.Min(_selectionStart.X, currentPoint.X);
            double width = Math.Abs(currentPoint.X - _selectionStart.X);
            
            // Создаём rectangle если его ещё нет
            if (_selectionRect == null && _canvas != null)
            {
                CreateSelectionRectangle();
            }
            
            if (_selectionRect != null)
            {
                Canvas.SetLeft(_selectionRect, left);
                _selectionRect.Width = width;
            }
        }
        
        private Canvas? _canvas;
        
        /// <summary>
        /// Create selection rectangle on the canvas
        /// </summary>
        private void CreateSelectionRectangle()
        {
            if (_canvas == null) return;
            
            _selectionRect = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 120, 129, 255)),
                IsHitTestVisible = false,
                Tag = "selectionRect"
            };
            
            Canvas.SetTop(_selectionRect, 0);
            _selectionRect.Height = _canvas.ActualHeight;
            
            // Добавляем в конец списка, чтобы был поверх waveform
            _canvas.Children.Add(_selectionRect);
        }
        
        /// <summary>
        /// Start a new selection at the given point
        /// </summary>
        public void StartSelection(Point startPoint, Canvas waveformCanvas)
        {
            _isSelecting = true;
            _selectionStart = startPoint;
            _canvas = waveformCanvas;
            
            // Clear any existing selection
            if (_selectionRect != null)
            {
                var parent = _selectionRect.Parent as Panel;
                parent?.Children.Remove(_selectionRect);
                _selectionRect = null;
            }
            
            // Create selection rectangle immediately
            CreateSelectionRectangle();
            
            // Set initial size
            if (_selectionRect != null)
            {
                Canvas.SetLeft(_selectionRect, startPoint.X);
                _selectionRect.Width = 0;
            }
        }

        /// <summary>
        /// End selection at the given point
        /// </summary>
        public void EndSelection(Point endPoint, int trackIndex)
        {
            if (!_isSelecting) return;
            _isSelecting = false;
            
            // Convert pixels to time
            double pixelsPerSecond = _mainWindow.PixelsPerSecond;
            double startTime = Math.Min(_selectionStart.X, endPoint.X) / pixelsPerSecond;
            double endTime = Math.Max(_selectionStart.X, endPoint.X) / pixelsPerSecond;
            
            // Minimum selection duration - 50ms
            if (Math.Abs(endTime - startTime) < 0.05)
            {
                ClearSelection();
                return;
            }
            
            SelectionStart = startTime;
            SelectionEnd = endTime;
            
            _mainWindow.FocusedClipIndex = trackIndex;
            _mainWindow.UpdateSelectionDisplay();
            _mainWindow.EnableControls(true);
        }

        /// <summary>
        /// Clear current selection
        /// </summary>
        public void ClearSelection()
        {
            SelectionStart = -1;
            SelectionEnd = -1;
            
            // Удаляем rectangle выделения
            if (_selectionRect != null)
            {
                var parent = _selectionRect.Parent as Panel;
                parent?.Children.Remove(_selectionRect);
                _selectionRect = null;
            }
            
            _canvas = null;
            
            _mainWindow.ClearSelectionUI();
            _mainWindow.UpdateSelectionDisplay();
        }

        /// <summary>
        /// Get sample range for the current selection
        /// </summary>
        public (int startSample, int endSample)? GetSampleRange(AudioClip clip)
        {
            if (!HasSelection) return null;
            
            double start = Math.Min(SelectionStart, SelectionEnd);
            double end = Math.Max(SelectionStart, SelectionEnd);
            
            int startSample = Math.Max(0, (int)(start * clip.SampleRate * clip.Channels));
            int endSample = Math.Min(clip.Samples.Length, (int)(end * clip.SampleRate * clip.Channels));
            
            if (endSample <= startSample) return null;
            
            return (startSample, endSample);
        }

        /// <summary>
        /// Check if we are currently dragging to create a selection
        /// </summary>
        public bool IsSelecting => _isSelecting;
    }
}
