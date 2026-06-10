using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioStudio
{
    public class SliderToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4) return 0.0;
            if (values[0] is not double value || values[1] is not double min
                || values[2] is not double max || values[3] is not double width)
                return 0.0;
            if (max <= min || width <= 0) return 0.0;
            double t = (value - min) / (max - min);
            return Math.Max(0, Math.Min(width, width * t));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
