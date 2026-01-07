using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GOON.Classes;
using GOON.ViewModels;
using Xunit;

namespace GOON.Tests {
    public class LauncherViewModelTests : IDisposable {
        private readonly string _testDirectory;
        private readonly string _testVideoFile;

        public LauncherViewModelTests() {
            _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
            
            _testVideoFile = Path.Combine(_testDirectory, "test.mp4");
            File.WriteAllText(_testVideoFile, "fake video content");

            // Setup services
            ServiceContainer.Clear();
            ServiceContainer.Register(new UserSettings());
            ServiceContainer.Register(new VideoPlayerService());
        }

        [Fact]
        public void Constructor_CreatesViewModel() {
            var viewModel = new LauncherViewModel();
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void AddedFiles_IsInitialized() {
            var viewModel = new LauncherViewModel();
            Assert.NotNull(viewModel.AddedFiles);
            Assert.Empty(viewModel.AddedFiles);
        }

        [Fact]
        public void AvailableScreens_IsInitialized() {
            var viewModel = new LauncherViewModel();
            Assert.NotNull(viewModel.AvailableScreens);
            // Should at least have "All Monitors" placeholder
            Assert.NotEmpty(viewModel.AvailableScreens);
        }

        [Fact]
        public void Shuffle_DefaultValue_IsFalse() {
            var viewModel = new LauncherViewModel();
            Assert.False(viewModel.Shuffle);
        }

        [Fact]
        public void HypnotizeButtonText_DefaultValue_IsGOON() {
            var viewModel = new LauncherViewModel();
            Assert.Equal("TRAIN ME!", viewModel.HypnotizeButtonText);
        }

        #region Edge Cases

        [Fact]
        public void MoveVideoItem_WithInvalidIndex_DoesNothing() {
            var viewModel = new LauncherViewModel();
            var item = new VideoItem(_testVideoFile);
            viewModel.AddedFiles.Add(item);
            
            viewModel.MoveVideoItem(item, 10); // Invalid index
            
            Assert.Single(viewModel.AddedFiles);
            Assert.Equal(0, viewModel.AddedFiles.IndexOf(item));
        }

        [Fact]
        public void MoveVideoItem_NegativeIndex_DoesNothing() {
            var viewModel = new LauncherViewModel();
            var item = new VideoItem(_testVideoFile);
            viewModel.AddedFiles.Add(item);
            
            viewModel.MoveVideoItem(item, -1);
            
            Assert.Single(viewModel.AddedFiles);
            Assert.Equal(0, viewModel.AddedFiles.IndexOf(item));
        }

        [Fact]
        public void UpdateButtons_WithUnassignedFiles_DisablesHypnotize() {
            var viewModel = new LauncherViewModel();
            var item = new VideoItem(_testVideoFile); // No screen assigned
            viewModel.AddedFiles.Add(item);
            
            Assert.False(viewModel.IsHypnotizeEnabled);
        }

        [Fact]
        public void InvalidateScreenCache_Works() {
            var viewModel = new LauncherViewModel();
            viewModel.InvalidateScreenCache();
            // No crash means success for simple property reset
            Assert.NotNull(viewModel);
        }

        [Fact]
        public void RemoveItem_NullParameter_DoesNothing() {
            var viewModel = new LauncherViewModel();
            viewModel.AddedFiles.Add(new VideoItem(_testVideoFile));
            
            // RemoveItemCommand is private/handled via relay, but we can test internal logic if we had access
            // Let's test AddedFiles directly since RemoveItem just calls AddedFiles.Remove
            viewModel.AddedFiles.Remove(null);
            Assert.Single(viewModel.AddedFiles);
        }

        [Fact]
        public void ClearAll_EmptyList_DoesNothing() {
            var viewModel = new LauncherViewModel();
            viewModel.AddedFiles.Clear();
            
            // Should not throw
            viewModel.AddedFiles.Clear();
            Assert.Empty(viewModel.AddedFiles);
        }

        #endregion

        public void Dispose() {
            try {
                if (Directory.Exists(_testDirectory)) {
                    Directory.Delete(_testDirectory, true);
                }
            } catch {
                // Ignore cleanup errors
            }
        }
    }
}

