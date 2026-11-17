namespace Chameleon.App.Services
{
    /// <summary>
    /// Optional interface for backends that are not always usable (e.g. the account backend when
    /// signed out). Backends without it are considered ready.
    /// </summary>
    public interface IBackendStatus
    {
        /// <summary>True when the backend can accept generations now.</summary>
        bool IsReady { get; }

        /// <summary>The kind reported to the UI: see <c>BackendInfoDto</c> Kind constants.</summary>
        string Kind { get; }
    }
}
