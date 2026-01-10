using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GOON.ViewModels {
    /// <summary>
    /// A composite view model that controls multiple HypnoViewModel instances.
    /// Used for unified control of "All Monitors" playback.
    /// </summary>
    public class GroupHypnoViewModel : HypnoViewModel {
        private readonly List<HypnoViewModel> _children;

        public GroupHypnoViewModel(IEnumerable<HypnoViewModel> children) {
            _children = children.ToList();
            
            // Sync properties from first child and monitor for changes
            if (_children.Any()) {
                var first = _children.First();
                this.MediaState = first.MediaState;
                this.Volume = first.Volume;
                this.Opacity = first.Opacity;
                this.SpeedRatio = first.SpeedRatio;

                foreach (var child in _children) {
                    child.PropertyChanged += (s, e) => {
                        if (e.PropertyName == nameof(MediaState)) {
                            SyncStateWithChildren();
                        }
                    };
                }
            }
        }

        private void SyncStateWithChildren() {
            if (!_children.Any()) return;

            Application.Current?.Dispatcher.InvokeAsync(() => {
                // If any child is playing, the group status should be 'Play'
                // This ensures the Pause icon shows up if any monitor is active
                if (_children.Any(c => c.MediaState == System.Windows.Controls.MediaState.Play)) {
                    this.MediaState = System.Windows.Controls.MediaState.Play;
                } else {
                    // Otherwise, reflect the first child's state (Master)
                    this.MediaState = _children.First().MediaState;
                }
            });
        }

        public override void Play() {
            foreach (var child in _children) child.Play();
            this.MediaState = System.Windows.Controls.MediaState.Play;
            base.Play();
        }

        public override void Pause() {
            foreach (var child in _children) child.Pause();
            this.MediaState = System.Windows.Controls.MediaState.Pause;
            base.Pause();
        }

        public override void TogglePlayPause() {
            if (!_children.Any()) return;
            
            var isAnyPlaying = _children.Any(c => c.MediaState == System.Windows.Controls.MediaState.Play);
            if (isAnyPlaying) {
                Pause();
            } else {
                Play();
            }
        }

        public override void PlayNext() {
            // Optimistically set state to Play so icon reflects intentionality
            this.MediaState = System.Windows.Controls.MediaState.Play;
            foreach (var child in _children) child.PlayNext();
        }

        public override void ForcePlay() {
            foreach (var child in _children) child.ForcePlay();
        }

        public override double Volume {
            get => base.Volume;
            set {
                base.Volume = value;
                foreach (var child in _children) child.Volume = value;
            }
        }

        public override double Opacity {
            get => base.Opacity;
            set {
                base.Opacity = value;
                foreach (var child in _children) child.Opacity = value;
            }
        }

        public override double SpeedRatio {
            get => base.SpeedRatio;
            set {
                base.SpeedRatio = value;
                foreach (var child in _children) child.SpeedRatio = value;
            }
        }
    }
}
