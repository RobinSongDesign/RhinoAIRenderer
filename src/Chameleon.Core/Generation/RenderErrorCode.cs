namespace Chameleon.Core.Generation
{
    /// <summary>Why a generation failed. The UI localizes messages by this code.</summary>
    public enum RenderErrorCode
    {
        /// <summary>Bad parameters; retrying the same request cannot succeed.</summary>
        InvalidRequest,

        /// <summary>API key rejected, missing, or the account session expired.</summary>
        AuthFailed,

        /// <summary>The account does not have enough credits (cloud mode only).</summary>
        InsufficientCredits,

        /// <summary>Too many requests; retryable.</summary>
        RateLimited,

        /// <summary>The provider's safety filter rejected the prompt or the result.</summary>
        ContentBlocked,

        /// <summary>The model does not exist or is not available on this endpoint.</summary>
        ModelUnavailable,

        /// <summary>The request did not finish in time; retryable.</summary>
        Timeout,

        /// <summary>Connection failure; retryable.</summary>
        Network,

        /// <summary>Server-side failure or an unparseable response; retryable.</summary>
        ProviderError,

        /// <summary>The request succeeded but the response carried no image.</summary>
        NoImageReturned,

        /// <summary>Anything that could not be classified.</summary>
        Unknown
    }
}
