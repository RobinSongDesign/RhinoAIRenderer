using System;

namespace Chameleon.Core.Generation
{
    /// <summary>
    /// A user-facing generation failure. Cancellation is never reported with this type;
    /// it surfaces as <see cref="OperationCanceledException"/>.
    /// </summary>
    public sealed class RenderException : Exception
    {
        /// <summary>Longest provider message kept on the exception.</summary>
        public const int MaxProviderMessageLength = 2048;

        /// <summary>Creates a failure.</summary>
        /// <param name="code">Classification of the failure.</param>
        /// <param name="message">English message for logs.</param>
        /// <param name="httpStatus">HTTP status returned by the provider, if any.</param>
        /// <param name="providerMessage">Raw provider text; truncated. Must already be redacted.</param>
        /// <param name="innerException">Underlying exception, if any.</param>
        public RenderException(
            RenderErrorCode code,
            string message,
            int? httpStatus = null,
            string? providerMessage = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            HttpStatus = httpStatus;
            ProviderMessage = Truncate(providerMessage);
        }

        /// <summary>Classification of the failure.</summary>
        public RenderErrorCode Code { get; }

        /// <summary>HTTP status returned by the provider, if the failure came from a response.</summary>
        public int? HttpStatus { get; }

        /// <summary>Raw provider text, truncated to <see cref="MaxProviderMessageLength"/> characters.</summary>
        public string? ProviderMessage { get; }

        /// <summary>Delay requested by the provider before retrying, if it sent one.</summary>
        public TimeSpan? RetryAfter { get; set; }

        /// <summary>True when sending the same request again may succeed.</summary>
        public bool IsRetryable =>
            Code == RenderErrorCode.RateLimited ||
            Code == RenderErrorCode.Timeout ||
            Code == RenderErrorCode.Network ||
            Code == RenderErrorCode.ProviderError;

        /// <summary>Maps an HTTP status code to an error code.</summary>
        public static RenderErrorCode CodeFromHttpStatus(int status)
        {
            switch (status)
            {
                case 400: return RenderErrorCode.InvalidRequest;
                case 401:
                case 403: return RenderErrorCode.AuthFailed;
                case 402: return RenderErrorCode.InsufficientCredits;
                case 404: return RenderErrorCode.ModelUnavailable;
                case 408: return RenderErrorCode.Timeout;
                case 429: return RenderErrorCode.RateLimited;
                default:
                    return status >= 500 && status <= 599 ? RenderErrorCode.ProviderError : RenderErrorCode.Unknown;
            }
        }

        /// <summary>Builds a failure from a non-success HTTP response.</summary>
        /// <param name="status">HTTP status code.</param>
        /// <param name="body">Response body, already redacted; may be null.</param>
        public static RenderException FromHttpStatus(int status, string? body)
        {
            return new RenderException(
                CodeFromHttpStatus(status),
                "The provider returned HTTP " + status + ".",
                status,
                body);
        }

        private static string? Truncate(string? text)
        {
            if (text == null || text.Length <= MaxProviderMessageLength)
            {
                return text;
            }

            return text.Substring(0, MaxProviderMessageLength);
        }
    }
}
