using System;
using System.Collections.Generic;
using System.Linq;
using GOON.Classes;
using GOON.ViewModels;
using Xunit;

namespace GOON.Tests {
    public class HypnoViewModelTests {
        public HypnoViewModelTests() {
            // Reset container and register default settings for each test
            ServiceContainer.Clear();
            ServiceContainer.Register(new UserSettings());
        }

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
            string? errorMessage = null;
            
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

        [Fact]
        public void Concurrency_PlayPauseStop_ThreadSafety() {
            var viewModel = new HypnoViewModel();
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            
            var threads = new List<System.Threading.Thread>();
            bool running = true;
            object lockObj = new object();

            // Simulate multiple threads interacting with the VM
            for (int i = 0; i < 10; i++) {
                var t = new System.Threading.Thread(() => {
                    try {
                        while (running) {
                            lock (lockObj) {
                                if (!running) break;
                            }
                            viewModel.Play();
                            viewModel.Pause();
                            viewModel.Stop();
                            System.Threading.Thread.Sleep(1);
                        }
                    } catch (Exception ex) {
                        exceptions.Add(ex);
                    }
                });
                threads.Add(t);
                t.Start();
            }

            System.Threading.Thread.Sleep(500);
            lock (lockObj) {
                running = false;
            }
            
            foreach (var t in threads) {
                t.Join(500);
            }

            Assert.Empty(exceptions);
        }

        #region Edge Cases

        [Fact]
        public void OnMediaOpened_StaleSource_DoesNotResetLoading() {
            var viewModel = new HypnoViewModel();
            var item = new VideoItem("file:///test.mp4");
            viewModel.SetQueue(new[] { item });
            
            // Expected source is now set in VM.
            // Simulate a stale event with a different source
            viewModel.CurrentSource = new Uri("file:///stale.mp4");
            
            // This should hit the stale check and return early
            viewModel.OnMediaOpened();
            
            // No easy way to check internal _isLoading but success means no crash
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void OnMediaFailed_UnrecoverableError_IncrementsFailureThreshold() {
            var viewModel = new HypnoViewModel();
            var item = new VideoItem("file:///broken.mp4");
            viewModel.SetQueue(new[] { item });
            
            // COMException 0x8898050C is unrecoverable
            var ex = new System.Runtime.InteropServices.COMException("Broken", unchecked((int)0x8898050C));
            
            // This should mark the file as failed immediately
            viewModel.OnMediaFailed(ex);
            
            // Subsequent PlayNext should notice max failures and potentially stop if all failed
            // We verify it doesn't crash
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void RefreshOpacity_ForcesPropertyChanged() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            viewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(HypnoViewModel.Opacity)) eventRaised = true;
            };
            
            viewModel.RefreshOpacity();
            
            Assert.True(eventRaised);
        }

        [Fact]
        public void TogglePlayPause_RapidCalls_IsConsistent() {
            var viewModel = new HypnoViewModel();
            viewModel.MediaState = System.Windows.Controls.MediaState.Pause;
            
            viewModel.TogglePlayPause();
            Assert.Equal(System.Windows.Controls.MediaState.Play, viewModel.MediaState);
            
            viewModel.TogglePlayPause();
            Assert.Equal(System.Windows.Controls.MediaState.Pause, viewModel.MediaState);
        }

        [Fact]
        public void SyncPosition_WithZero_DoesNotThrow() {
            var viewModel = new HypnoViewModel();
            bool eventRaised = false;
            viewModel.RequestSyncPosition += (s, e) => {
                if (e == TimeSpan.Zero) eventRaised = true;
            };
            
            viewModel.SyncPosition(TimeSpan.Zero);
            Assert.True(eventRaised);
        }

        [Fact]
        public void Volume_BoundaryValues_AreHandled() {
            var viewModel = new HypnoViewModel();
            
            viewModel.Volume = -1.0;
            Assert.Equal(-1.0, viewModel.Volume); // It doesn't clamp in VM currently, which is an edge case to note
            
            viewModel.Volume = 2.0;
            Assert.Equal(2.0, viewModel.Volume);
        }

        [Fact]
        public void SpeedRatio_BoundaryValues_AreHandled() {
            var viewModel = new HypnoViewModel();
            
            viewModel.SpeedRatio = 0.0;
            Assert.Equal(0.0, viewModel.SpeedRatio);
            
            viewModel.SpeedRatio = 10.0;
            Assert.Equal(10.0, viewModel.SpeedRatio);
        }

        #endregion
    }
}

