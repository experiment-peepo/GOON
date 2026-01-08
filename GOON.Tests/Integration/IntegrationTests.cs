using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GOON.Classes;
using GOON.ViewModels;
using Xunit;

namespace GOON.Tests {
    public class IntegrationTests : IDisposable {
        private readonly string _testDirectory;
        private readonly string _testVideoFile1;
        private readonly string _testVideoFile2;

        public IntegrationTests() {
            _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
            
            _testVideoFile1 = Path.Combine(_testDirectory, "test1.mp4");
            _testVideoFile2 = Path.Combine(_testDirectory, "test2.mp4");
            
            File.WriteAllText(_testVideoFile1, "fake video content 1");
            File.WriteAllText(_testVideoFile2, "fake video content 2");
        }

        [Fact]
        public void FileValidator_And_VideoItem_Integration() {
            // Validate file using FileValidator
            var isValid = FileValidator.ValidateVideoFile(_testVideoFile1, out string errorMessage);
            Assert.True(isValid);
            Assert.Null(errorMessage);
            
            // Create VideoItem and validate
            var item = new VideoItem(_testVideoFile1);
            item.Validate();
            
            Assert.Equal(FileValidationStatus.Valid, item.ValidationStatus);
            Assert.True(item.IsValid);
        }

        [Fact]
        public void LruCache_And_VideoPlayerService_Integration() {
            var service = new VideoPlayerService();
            
            // Service uses LruCache internally
            // Clear cache to test integration
            service.ClearFileExistenceCache();
            
            // Should not throw
            Assert.NotNull(service);
        }

        [Fact]
        public void ServiceContainer_And_UserSettings_Integration() {
            var settings = new UserSettings {
                Opacity = 0.5,
                Volume = 0.7
            };
            
            ServiceContainer.Register(settings);
            
            var retrieved = ServiceContainer.Get<UserSettings>();
            
            Assert.Same(settings, retrieved);
            Assert.Equal(0.5, retrieved.Opacity);
            Assert.Equal(0.7, retrieved.Volume);
        }

        [Fact]
        public void ObservableObject_And_VideoItem_Integration() {
            var item = new VideoItem(_testVideoFile1);
            bool opacityChanged = false;
            bool volumeChanged = false;
            
            item.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(VideoItem.Opacity)) {
                    opacityChanged = true;
                }
                if (e.PropertyName == nameof(VideoItem.Volume)) {
                    volumeChanged = true;
                }
            };
            
            item.Opacity = 0.6;
            item.Volume = 0.8;
            
            Assert.True(opacityChanged);
            Assert.True(volumeChanged);
            Assert.Equal(0.6, item.Opacity);
            Assert.Equal(0.8, item.Volume);
        }

        [Fact]
        public void Playlist_Serialization_Integration() {
            var playlist = new Playlist();
            playlist.Items.Add(new PlaylistItem {
                FilePath = _testVideoFile1,
                ScreenDeviceName = "Screen1",
                Opacity = 0.8,
                Volume = 0.6
            });
            playlist.Items.Add(new PlaylistItem {
                FilePath = _testVideoFile2,
                ScreenDeviceName = "Screen2",
                Opacity = 0.9,
                Volume = 0.7
            });
            
            var json = System.Text.Json.JsonSerializer.Serialize(playlist);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<Playlist>(json);
            
            Assert.NotNull(deserialized);
            Assert.Equal(2, deserialized.Items.Count);
            Assert.Equal(_testVideoFile1, deserialized.Items[0].FilePath);
            Assert.Equal(_testVideoFile2, deserialized.Items[1].FilePath);
        }

        [Fact]
        public void FileValidator_ComprehensiveValidation_Integration() {
            // Test full validation flow
            var result = FileValidator.ValidateVideoFile(_testVideoFile1, out string errorMessage);
            
            Assert.True(result);
            Assert.Null(errorMessage);
            
            // Test with invalid file
            var invalidFile = Path.Combine(_testDirectory, "test.txt");
            File.WriteAllText(invalidFile, "content");
            
            result = FileValidator.ValidateVideoFile(invalidFile, out errorMessage);
            
            Assert.False(result);
            Assert.NotNull(errorMessage);
            Assert.Contains("not supported", errorMessage);
        }

        [Fact]
        public void VideoItem_Validation_And_Properties_Integration() {
            var item = new VideoItem(_testVideoFile1);
            
            // Initial state
            Assert.Equal(FileValidationStatus.Unknown, item.ValidationStatus);
            Assert.Equal(0.9, item.Opacity);
            Assert.Equal(0.5, item.Volume);
            
            // Validate
            item.Validate();
            
            Assert.Equal(FileValidationStatus.Valid, item.ValidationStatus);
            Assert.True(item.IsValid);
            
            // Change properties
            item.Opacity = 0.7;
            item.Volume = 0.8;
            
            Assert.Equal(0.7, item.Opacity);
            Assert.Equal(0.8, item.Volume);
        }

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

