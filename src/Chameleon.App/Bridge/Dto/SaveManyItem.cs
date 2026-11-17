namespace Chameleon.App.Bridge.Dto
{
    /// <summary>One file of "files.saveMany".</summary>
    public sealed class SaveManyItem
    {
        /// <summary>Asset to save.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>File name without extension.</summary>
        public string FileName { get; set; } = string.Empty;
    }
}
