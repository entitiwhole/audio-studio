using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AudioStudio.Models;

namespace AudioStudio.Controls
{
    public class TimeRuler : Canvas
    {
        private readonly Canvas _ticksContainer;
        private readonly Rectangle _highlightRect;
        private readonly TextBlock _startTimeLabel;
        private readonly TextBlock _endTimeLabel;
        private readonly TextBlock _durationLabel;
        private readonly Line _startMarker;
        private readonly Line _endMarker;

        public double PixelsPerSecond { get; set; } = 50;
        public double Offset { get; set; }
        public double ScrollOffset { get; set; }
        public double TotalDuration { get; set; }
        public int Bpm { get; set; } = 128;

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
                ruler.UpdateSelectionHighlight();
        }

        public TimeRuler()
        {
            Height = 24;
            ClipToBounds = true;
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 40));

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

            _startMarker = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 2,
                Y1 = 0,
                Y2 = 24,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_startMarker);

            _endMarker = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 255)),
                StrokeThickness = 2,
                Y1 = 0,
                Y2 = 24,
                Visibility = Visibility.Collapsed
            };
            Children.Add(_endMarker);

            _startTimeLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 255)),
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_startTimeLabel);

            _endTimeLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 255)),
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_endTimeLabel);

            _durationLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(120, 129, 255)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Visibility = Visibility.Collapsed
            };
            Children.Add(_durationLabel);

            _ticksContainer = new Canvas { ClipToBounds = false };
            Children.Add(_ticksContainer);

            SizeChanged += (_, _) => UpdateTicks();
        }

        public void UpdateTicks()
        {
            _ticksContainer.Children.Clear();

            if (ActualWidth <= 0 || PixelsPerSecond <= 0) return;

            var grid = TimelineGrid.Compute(PixelsPerSecond, Bpm);
            double scrollTime = Math.Max(0, ScrollOffset / PixelsPerSecond);
            double visibleEnd = scrollTime + ActualWidth / PixelsPerSecond;

            int i0 = TimelineGrid.StartIndex(scrollTime, grid.MinorStepSeconds);
            int i1 = TimelineGrid.EndIndex(visibleEnd, grid.MinorStepSeconds);

            var majorBrush = new SolidColorBrush(Color.FromArgb(140, 200, 200, 200));
            var minorBrush = new SolidColorBrush(Color.FromArgb(70, 150, 150, 150));
            var labelBrush = new SolidColorBrush(Color.FromArgb(180, 170, 170, 170));

            const double labelWidthEstimate = 52;

            for (int i = i0; i <= i1; i++)
            {
                double t = i * grid.MinorStepSeconds;
                double x = t * PixelsPerSecond;

                bool isMajor = grid.MinorPerMajor > 0 && i % grid.MinorPerMajor == 0;
                bool isLabel = grid.MinorPerLabel > 0 && i % grid.MinorPerLabel == 0;

                var tick = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = Height - (isMajor ? 13 : 7),
                    Y2 = Height,
                    Stroke = isMajor ? majorBrush : minorBrush,
                    StrokeThickness = isMajor ? 1.5 : 0.5
                };
                _ticksContainer.Children.Add(tick);

                if (!isLabel) continue;

                double screenX = x - ScrollOffset + Offset;
                if (screenX < -4 || screenX > ActualWidth - labelWidthEstimate)
                    continue;

                var label = new TextBlock
                {
                    Text = TimelineGrid.FormatLabel(t, grid.FractionDigits),
                    Foreground = labelBrush,
                    FontSize = 9,
                    FontFamily = new FontFamily("Consolas")
                };
                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, 2);
                _ticksContainer.Children.Add(label);
            }

            Canvas.SetLeft(_ticksContainer, Offset - ScrollOffset);
        }

        public void RefreshScroll() => UpdateTicks();

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

            Canvas.SetLeft(_highlightRect, startX);
            _highlightRect.Width = width;
            _highlightRect.Visibility = Visibility.Visible;

            _startMarker.X1 = startX;
            _startMarker.X2 = startX;
            _startMarker.Visibility = Visibility.Visible;

            _endMarker.X1 = endX;
            _endMarker.X2 = endX;
            _endMarker.Visibility = Visibility.Visible;

            _startTimeLabel.Text = FormatTime(SelectionStart);
            Canvas.SetLeft(_startTimeLabel, Math.Max(0, startX + 2));
            Canvas.SetTop(_startTimeLabel, 2);
            _startTimeLabel.Visibility = Visibility.Visible;

            _endTimeLabel.Text = FormatTime(SelectionEnd);
            double endLabelX = endX - 40;
            Canvas.SetLeft(_endTimeLabel, Math.Max(0, endLabelX));
            Canvas.SetTop(_endTimeLabel, 2);
            _endTimeLabel.Visibility = Visibility.Visible;

            _durationLabel.Text = FormatTime(SelectionEnd - SelectionStart);
            const double labelWidth = 50;
            Canvas.SetLeft(_durationLabel, startX + (width - labelWidth) / 2);
            Canvas.SetTop(_durationLabel, 14);
            _durationLabel.Visibility = Visibility.Visible;
        }

        private static string FormatTime(double seconds)
        {
            int min = (int)(seconds / 60);
            int sec = (int)(seconds % 60);
            int ms = (int)((seconds % 1) * 100);
            return $"{min:D2}:{sec:D2}.{ms:D2}";
        }
    }
}
