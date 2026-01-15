using FlyleafLib;
using FlyleafLib.MediaFramework.MediaDemuxer;
using Xunit;

namespace FlyleafLib.Tests
{
    public class DemuxerTests
    {
        [Fact]
        public void DemuxerConfigHasUserAgentAndCookies()
        {
            var config = new Config.DemuxerConfig();
            config.UserAgent = "TestAgent";
            config.Cookies = "TestCookie=Value";

            Assert.Equal("TestAgent", config.UserAgent);
            Assert.Equal("TestCookie=Value", config.Cookies);
        }

        [Fact]
        public void DemuxerConfigFormatOptToUnderlyingDefaultIsTrue()
        {
            var config = new Config.DemuxerConfig();
            // Assuming the roadmap intended this to be true by default for compatibility
            // Let's verify what the current implementation has.
            Assert.True(config.FormatOptToUnderlying);
        }
    }
}
