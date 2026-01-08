using System;
using System.Windows;
using System.Windows.Input;

namespace GOON.Windows {
    public partial class InputDialogWindow : Window {
        public string DialogTitle {
            get => (string)GetValue(DialogTitleProperty);
            set => SetValue(DialogTitleProperty, value);
        }

        public static readonly DependencyProperty DialogTitleProperty =
            DependencyProperty.Register(nameof(DialogTitle), typeof(string), typeof(InputDialogWindow), new PropertyMetadata("Input"));

        public string LabelText {
            get => (string)GetValue(LabelTextProperty);
            set => SetValue(LabelTextProperty, value);
        }

        public static readonly DependencyProperty LabelTextProperty =
            DependencyProperty.Register(nameof(LabelText), typeof(string), typeof(InputDialogWindow), new PropertyMetadata("Enter value:"));

        public string InputText {
            get => (string)GetValue(InputTextProperty);
            set => SetValue(InputTextProperty, value);
        }

        public static readonly DependencyProperty InputTextProperty =
            DependencyProperty.Register(nameof(InputText), typeof(string), typeof(InputDialogWindow), new PropertyMetadata(""));

        public InputDialogWindow() {
            InitializeComponent();
            DataContext = this;
            Loaded += InputDialogWindow_Loaded;
        }

        private void InputDialogWindow_Loaded(object sender, RoutedEventArgs e) {
            // Focus the TextBox and select all text if there's an initial value
            InputTextBox.Focus();
            // Ensure text is visible by setting foreground explicitly
            InputTextBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            if (!string.IsNullOrEmpty(InputText)) {
                InputTextBox.SelectAll();
            }
            // Initialize the clip geometry
            UpdateMainBorderClip();
        }

        private void MainBorder_SizeChanged(object sender, SizeChangedEventArgs e) {
            UpdateMainBorderClip();
        }

        private void UpdateMainBorderClip() {
            if (MainBorderClip != null && MainBorder != null) {
                MainBorderClip.Rect = new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight);
            }
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            // Mark event as handled to prevent event bubbling issues
            e.Handled = true;
            
            // Call DragMove immediately while the button is definitely pressed
            if (e.ButtonState == MouseButtonState.Pressed) {
                try {
                    this.DragMove();
                } catch (InvalidOperationException) {
                    // Silently handle the case where DragMove fails
                    // This can happen in rare timing scenarios
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) {
                OkButton_Click(sender, e);
                e.Handled = true;
            } else if (e.Key == Key.Escape) {
                CancelButton_Click(sender, e);
                e.Handled = true;
            }
        }

        public static string ShowDialog(Window owner, string title, string labelText, string initialValue = "") {
            var dialog = new InputDialogWindow {
                DialogTitle = title,
                LabelText = labelText,
                InputText = initialValue
            };

            // Set owner window
            if (owner != null) {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            } else {
                // Fallback to MainWindow if available
                if (Application.Current?.MainWindow != null) {
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                } else {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            // Show dialog and return result
            bool? result = dialog.ShowDialog();
            if (result == true) {
                return dialog.InputText?.Trim() ?? string.Empty;
            }
            return null;
        }
    }
}

