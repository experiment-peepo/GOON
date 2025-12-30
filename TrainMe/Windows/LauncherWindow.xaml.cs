using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TrainMe.Classes;
using TrainMe.ViewModels;

namespace TrainMe.Windows {
    public partial class LauncherWindow : Window {
        private LauncherViewModel ViewModel => DataContext as LauncherViewModel;

        public LauncherWindow() {
            InitializeComponent();
            DataContext = new LauncherViewModel();
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            this.DragMove();
        }

        private void AssignCombo_Loaded(object sender, RoutedEventArgs e) {
            var combo = sender as ComboBox;
            var file = combo?.Tag as string;
            if (combo == null || string.IsNullOrEmpty(file)) return;
            
            combo.ItemsSource = WindowServices.GetAllScreenViewers();
            var assigned = ViewModel?.GetAssignment(file);
            if (assigned != null) {
                combo.SelectedItem = combo.Items.Cast<ScreenViewer>().FirstOrDefault(x => x.ID == assigned.ID);
            }
        }

        private void AssignCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var combo = sender as ComboBox;
            var file = combo?.Tag as string;
            var sel = combo?.SelectedItem as ScreenViewer;
            if (combo == null || string.IsNullOrEmpty(file) || sel == null) return;
            
            ViewModel?.AssignFile(file, sel);
        }

        private void AddedFilesList_DragOver(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) 
                e.Effects = DragDropEffects.Copy; 
            else 
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void AddedFilesList_Drop(object sender, DragEventArgs e) {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            ViewModel?.AddDroppedFiles(files);
        }
    }
}
