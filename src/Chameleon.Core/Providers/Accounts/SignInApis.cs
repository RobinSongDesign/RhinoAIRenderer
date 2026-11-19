namespace Chameleon.Core.Providers.Accounts
{
    /// <summary>How users of a provider can sign in from the plug-in instead of pasting a key.</summary>
    public static class SignInApis
    {
        /// <summary>
        /// A new-api gateway: username and password (plus a two-factor code when enabled) on the
        /// gateway's own login API; the plug-in then uses a token of that account.
        /// </summary>
        public const string NewApi = "newapi";

        /// <summary>True for a value this build understands; null or empty means "keys only".</summary>
        public static bool IsKnown(string? signInApi)
        {
            return signInApi == NewApi;
        }
    }
}
