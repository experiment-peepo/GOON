using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;

namespace GOON.Windows {
    /// <summary>
    /// Modern confirmation dialog matching ConfirmationWindow styling
    /// </summary>
    [SupportedOSPlatform("windows")]
    public partial class ConfirmationDialog : Window {
        public bool Result { get; private set; }

        public ConfirmationDialog(string message, string title = "Confirm Action") {
            InitializeComponent();
            
            MessageTextBlock.Text = message;
            TitleTextBlock.Text = title;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left) {
                DragMove();
            }
        }

        private void YesButton_Click(object sender, RoutedEventArgs e) {
            Result = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e) {
            Result = false;
            Close();
        }

        /// <summary>
        /// Shows a modern confirmation dialog
        /// </summary>
        /// <param name="message">The message to display</param>
        /// <param name="title">The dialog title</param>
        /// <returns>True if user clicked Yes, false otherwise</returns>
        public static bool Show(string message, string title = "Confirm Action") {
            var dialog = new ConfirmationDialog(message, title);
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}
