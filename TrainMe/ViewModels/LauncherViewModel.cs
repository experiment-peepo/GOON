using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TrainMe.Classes;
using Microsoft.Win32;

namespace TrainMe.ViewModels {
    public class LauncherViewModel : ObservableObject {
        public ObservableCollection<string> AddedFiles { get; } = new ObservableCollection<string>();
        private Dictionary<string, ScreenViewer> fileAssignments = new Dictionary<string, ScreenViewer>();
        private Random random = new Random();

        private double _volume;
        public double Volume {
            get => _volume;
            set {
                if (SetProperty(ref _volume, value)) {
                    App.Settings.Volume = value;
                    App.Settings.Save();
                    App.VideoService.SetVolumeAll(value);
                }
            }
        }

        private double _opacity;
        public double Opacity {
            get => _opacity;
            set {
                if (SetProperty(ref _opacity, value)) {
                    App.Settings.Opacity = value;
                    App.Settings.Save();
                    App.VideoService.SetOpacityAll(value);
                }
            }
        }

        private bool _shuffle;
        public bool Shuffle {
            get => _shuffle;
            set => SetProperty(ref _shuffle, value);
        }

        private string _hypnotizeButtonText = "TRAIN ME!";
        public string HypnotizeButtonText {
            get => _hypnotizeButtonText;
            set => SetProperty(ref _hypnotizeButtonText, value);
        }

        private bool _isHypnotizeEnabled;
        public bool IsHypnotizeEnabled {
            get => _isHypnotizeEnabled;
            set => SetProperty(ref _isHypnotizeEnabled, value);
        }

        private bool _isDehypnotizeEnabled;
        public bool IsDehypnotizeEnabled {
            get => _isDehypnotizeEnabled;
            set => SetProperty(ref _isDehypnotizeEnabled, value);
        }

        private bool _isPauseEnabled;
        public bool IsPauseEnabled {
            get => _isPauseEnabled;
            set => SetProperty(ref _isPauseEnabled, value);
        }

        private string _pauseButtonText = "Pause";
        public string PauseButtonText {
            get => _pauseButtonText;
            set => SetProperty(ref _pauseButtonText, value);
        }

        private bool _pauseClicked;

        public ICommand HypnotizeCommand { get; }
        public ICommand DehypnotizeCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand BrowseCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand KofiCommand { get; }
        public ICommand MinimizeCommand { get; }

        public LauncherViewModel() {
            Volume = App.Settings.Volume;
            Opacity = App.Settings.Opacity;

            HypnotizeCommand = new RelayCommand(Hypnotize, _ => IsHypnotizeEnabled);
            DehypnotizeCommand = new RelayCommand(Dehypnotize);
            PauseCommand = new RelayCommand(Pause);
            BrowseCommand = new RelayCommand(Browse);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected);
            ClearAllCommand = new RelayCommand(ClearAll);
            ExitCommand = new RelayCommand(Exit);
            KofiCommand = new RelayCommand(Kofi);
            MinimizeCommand = new RelayCommand(Minimize);

            UpdateButtons();
        }

        private void UpdateButtons() {
            bool hasFiles = AddedFiles.Count > 0;
            bool allAssigned = AllFilesAssigned();
            IsHypnotizeEnabled = hasFiles && allAssigned;
        }

        private bool AllFilesAssigned() {
            foreach (string f in AddedFiles) {
                if (!fileAssignments.ContainsKey(f)) return false;
            }
            return true;
        }

        private void Hypnotize(object parameter) {
            var selectedItems = parameter as System.Collections.IList;
            var assignments = BuildAssignmentsFromSelection(selectedItems);
            if (assignments == null || assignments.Count == 0) return;
            
            App.VideoService.PlayPerMonitor(assignments, Opacity, Volume);
            IsDehypnotizeEnabled = true;
            IsPauseEnabled = true;
        }

        private Dictionary<ScreenViewer, IEnumerable<string>> BuildAssignmentsFromSelection(System.Collections.IList selectedItems) {
            var selectedFiles = new List<string>();
            if (selectedItems != null) {
                foreach (string f in selectedItems) selectedFiles.Add(f);
            }
            
            if (selectedFiles.Count < 1) {
                foreach (var f in AddedFiles) selectedFiles.Add(f);
            }

            if (!AllFilesAssigned()) return null;

            if (Shuffle) selectedFiles = selectedFiles.OrderBy(a => random.Next()).ToList();

            var assignments = new Dictionary<ScreenViewer, IEnumerable<string>>();
            foreach (var f in selectedFiles) {
                var assigned = fileAssignments[f];
                if (assigned == null) continue;
                if (!assignments.ContainsKey(assigned)) assignments[assigned] = new List<string>();
                ((List<string>)assignments[assigned]).Add(f);
            }
            return assignments;
        }

        private void Dehypnotize(object obj) {
            IsDehypnotizeEnabled = false;
            IsPauseEnabled = false;
            App.VideoService.StopAll();
        }

        private void Pause(object obj) {
            if (_pauseClicked) {
                _pauseClicked = false;
                PauseButtonText = "Pause";
                App.VideoService.ContinueAll();
            } else {
                _pauseClicked = true;
                PauseButtonText = "Continue";
                App.VideoService.PauseAll();
            }
        }

        private void Browse(object obj) {
            var dlg = new OpenFileDialog {
                Multiselect = true,
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv|All Files|*.*"
            };
            if (dlg.ShowDialog() == true) {
                var viewers = WindowServices.GetAllScreenViewers();
                var primary = viewers.FirstOrDefault(v => v.Screen.Primary) ?? viewers.FirstOrDefault();
                foreach (var f in dlg.FileNames) {
                    if (!AddedFiles.Contains(f)) {
                        AddedFiles.Add(f);
                        if (primary != null) fileAssignments[f] = primary;
                    }
                }
                UpdateButtons();
            }
        }

        private void RemoveSelected(object parameter) {
            var selectedItems = parameter as System.Collections.IList;
            if (selectedItems == null) return;
            
            var toRemove = new List<string>();
            foreach (string f in selectedItems) toRemove.Add(f);
            foreach (var f in toRemove) {
                AddedFiles.Remove(f);
                if (fileAssignments.ContainsKey(f)) fileAssignments.Remove(f);
            }
            UpdateButtons();
        }

        private void ClearAll(object obj) {
            AddedFiles.Clear();
            fileAssignments.Clear();
            UpdateButtons();
        }

        private void Exit(object obj) {
            if (MessageBox.Show("Exit program? All hypnosis will be terminated :(", "Exit program", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                Application.Current.Shutdown();
            }
        }

        private void Kofi(object obj) {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = "https://ko-fi.com/damsel",
                UseShellExecute = true
            });
        }

        private void Minimize(object obj) {
            if (obj is Window w) w.WindowState = WindowState.Minimized;
        }
        
        // Method to handle Drag & Drop from View
        public void AddDroppedFiles(string[] files) {
             var viewers = WindowServices.GetAllScreenViewers();
             var primary = viewers.FirstOrDefault(v => v.Screen.Primary) ?? viewers.FirstOrDefault();
             foreach (var f in files) {
                 var ext = System.IO.Path.GetExtension(f)?.ToLowerInvariant();
                 if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".wmv") {
                     if (!AddedFiles.Contains(f)) {
                         AddedFiles.Add(f);
                         if (primary != null) fileAssignments[f] = primary;
                     }
                 }
             }
             UpdateButtons();
        }

        // Method to handle assignment change from View
        public void AssignFile(string file, ScreenViewer viewer) {
            if (string.IsNullOrEmpty(file) || viewer == null) return;
            fileAssignments[file] = viewer;
            UpdateButtons();
        }
        
        public ScreenViewer GetAssignment(string file) {
            if (fileAssignments.ContainsKey(file)) return fileAssignments[file];
            return null;
        }
    }
}
