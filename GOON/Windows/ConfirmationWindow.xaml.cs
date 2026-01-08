using System;
using System.Windows;
using System.Windows.Input;

namespace GOON.Windows {
    public partial class ConfirmationWindow : Window {
        public string DialogTitle {
            get => (string)GetValue(DialogTitleProperty);
            set => SetValue(DialogTitleProperty, value);
        }

        public static readonly DependencyProperty DialogTitleProperty =
            DependencyProperty.Register(nameof(DialogTitle), typeof(string), typeof(ConfirmationWindow), new PropertyMetadata("Confirm"));

        public string Message {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(ConfirmationWindow), new PropertyMetadata("Are you sure?"));

        public ConfirmationWindow() {
            InitializeComponent();
            DataContext = this;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ButtonState == MouseButtonState.Pressed) {
                this.DragMove();
            }
        }

        private void YesButton_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }

        public static bool Show(Window owner, string title, string message) {
            var dialog = new ConfirmationWindow {
                DialogTitle = title,
                Message = message,
                Owner = owner ?? Application.Current.MainWindow
            };
            
            return dialog.ShowDialog() == true;
        }
    }
}
