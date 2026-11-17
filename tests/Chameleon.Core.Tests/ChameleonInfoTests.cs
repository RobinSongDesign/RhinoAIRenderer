using Xunit;

namespace Chameleon.Core.Tests
{
    public class ChameleonInfoTests
    {
        [Fact]
        public void ProductName_IsStable()
        {
            // Settings and cache folders are derived from this name; changing it orphans user data.
            Assert.Equal("Chameleon", ChameleonInfo.ProductName);
        }
    }
}
