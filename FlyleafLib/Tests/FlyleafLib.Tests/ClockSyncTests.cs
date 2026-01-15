using FlyleafLib;
using FlyleafLib.MediaPlayer;
using Xunit;

namespace FlyleafLib.Tests
{
    public class ManualClock : IClock
    {
        public long Ticks { get; set; }
        public double Speed { get; set; } = 1.0;
    }

    public class ClockSyncTests
    {
        public ClockSyncTests()
        {
            if (!Engine.IsLoaded)
            {
                // Ensure we point to the FFmpeg folder in the root of the workspace
                string ffmpegPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.Environment.CurrentDirectory, "../../../FFmpeg"));
                if (!System.IO.Directory.Exists(ffmpegPath))
                {
                    // Fallback for different test execution environments
                    ffmpegPath = "d:\\Projects\\Develop\\GOON\\FlyleafLib\\FFmpeg";
                }

                Engine.Start(new EngineConfig() 
                { 
                    UIRefresh = false,
                    FFmpegPath = ffmpegPath,
                    LogLevel = LogLevel.Debug 
                });
            }
        }

        [Fact]
        public void PlayerUsesExternalClockWhenSet()
        {
            var config = new Config();
            config.Player.MasterClock = MasterClock.External;
            
            // Note: Player constructor might require FFmpeg initialization if it creates decoders
            // For a pure unit test of properties, we might need a mock or just test the config.
            
            var player = new Player(config);
            var clock = new ManualClock { Ticks = 10000000 }; // 1 second
            
            player.ExternalClock = clock;
            
            Assert.Equal(clock, player.ExternalClock);
            Assert.Equal(MasterClock.External, player.Config.Player.MasterClock);
        }

        [Fact]
        public void SpeedUpdatePropagatesToExternalClock()
        {
            var config = new Config();
            config.Player.MasterClock = MasterClock.External;
            var player = new Player(config);
            var clock = new ManualClock();
            player.ExternalClock = clock;

            player.Speed = 2.0;
            
            Assert.Equal(2.0, clock.Speed);
        }
    }
}
