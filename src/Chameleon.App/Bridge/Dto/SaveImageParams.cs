namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "files.saveImage".</summary>
    public sealed class SaveImageParams
    {
        /// <summary>Asset to save.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>File name offered to the user, without extension.</summary>
        public string? SuggestedName { get; set; }
    }
}
