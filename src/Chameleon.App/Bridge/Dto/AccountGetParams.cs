namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "account.get".</summary>
    public sealed class AccountGetParams
    {
        /// <summary>True to ask the gateway for the current balance instead of the last known one.</summary>
        public bool Refresh { get; set; }
    }
}
