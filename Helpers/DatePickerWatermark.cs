using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Attendance_System.Helpers
{
    /// <summary>
    /// Replaces a DatePicker's built-in "Select a date" placeholder, e.g. with "From" / "To"
    /// for compact toolbars that have no field labels. Usage: DatePickerWatermark.Text="From".
    /// </summary>
    public static class DatePickerWatermark
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(DatePickerWatermark), new PropertyMetadata(null, OnTextChanged));

        public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
        public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DatePicker picker) return;
            picker.Loaded -= Apply;
            picker.Loaded += Apply;
            if (picker.IsLoaded) Apply(picker, new RoutedEventArgs());
        }

        private static void Apply(object sender, RoutedEventArgs e)
        {
            var picker = (DatePicker)sender;
            picker.ApplyTemplate();
            if (picker.Template.FindName("PART_TextBox", picker) is DatePickerTextBox box)
            {
                box.ApplyTemplate();
                if (box.Template.FindName("PART_Watermark", box) is ContentControl watermark)
                    watermark.Content = GetText(picker);
            }
        }
    }
}
