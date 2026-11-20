namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Result of "providers.balance": what is left on a provider key.</summary>
    public sealed class BalanceDto
    {
        /// <summary>Amount left; null when the key has no limit (sent as null, not left out).</summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Include)]
        public decimal? Remaining { get; set; }

        /// <summary>Amount spent.</summary>
        public decimal Used { get; set; }

        /// <summary>Total granted; null when the key has no limit (sent as null, not left out).</summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Include)]
        public decimal? Total { get; set; }

        /// <summary>ISO currency code of the amounts.</summary>
        public string Currency { get; set; } = "USD";
    }
}
