namespace Chameleon.Core.Providers.Balance
{
    /// <summary>What is left on a provider key, in the provider's billing currency.</summary>
    public sealed class ProviderBalance
    {
        /// <summary>Creates a balance.</summary>
        /// <param name="total">Total granted, or null when the key has no limit.</param>
        /// <param name="used">Amount spent.</param>
        /// <param name="currency">ISO currency code of the amounts.</param>
        public ProviderBalance(decimal? total, decimal used, string currency)
        {
            Total = total;
            Used = used;
            Currency = currency;
        }

        /// <summary>Total granted; null when the key has no limit.</summary>
        public decimal? Total { get; }

        /// <summary>Amount spent so far.</summary>
        public decimal Used { get; }

        /// <summary>Amount left; null when the key has no limit. Never negative.</summary>
        public decimal? Remaining => Total.HasValue ? (Total.Value > Used ? Total.Value - Used : 0m) : (decimal?)null;

        /// <summary>ISO currency code of the amounts.</summary>
        public string Currency { get; }
    }
}
