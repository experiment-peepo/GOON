using System;
using System.Windows;
using System.Windows.Input;
using GOON.ViewModels;
using System.IO;
using System.Diagnostics;

namespace GOON.Windows {
    public partial class SettingsWindow : Window {
        private SettingsViewModel _viewModel;

        public SettingsWindow() {
            InitializeComponent();
            _viewModel = new SettingsViewModel();
            _viewModel.RequestClose += (s, e) => {
                // Apply settings changes in owner window if it's LauncherWindow
                if (Owner is LauncherWindow launcherWindow) {
                    launcherWindow.ReloadHotkeys();
                    launcherWindow.ApplyAlwaysOnTopSetting();
                    App.VideoService.RefreshAllOpacities();
                }
                this.Close();
            };
            DataContext = _viewModel;
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

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

    }
}
