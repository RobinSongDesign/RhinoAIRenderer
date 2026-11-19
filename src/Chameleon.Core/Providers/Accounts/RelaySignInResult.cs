namespace Chameleon.Core.Providers.Accounts
{
    /// <summary>Outcome of a sign-in step: either a usable key, or a request for a two-factor code.</summary>
    public sealed class RelaySignInResult
    {
        private RelaySignInResult(string? apiKey, string? username, string? flowToken)
        {
            ApiKey = apiKey;
            Username = username;
            FlowToken = flowToken;
        }

        /// <summary>True when the account wants a two-factor code; pass <see cref="FlowToken"/> back with it.</summary>
        public bool NeedsTwoFactor => FlowToken != null;

        /// <summary>The key the plug-in generates with, once signed in.</summary>
        public string? ApiKey { get; }

        /// <summary>Account name, for showing who is signed in.</summary>
        public string? Username { get; }

        /// <summary>Opaque handle of a pending two-factor step.</summary>
        public string? FlowToken { get; }

        /// <summary>A completed sign-in.</summary>
        public static RelaySignInResult SignedIn(string apiKey, string username) => new RelaySignInResult(apiKey, username, null);

        /// <summary>A sign-in waiting for a two-factor code.</summary>
        public static RelaySignInResult TwoFactorRequired(string flowToken) => new RelaySignInResult(null, null, flowToken);
    }
}
