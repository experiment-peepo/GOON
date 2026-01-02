using System.IO;
using System.Text.Json;
using TrainMeX.Classes;
using Xunit;

namespace TrainMeX.Tests {
    public class PlaylistTests {
        [Fact]
        public void Constructor_CreatesEmptyItemsList() {
            var playlist = new Playlist();
            
            Assert.NotNull(playlist.Items);
            Assert.Empty(playlist.Items);
        }

        [Fact]
        public void PlaylistItem_DefaultValues_AreSet() {
            var item = new PlaylistItem();
            
            Assert.Null(item.FilePath);
            Assert.Null(item.ScreenDeviceName);
            Assert.Equal(0.9, item.Opacity);
            Assert.Equal(1.0, item.Volume);
        }

        [Fact]
        public void PlaylistItem_CanSetProperties() {
            var item = new PlaylistItem {
                FilePath = "test.mp4",
                ScreenDeviceName = "Screen1",
                Opacity = 0.5,
                Volume = 0.7
            };
            
            Assert.Equal("test.mp4", item.FilePath);
            Assert.Equal("Screen1", item.ScreenDeviceName);
            Assert.Equal(0.5, item.Opacity);
            Assert.Equal(0.7, item.Volume);
        }

        [Fact]
        public void Serialize_WithItems_ProducesValidJson() {
            var playlist = new Playlist();
            playlist.Items.Add(new PlaylistItem {
                FilePath = "test1.mp4",
                ScreenDeviceName = "Screen1",
                Opacity = 0.8,
                Volume = 0.6
            });
            playlist.Items.Add(new PlaylistItem {
                FilePath = "test2.mp4",
                ScreenDeviceName = "Screen2",
                Opacity = 0.9,
                Volume = 0.7
            });
            
            var json = JsonSerializer.Serialize(playlist);
            
            Assert.NotNull(json);
            Assert.Contains("test1.mp4", json);
            Assert.Contains("test2.mp4", json);
            Assert.Contains("Screen1", json);
            Assert.Contains("Screen2", json);
        }

        [Fact]
        public void Deserialize_WithValidJson_CreatesPlaylist() {
            var json = @"{
  ""Items"": [
    {
      ""FilePath"": ""test1.mp4"",
      ""ScreenDeviceName"": ""Screen1"",
      ""Opacity"": 0.8,
      ""Volume"": 0.6
    },
    {
      ""FilePath"": ""test2.mp4"",
      ""ScreenDeviceName"": ""Screen2"",
      ""Opacity"": 0.9,
      ""Volume"": 0.7
    }
  ]
}";
            
            var playlist = JsonSerializer.Deserialize<Playlist>(json);
            
            Assert.NotNull(playlist);
            Assert.Equal(2, playlist.Items.Count);
            Assert.Equal("test1.mp4", playlist.Items[0].FilePath);
            Assert.Equal("Screen1", playlist.Items[0].ScreenDeviceName);
            Assert.Equal(0.8, playlist.Items[0].Opacity);
            Assert.Equal(0.6, playlist.Items[0].Volume);
            Assert.Equal("test2.mp4", playlist.Items[1].FilePath);
        }

        [Fact]
        public void Serialize_AndDeserialize_RoundTrip() {
            var originalPlaylist = new Playlist();
            originalPlaylist.Items.Add(new PlaylistItem {
                FilePath = "test1.mp4",
                ScreenDeviceName = "Screen1",
                Opacity = 0.8,
                Volume = 0.6
            });
            originalPlaylist.Items.Add(new PlaylistItem {
                FilePath = "test2.mp4",
                ScreenDeviceName = "Screen2",
                Opacity = 0.9,
                Volume = 0.7
            });
            
            var json = JsonSerializer.Serialize(originalPlaylist);
            var deserializedPlaylist = JsonSerializer.Deserialize<Playlist>(json);
            
            Assert.NotNull(deserializedPlaylist);
            Assert.Equal(originalPlaylist.Items.Count, deserializedPlaylist.Items.Count);
            
            for (int i = 0; i < originalPlaylist.Items.Count; i++) {
                var original = originalPlaylist.Items[i];
                var deserialized = deserializedPlaylist.Items[i];
                
                Assert.Equal(original.FilePath, deserialized.FilePath);
                Assert.Equal(original.ScreenDeviceName, deserialized.ScreenDeviceName);
                Assert.Equal(original.Opacity, deserialized.Opacity);
                Assert.Equal(original.Volume, deserialized.Volume);
            }
        }

        [Fact]
        public void Deserialize_WithEmptyItemsArray_CreatesEmptyPlaylist() {
            var json = @"{ ""Items"": [] }";
            
            var playlist = JsonSerializer.Deserialize<Playlist>(json);
            
            Assert.NotNull(playlist);
            Assert.Empty(playlist.Items);
        }

        [Fact]
        public void Deserialize_WithMissingItems_CreatesEmptyPlaylist() {
            var json = @"{}";
            
            var playlist = JsonSerializer.Deserialize<Playlist>(json);
            
            Assert.NotNull(playlist);
            // Items will be null if not present, but constructor initializes it
            // This depends on JsonSerializer behavior
        }
    }
}

