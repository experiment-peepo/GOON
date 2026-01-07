using System;
using System.Collections.Generic;
using System.Linq;
using GOON.ViewModels;
using Xunit;

namespace GOON.Tests {
    public class GroupHypnoViewModelTests {
        [Fact]
        public void Constructor_WithEmptyChildren_DoesNotThrow() {
            var group = new GroupHypnoViewModel(Enumerable.Empty<HypnoViewModel>());
            Assert.NotNull(group);
        }

        [Fact]
        public void Play_CallsAllChildren() {
            var child1 = new HypnoViewModel();
            var child2 = new HypnoViewModel();
            var group = new GroupHypnoViewModel(new[] { child1, child2 });
            
            group.Play();
            
            Assert.Equal(System.Windows.Controls.MediaState.Play, child1.MediaState);
            Assert.Equal(System.Windows.Controls.MediaState.Play, child2.MediaState);
        }

        [Fact]
        public void Volume_SetsAllChildren() {
            var child1 = new HypnoViewModel();
            var child2 = new HypnoViewModel();
            var group = new GroupHypnoViewModel(new[] { child1, child2 });
            
            group.Volume = 0.5;
            
            Assert.Equal(0.5, child1.Volume);
            Assert.Equal(0.5, child2.Volume);
        }

        [Fact]
        public void TogglePlayPause_WithNoChildren_DoesNotThrow() {
            var group = new GroupHypnoViewModel(Enumerable.Empty<HypnoViewModel>());
            group.TogglePlayPause();
            Assert.NotNull(group);
        }

        [Fact]
        public void TogglePlayPause_BasedOnFirstChild_Works() {
            var child1 = new HypnoViewModel { MediaState = System.Windows.Controls.MediaState.Play };
            var child2 = new HypnoViewModel { MediaState = System.Windows.Controls.MediaState.Pause };
            var group = new GroupHypnoViewModel(new[] { child1, child2 });
            
            // child1 is playing, so it should pause ALL
            group.TogglePlayPause();
            
            Assert.Equal(System.Windows.Controls.MediaState.Pause, child1.MediaState);
            Assert.Equal(System.Windows.Controls.MediaState.Pause, child2.MediaState);
        }
    }
}
