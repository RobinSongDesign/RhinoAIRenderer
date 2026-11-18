using System;

namespace Chameleon.Core.Providers
{
    /// <summary>
    /// Error codes relay gateways (One API, new-api and their forks) put in <c>error.code</c>,
    /// whatever wire format the request used. Relays answer an exhausted balance with HTTP 403,
    /// which on its own would read as a rejected key.
    /// </summary>
    public static class RelayErrorCodes
    {
        /// <summary>The account behind the key has no balance left.</summary>
        public const string InsufficientUserQuota = "insufficient_user_quota";

        /// <summary>The key's own spending limit is used up.</summary>
        public const string TokenQuotaExhausted = "pre_consume_token_quota_failed";

        /// <summary>True when the code means "out of credits": topping up helps, retrying does not.</summary>
        public static bool IsQuotaExhausted(string? code)
        {
            return string.Equals(code, InsufficientUserQuota, StringComparison.Ordinal) ||
                   string.Equals(code, TokenQuotaExhausted, StringComparison.Ordinal);
        }
    }
}
