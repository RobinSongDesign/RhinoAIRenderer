using System;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Cloud
{
    /// <summary>The gateway answered with an error (or could not be reached).</summary>
    public sealed class CloudApiException : Exception
    {
        /// <summary>Code used when the gateway could not be reached at all.</summary>
        public const string NetworkCode = "network";

        /// <summary>Creates the exception.</summary>
        public CloudApiException(int status, string code, string message, string? detail = null, TimeSpan? retryAfter = null, Exception? inner = null)
            : base(message, inner)
        {
            Status = status;
            Code = code ?? string.Empty;
            Detail = detail;
            RetryAfter = retryAfter;
        }

        /// <summary>HTTP status; 0 when no response was received.</summary>
        public int Status { get; }

        /// <summary>Error code from the body (see the contract's ErrorCode), or empty.</summary>
        public string Code { get; }

        /// <summary>Optional detail from the body.</summary>
        public string? Detail { get; }

        /// <summary>Delay requested by the gateway, if any.</summary>
        public TimeSpan? RetryAfter { get; }

        /// <summary>Translates the failure into the generation error model.</summary>
        public RenderException ToRenderException()
        {
            var exception = new RenderException(ToRenderErrorCode(Code, Status), Message, Status == 0 ? (int?)null : Status, Detail ?? Message, this)
            {
                RetryAfter = RetryAfter
            };
            return exception;
        }

        /// <summary>Maps a gateway error code to the core error code; unknown codes go by HTTP status.</summary>
        public static RenderErrorCode ToRenderErrorCode(string? code, int status)
        {
            switch (code)
            {
                case "invalid_request":
                case "conflict":
                    return RenderErrorCode.InvalidRequest;
                case "auth_failed":
                    return RenderErrorCode.AuthFailed;
                case "insufficient_credits":
                    return RenderErrorCode.InsufficientCredits;
                case "rate_limited":
                    return RenderErrorCode.RateLimited;
                case "content_blocked":
                    return RenderErrorCode.ContentBlocked;
                case "model_unavailable":
                    return RenderErrorCode.ModelUnavailable;
                case "timeout":
                    return RenderErrorCode.Timeout;
                case NetworkCode:
                    return RenderErrorCode.Network;
                case "provider_error":
                    return RenderErrorCode.ProviderError;
                case "no_image":
                    return RenderErrorCode.NoImageReturned;
                default:
                    return status == 0 ? RenderErrorCode.Network : RenderException.CodeFromHttpStatus(status);
            }
        }
    }
}
