using System;
using System.Collections.Generic;
using System.Linq;

namespace GOON.ViewModels {
    /// <summary>
    /// A composite view model that controls multiple HypnoViewModel instances.
    /// Used for unified control of "All Monitors" playback.
    /// </summary>
    public class GroupHypnoViewModel : HypnoViewModel {
        private readonly List<HypnoViewModel> _children;

        public GroupHypnoViewModel(IEnumerable<HypnoViewModel> children) {
            _children = children.ToList();
            
            // Sync properties from first child
            if (_children.Any()) {
                var first = _children.First();
                // We use base property setters to avoid triggering virtual overheads unnecessarily
                // but here we just want to reflect state
            }
        }

        public override void Play() {
            foreach (var child in _children) child.Play();
            base.Play();
        }

        public override void Pause() {
            foreach (var child in _children) child.Pause();
            base.Pause();
        }

        public override void TogglePlayPause() {
            // Determine majority state or just use first child
            if (!_children.Any()) return;
            
            var isAnyPlaying = _children.Any(c => c.MediaState == System.Windows.Controls.MediaState.Play);
            if (isAnyPlaying) {
                Pause();
            } else {
                Play();
            }
        }

        public override void PlayNext() {
            foreach (var child in _children) child.PlayNext();
            // No base call needed as we don't maintain our own queue in the group proxy usually
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
