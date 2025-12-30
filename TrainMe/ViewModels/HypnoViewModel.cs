using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using TrainMe.Classes;

namespace TrainMe.ViewModels {
    public class HypnoViewModel : ObservableObject {
        private string[] _files;
        private int _currentPos = 0;
        
        private Uri _currentSource;
        public Uri CurrentSource {
            get => _currentSource;
            set {
                SetProperty(ref _currentSource, value);
                // Reset position logic might be handled by behavior or property change trigger in View
            }
        }

        private double _opacity;
        public double Opacity {
            get => _opacity;
            set => SetProperty(ref _opacity, value);
        }

        private double _volume;
        public double Volume {
            get => _volume;
            set => SetProperty(ref _volume, value);
        }

        private MediaState _mediaState = MediaState.Manual;
        public MediaState MediaState {
            get => _mediaState;
            set => SetProperty(ref _mediaState, value);
        }

        // We'll use a simple mechanism to request Play/Pause/Stop via property or event if not binding directly to MediaElement's LoadedBehavior.
        // Since MediaElement loaded behavior is restrictive, we often use Manual and control it via attached properties or code-behind bridging.
        // For simplicity here, we'll expose methods that the View can call, or events the View can subscribe to.
        // Or better: Use an action delegate or event to signal the View.
        
        public event EventHandler RequestPlay;
        public event EventHandler RequestPause;
        public event EventHandler RequestStop;

        public HypnoViewModel() {
        }

        public void SetQueue(IEnumerable<string> files) {
            _files = files?.ToArray() ?? new string[0];
            _currentPos = -1;
            PlayNext();
        }

        public void PlayNext() {
            if (_files == null || _files.Length == 0) return;

            if (_currentPos + 1 < _files.Length) {
                _currentPos++;
            } else {
                _currentPos = 0; // Loop
            }

            LoadCurrentVideo();
        }

        private void LoadCurrentVideo() {
            if (_files == null || _files.Length == 0 || _currentPos < 0 || _currentPos >= _files.Length) return;

            var path = _files[_currentPos];
            if (!System.IO.Path.IsPathRooted(path)) {
                // Should handle error, maybe an error property
                return;
            }
            
            CurrentSource = new Uri(path, UriKind.Absolute);
            RequestPlay?.Invoke(this, EventArgs.Empty);
        }

        public void OnMediaEnded() {
            PlayNext();
        }

        public void OnMediaFailed(Exception ex) {
            // Log or show error?
            // For now, just skip to next to avoid getting stuck?
             PlayNext();
        }

        public void Play() {
            RequestPlay?.Invoke(this, EventArgs.Empty);
        }

        public void Pause() {
            RequestPause?.Invoke(this, EventArgs.Empty);
        }

        public void Stop() {
            RequestStop?.Invoke(this, EventArgs.Empty);
        }
    }
}
