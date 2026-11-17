namespace Chameleon.Core.Providers
{
    /// <summary>How an API key is attached to a request.</summary>
    public static class AuthTypes
    {
        /// <summary>"x-goog-api-key" header, used by Google's own endpoint.</summary>
        public const string Goog = "goog";

        /// <summary>"Authorization: Bearer" header, used by relays and OpenAI-compatible endpoints.</summary>
        public const string Bearer = "bearer";
    }
}
