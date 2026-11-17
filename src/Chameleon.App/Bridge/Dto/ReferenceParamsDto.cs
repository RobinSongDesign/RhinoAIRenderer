namespace Chameleon.App.Bridge.Dto
{
    /// <summary>A reference image of a generation request.</summary>
    public sealed class ReferenceParamsDto
    {
        /// <summary>Asset id of the image.</summary>
        public string AssetId { get; set; } = string.Empty;

        /// <summary>"style", "material", "object" or "consistency".</summary>
        public string Role { get; set; } = "style";

        /// <summary>Optional user note.</summary>
        public string? Note { get; set; }
    }
}
