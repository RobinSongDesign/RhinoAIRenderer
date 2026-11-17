using System;
using Chameleon.App.Hosting;
using Xunit;

namespace Chameleon.App.Tests
{
    public class HostPlatformTests
    {
        [Fact]
        public void Enum_CoversSupportedPlatforms()
        {
            Assert.Equal(3, Enum.GetValues(typeof(HostPlatform)).Length);
        }
    }
}
