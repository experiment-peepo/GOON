using System;
using System.Windows;
using System.Windows.Input;
using TrainMeX.Classes;
using System.Reflection;

namespace TrainMeX.Windows {
    public partial class AboutWindow : Window {
        public AboutWindow() {
            InitializeComponent();
            DataContext = this;
            
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            e.Handled = true;
            if (e.ButtonState == MouseButtonState.Pressed) {
                try {
                    this.DragMove();
                } catch (InvalidOperationException) { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        public ICommand OpenKoFiCommand => new RelayCommand(OpenKoFi);

        private void OpenKoFi(object obj) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "https://ko-fi.com/vexfromdestiny",
                    UseShellExecute = true
                });
            } catch (Exception ex) {
                Logger.Error("Failed to open Ko-Fi link", ex);
            }
        }
    }
}
