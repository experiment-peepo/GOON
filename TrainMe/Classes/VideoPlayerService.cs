using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TrainMe.Windows;
using TrainMe.ViewModels;

namespace TrainMe.Classes {
    public class VideoPlayerService {
        readonly List<HypnoWindow> players = new List<HypnoWindow>();

        public bool IsPlaying => players.Count > 0;

        public void PlayOnScreens(IEnumerable<string> files, IEnumerable<ScreenViewer> screens, double opacity, double volume) {
            StopAll();
            var queue = NormalizeFiles(files).ToArray();
            foreach (var sv in screens ?? Enumerable.Empty<ScreenViewer>()) {
                var w = new HypnoWindow();
                w.Show();
                WindowServices.MoveWindowToScreen(w, sv.Screen);
                
                w.ViewModel.Opacity = opacity;
                w.ViewModel.Volume = volume;
                w.ViewModel.SetQueue(queue); // Start playing automatically
                
                players.Add(w);
            }
        }

        public void PauseAll() {
            foreach (var w in players) w.ViewModel.Pause();
        }

        public void ContinueAll() {
            foreach (var w in players) w.ViewModel.Play();
        }

        public void StopAll() {
            foreach (var w in players) w.Close();
            players.Clear();
        }

        public void SetVolumeAll(double volume) {
            foreach (var w in players) w.ViewModel.Volume = volume;
        }

        public void SetOpacityAll(double opacity) {
            foreach (var w in players) w.ViewModel.Opacity = opacity;
        }

        public void PlayPerMonitor(IDictionary<ScreenViewer, IEnumerable<string>> assignments, double opacity, double volume) {
            StopAll();
            if (assignments == null) return;
            foreach (var kvp in assignments) {
                var sv = kvp.Key;
                var queue = NormalizeFiles(kvp.Value).ToArray();
                if (queue.Length == 0) continue;
                var w = new HypnoWindow();
                w.Show();
                WindowServices.MoveWindowToScreen(w, sv.Screen);
                
                w.ViewModel.Opacity = opacity;
                w.ViewModel.Volume = volume;
                w.ViewModel.SetQueue(queue);

                players.Add(w);
            }
        }

        IEnumerable<string> NormalizeFiles(IEnumerable<string> files) {
            var list = new List<string>();
            foreach (var f in files ?? Enumerable.Empty<string>()) {
                if (Path.IsPathRooted(f)) {
                    if (File.Exists(f)) list.Add(f);
                }
            }
            return list;
        }
    }
}
