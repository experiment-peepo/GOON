using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GOON.Windows
{
    public partial class CalibrationWindow : Window
    {
        public CalibrationWindow()
        {
            InitializeComponent();
            KeyDown += CalibrationWindow_KeyDown;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position on the screen where HypnoWindow would be
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (App.Settings.DefaultMonitorDeviceName != null)
            {
                foreach (var s in System.Windows.Forms.Screen.AllScreens)
                {
                    if (s.DeviceName == App.Settings.DefaultMonitorDeviceName)
                    {
                        screen = s;
                        break;
                    }
                }
            }

            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;

            // Position center marker at screen center
            CenterMarker.SetValue(System.Windows.Controls.Canvas.LeftProperty, (Width - 100) / 2);
            CenterMarker.SetValue(System.Windows.Controls.Canvas.TopProperty, (Height - 100) / 2);
        }

        private void CalibrationWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
