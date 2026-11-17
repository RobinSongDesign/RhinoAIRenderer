namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "account.ledger".</summary>
    public sealed class LedgerParams
    {
        /// <summary>Cursor from the previous page; null for the first page.</summary>
        public string? Cursor { get; set; }
    }
}
