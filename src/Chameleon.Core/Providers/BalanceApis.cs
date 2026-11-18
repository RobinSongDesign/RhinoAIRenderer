namespace Chameleon.Core.Providers
{
    /// <summary>How a provider reports the remaining balance of a key, if it can.</summary>
    public static class BalanceApis
    {
        /// <summary>
        /// OpenAI's legacy dashboard endpoints as served by One API / new-api relays:
        /// <c>/v1/dashboard/billing/subscription</c> and <c>/v1/dashboard/billing/usage</c>.
        /// </summary>
        public const string OneApi = "oneapi";

        /// <summary>True for a value this build understands; null or empty means "no balance".</summary>
        public static bool IsKnown(string? balanceApi)
        {
            return balanceApi == OneApi;
        }
    }
}
