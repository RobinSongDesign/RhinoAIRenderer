using Chameleon.Core.Generation;
using Xunit;

namespace Chameleon.Core.Tests.Generation
{
    public class RenderExceptionTests
    {
        [Theory]
        [InlineData(400, RenderErrorCode.InvalidRequest)]
        [InlineData(401, RenderErrorCode.AuthFailed)]
        [InlineData(403, RenderErrorCode.AuthFailed)]
        [InlineData(402, RenderErrorCode.InsufficientCredits)]
        [InlineData(404, RenderErrorCode.ModelUnavailable)]
        [InlineData(408, RenderErrorCode.Timeout)]
        [InlineData(429, RenderErrorCode.RateLimited)]
        [InlineData(500, RenderErrorCode.ProviderError)]
        [InlineData(503, RenderErrorCode.ProviderError)]
        [InlineData(599, RenderErrorCode.ProviderError)]
        [InlineData(418, RenderErrorCode.Unknown)]
        [InlineData(302, RenderErrorCode.Unknown)]
        public void FromHttpStatus_MapsStatusToCode(int status, RenderErrorCode expected)
        {
            RenderException ex = RenderException.FromHttpStatus(status, "body");

            Assert.Equal(expected, ex.Code);
            Assert.Equal(status, ex.HttpStatus);
            Assert.Equal("body", ex.ProviderMessage);
        }

        [Theory]
        [InlineData(RenderErrorCode.RateLimited, true)]
        [InlineData(RenderErrorCode.Timeout, true)]
        [InlineData(RenderErrorCode.Network, true)]
        [InlineData(RenderErrorCode.ProviderError, true)]
        [InlineData(RenderErrorCode.InvalidRequest, false)]
        [InlineData(RenderErrorCode.AuthFailed, false)]
        [InlineData(RenderErrorCode.InsufficientCredits, false)]
        [InlineData(RenderErrorCode.ContentBlocked, false)]
        [InlineData(RenderErrorCode.ModelUnavailable, false)]
        [InlineData(RenderErrorCode.NoImageReturned, false)]
        [InlineData(RenderErrorCode.Unknown, false)]
        public void IsRetryable_OnlyForTransientCodes(RenderErrorCode code, bool expected)
        {
            Assert.Equal(expected, new RenderException(code, "x").IsRetryable);
        }

        [Fact]
        public void ProviderMessage_IsTruncated()
        {
            var ex = new RenderException(RenderErrorCode.ProviderError, "x", 500, new string('a', 5000));

            Assert.Equal(RenderException.MaxProviderMessageLength, ex.ProviderMessage!.Length);
        }

        [Fact]
        public void ProviderMessage_MayBeNull()
        {
            Assert.Null(RenderException.FromHttpStatus(500, null).ProviderMessage);
        }
    }
}
