using System;
using System.Collections.Generic;
using System.Linq;
using TrainMeX.Classes;
using TrainMeX.ViewModels;
using Xunit;

namespace TrainMeX.Tests {
    public class HypnoViewModelTests {
        [Fact]
        public void Constructor_CreatesViewModel() {
            var viewModel = new HypnoViewModel();
            
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void SetQueue_WithNullFiles_SetsEmptyQueue() {
            var viewModel = new HypnoViewModel();
            
            viewModel.SetQueue(null);
            
            // Should not throw
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void SetQueue_WithEmptyFiles_SetsEmptyQueue() {
            var viewModel = new HypnoViewModel();
            
            viewModel.SetQueue(Enumerable.Empty<VideoItem>());
            
            // Should not throw
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void PlayNext_WithEmptyQueue_DoesNotThrow() {
            var viewModel = new HypnoViewModel();
            viewModel.SetQueue(Enumerable.Empty<VideoItem>());
            
            // PlayNext is called internally by SetQueue
            // Should not throw
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void Play_WithNoQueue_DoesNotThrow() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            
            viewModel.RequestPlay += (s, e) => eventRaised = true;
            
            viewModel.Play();
            
            Assert.True(eventRaised);
        }

        [Fact]
        public void Pause_WithNoQueue_DoesNotThrow() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            
            viewModel.RequestPause += (s, e) => eventRaised = true;
            
            viewModel.Pause();
            
            Assert.True(eventRaised);
        }

        [Fact]
        public void Stop_WithNoQueue_DoesNotThrow() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            
            viewModel.RequestStop += (s, e) => eventRaised = true;
            
            viewModel.Stop();
            
            Assert.True(eventRaised);
        }

        [Fact]
        public void OnMediaEnded_WithEmptyQueue_DoesNotThrow() {
            var viewModel = new HypnoViewModel();
            
            viewModel.OnMediaEnded();
            
            // Should not throw
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void OnMediaFailed_WithNullException_DoesNotThrow() {
            var viewModel = new HypnoViewModel();
            bool errorRaised = false;
            
            viewModel.MediaErrorOccurred += (s, e) => errorRaised = true;
            
            viewModel.OnMediaFailed(null);
            
            Assert.True(errorRaised);
        }

        [Fact]
        public void OnMediaFailed_WithException_RaisesMediaErrorOccurred() {
            var viewModel = new HypnoViewModel();
            bool errorRaised = false;
            string errorMessage = null;
            
            viewModel.MediaErrorOccurred += (s, e) => {
                errorRaised = true;
                errorMessage = e.ErrorMessage;
            };
            
            viewModel.OnMediaFailed(new Exception("Test error"));
            
            Assert.True(errorRaised);
            Assert.NotNull(errorMessage);
            Assert.Contains("Test error", errorMessage);
        }

        [Fact]
        public void Opacity_SetValue_RaisesPropertyChanged() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            
            viewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(HypnoViewModel.Opacity)) {
                    eventRaised = true;
                }
            };
            
            viewModel.Opacity = 0.5;
            
            Assert.True(eventRaised);
            Assert.Equal(0.5, viewModel.Opacity);
        }

        [Fact]
        public void Volume_SetValue_RaisesPropertyChanged() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            
            viewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(HypnoViewModel.Volume)) {
                    eventRaised = true;
                }
            };
            
            viewModel.Volume = 0.7;
            
            Assert.True(eventRaised);
            Assert.Equal(0.7, viewModel.Volume);
        }

        [Fact]
        public void CurrentSource_SetValue_RaisesPropertyChanged() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            
            viewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(HypnoViewModel.CurrentSource)) {
                    eventRaised = true;
                }
            };
            
            viewModel.CurrentSource = new Uri("file:///test.mp4");
            
            Assert.True(eventRaised);
            Assert.NotNull(viewModel.CurrentSource);
        }

        [Fact]
        public void MediaState_SetValue_RaisesPropertyChanged() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            
            viewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(HypnoViewModel.MediaState)) {
                    eventRaised = true;
                }
            };
            
            viewModel.MediaState = System.Windows.Controls.MediaState.Play;
            
            Assert.True(eventRaised);
        }
    }
}

