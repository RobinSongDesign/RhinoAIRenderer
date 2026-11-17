using Chameleon.Core.Generation;
using Xunit;

namespace Chameleon.Core.Tests.Generation
{
    public class RedactorTests
    {
        [Fact]
        public void Redact_ReplacesEveryOccurrenceOfEverySecret()
        {
            string? result = Redactor.Redact("key=test-key-123 again test-key-123 and other-secret", "test-key-123", "other-secret");

            Assert.Equal("key=*** again *** and ***", result);
        }

        [Fact]
        public void Redact_IgnoresNullEmptyAndVeryShortSecrets()
        {
            Assert.Equal("a message", Redactor.Redact("a message", null, "", "a"));
        }

        [Fact]
        public void Redact_PassesThroughNullAndEmptyText()
        {
            Assert.Null(Redactor.Redact(null, "secret"));
            Assert.Equal(string.Empty, Redactor.Redact(string.Empty, "secret"));
        }
    }
}
