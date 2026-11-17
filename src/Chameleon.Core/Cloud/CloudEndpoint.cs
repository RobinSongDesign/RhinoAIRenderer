using System;

namespace Chameleon.Core.Cloud
{
    /// <summary>Rules for the gateway address.</summary>
    public static class CloudEndpoint
    {
        /// <summary>
        /// Address used when settings do not override it. Null until the gateway is deployed
        /// (task P4-T10); account mode is not offered while there is no address.
        /// </summary>
        public const string? DefaultBaseUrl = null;

        /// <summary>
        /// Parses a gateway address. Only https is accepted, except loopback addresses used for
        /// local development, because tokens travel with every request.
        /// </summary>
        /// <returns>The normalized address ending in "/", or null when it is not acceptable.</returns>
        public static Uri? TryParse(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl!.Trim(), UriKind.Absolute, out Uri? uri))
            {
                return null;
            }

            bool loopback = uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]";
            if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && loopback))
            {
                return null;
            }

            string text = uri.GetLeftPart(UriPartial.Path);
            return new Uri(text.EndsWith("/", StringComparison.Ordinal) ? text : text + "/");
        }
    }
}
