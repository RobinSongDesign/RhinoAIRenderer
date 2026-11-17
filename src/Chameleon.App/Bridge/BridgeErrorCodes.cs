namespace Chameleon.App.Bridge
{
    /// <summary>Error codes of the UI bridge; see docs/plan/bridge-protocol.md section 5.</summary>
    public static class BridgeErrorCodes
    {
        /// <summary>Bad parameters or an unknown method.</summary>
        public const string InvalidRequest = "invalid_request";

        /// <summary>Key rejected or the account session expired.</summary>
        public const string AuthFailed = "auth_failed";

        /// <summary>The selected backend has no key or is not signed in.</summary>
        public const string NotReady = "not_ready";

        /// <summary>Not enough credits.</summary>
        public const string InsufficientCredits = "insufficient_credits";

        /// <summary>Too many requests.</summary>
        public const string RateLimited = "rate_limited";

        /// <summary>Rejected by the provider's safety filter.</summary>
        public const string ContentBlocked = "content_blocked";

        /// <summary>The model does not exist or is unavailable.</summary>
        public const string ModelUnavailable = "model_unavailable";

        /// <summary>The provider did not answer in time.</summary>
        public const string Timeout = "timeout";

        /// <summary>Connection failure.</summary>
        public const string Network = "network";

        /// <summary>Provider-side failure or unparseable response.</summary>
        public const string ProviderError = "provider_error";

        /// <summary>The response carried no image.</summary>
        public const string NoImage = "no_image";

        /// <summary>The operation was cancelled.</summary>
        public const string Cancelled = "cancelled";

        /// <summary>Capture failed, no document, missing named view and similar.</summary>
        public const string RhinoError = "rhino_error";

        /// <summary>An asset, job or node does not exist.</summary>
        public const string NotFound = "not_found";

        /// <summary>Unexpected failure inside the host.</summary>
        public const string Internal = "internal";
    }
}
