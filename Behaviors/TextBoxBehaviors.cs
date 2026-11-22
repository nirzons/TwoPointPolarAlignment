using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace NirZonshine.NINA.TwoPointPolarAlignment.Behaviors {
    /// <summary>
    /// Attached behavior for TextBoxes to auto-select all text on initial focus (mouse click or tab)
    /// and commit values on pressing Enter.
    /// </summary>
    public static class TextBoxBehaviors {
        public static readonly DependencyProperty SelectAllOnFocusProperty =
            DependencyProperty.RegisterAttached(
                "SelectAllOnFocus",
                typeof(bool),
                typeof(TextBoxBehaviors),
                new UIPropertyMetadata(false, OnSelectAllOnFocusChanged));

        public static bool GetSelectAllOnFocus(DependencyObject obj) => (bool)obj.GetValue(SelectAllOnFocusProperty);
        public static void SetSelectAllOnFocus(DependencyObject obj, bool value) => obj.SetValue(SelectAllOnFocusProperty, value);

        private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is TextBox textBox) {
                if ((bool)e.NewValue) {
                    textBox.GotKeyboardFocus += OnGotKeyboardFocus;
                    textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
                    textBox.KeyDown += OnKeyDown;
                } else {
                    textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
                    textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
                    textBox.KeyDown -= OnKeyDown;
                }
            }
        }

        private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
            if (sender is TextBox tb) {
                tb.SelectAll();
            }
        }

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin) {
                e.Handled = true;
                tb.Focus();
            }
        }

        private static void OnKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && sender is TextBox tb) {
                BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();
                Keyboard.ClearFocus();
            }
        }
    }
}
