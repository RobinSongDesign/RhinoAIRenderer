namespace Chameleon.App.Hosting
{
    /// <summary>Parameters of a native "save file" dialog.</summary>
    public sealed class SaveFileRequest
    {
        /// <summary>File name offered to the user, without directory.</summary>
        public string SuggestedFileName { get; set; } = string.Empty;

        /// <summary>Extension of the file being saved, without the dot (e.g. "png").</summary>
        public string Extension { get; set; } = "png";
    }
}
