namespace Chameleon.Core.Generation
{
    /// <summary>A model offered by a backend.</summary>
    public sealed class ModelInfo
    {
        /// <summary>Identifier sent to the provider, e.g. "gemini-3-pro-image-preview".</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Name shown to the user; falls back to <see cref="Id"/> when empty.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>What this model supports.</summary>
        public BackendCapabilities Capabilities { get; set; } = new BackendCapabilities();
    }
}
