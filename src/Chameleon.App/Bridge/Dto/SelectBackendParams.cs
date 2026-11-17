namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "backend.select".</summary>
    public sealed class SelectBackendParams
    {
        /// <summary>Backend to activate.</summary>
        public string BackendId { get; set; } = string.Empty;

        /// <summary>Model to activate; null keeps the backend's last or default model.</summary>
        public string? ModelId { get; set; }
    }
}
