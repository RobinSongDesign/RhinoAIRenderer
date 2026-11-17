namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "account.redeem".</summary>
    public sealed class RedeemParams
    {
        /// <summary>The code as typed; spaces, hyphens and case do not matter.</summary>
        public string Code { get; set; } = string.Empty;
    }
}
