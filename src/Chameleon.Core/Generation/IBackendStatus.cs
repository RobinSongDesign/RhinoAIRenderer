namespace Chameleon.Core.Generation
{
    /// <summary>
    /// Optional interface for backends that are not always usable, such as the account backend
    /// while signed out. Backends without it are considered ready.
    /// </summary>
    public interface IBackendStatus
    {
        /// <summary>True when the backend can accept generations now.</summary>
        bool IsReady { get; }

        /// <summary>Kind reported to the UI: "direct", "cloud" or "dev".</summary>
        string Kind { get; }
    }
}
