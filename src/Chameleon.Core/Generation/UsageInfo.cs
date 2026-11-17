namespace Chameleon.Core.Generation
{
    /// <summary>Billing information reported by a backend for one generation.</summary>
    public sealed class UsageInfo
    {
        /// <summary>Creates usage information.</summary>
        public UsageInfo(int creditsCharged)
        {
            CreditsCharged = creditsCharged;
        }

        /// <summary>Credits deducted from the signed-in account.</summary>
        public int CreditsCharged { get; }
    }
}
