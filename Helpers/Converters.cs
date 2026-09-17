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

    /// <summary>"Stephen Paul Alagao" → "SA"; an email falls back to its first letter.</summary>
    public class InitialsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parts = (value as string ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            var first = char.ToUpperInvariant(parts[0][0]);
            return parts.Length == 1 ? first.ToString() : $"{first}{char.ToUpperInvariant(parts[^1][0])}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Stable avatar fill per name, from the palette's darker tones so white
    /// initials always pass contrast. The name is always shown beside it.
    /// </summary>
    public class AvatarBrushConverter : IValueConverter
    {
        private static readonly Brush[] Fills =
            new[] { "#BC2D29", "#432E6F", "#9A2320", "#6B4A45", "#C2461F" }
            .Select(h => (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(h))).ToArray();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // string.GetHashCode is randomized per process; use a simple stable hash.
            int hash = 0;
            foreach (var c in value as string ?? "") hash = unchecked(hash * 31 + c);
            return Fills[(hash & int.MaxValue) % Fills.Length];
        }

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
                AttendanceRecord { IsLate: true } => ("#FFF0D6", "#8A5206"),
                AttendanceRecord { Status: "Time In" } => ("#EDF7D2", "#3D5A12"),
                AttendanceRecord { Status: "Time Out" } => ("#ECE8F3", "#432E6F"),
                _ => ("#ECE8F3", "#432E6F"),
            };

            var hex = Foreground ? text : fill;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
