using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Attendance_System.Models;

namespace Attendance_System.Helpers
{
    /// <summary>Visible when the bound bool is false — used for DataGrid empty states.</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Visible only when the bound value is null — used for ComboBox placeholders.</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Status badge colors for an AttendanceRecord. A late TIME-IN reads as a
    /// warning, an on-time TIME-IN as success, a TIME-OUT as neutral. The badge
    /// always carries a text label too — color is never the only signal.
    /// </summary>
    public class StatusBadgeBrushConverter : IValueConverter
    {
        public bool Foreground { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var (fill, text) = value switch
            {
                AttendanceRecord { IsLate: true } => ("#FDEFD8", "#8A5206"),
                AttendanceRecord { Status: "Time In" } => ("#EDF7D2", "#3D5A12"),
                AttendanceRecord { Status: "Time Out" } => ("#EDEAE4", "#5A5F5E"),
                _ => ("#EDEAE4", "#5A5F5E"),
            };

            var hex = Foreground ? text : fill;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
