using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Attendance_System.Helpers
{
    /// <summary>
    /// Clicking empty space (cards, labels, background) in any window takes
    /// focus away from the text box being edited, so its caret and focus ring go away.
    /// </summary>
    public static class ClickAwayFocus
    {
        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnPreviewMouseDown));
        }

        private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Window window) return;
            if (Keyboard.FocusedElement is not (TextBoxBase or PasswordBox)) return;
            if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject)) return;

            // The window remembers its last focused element and hands keyboard focus
            // straight back to it, so forget that element before moving focus away.
            var scope = FocusManager.GetFocusScope((DependencyObject)Keyboard.FocusedElement);
            FocusManager.SetFocusedElement(scope, null);
            Keyboard.ClearFocus();
            Keyboard.Focus(window);
        }

        // Clicks on other inputs or buttons move focus on their own — leave those alone.
        private static bool IsInsideInteractiveControl(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is TextBoxBase or PasswordBox or ButtonBase or DatePicker
                    or ScrollBar or Selector)
                    return true;
                if (node is Window) return false;

                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);   // e.g. a Run inside a TextBlock
            }
            return false;
        }
    }
}
