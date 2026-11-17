namespace Chameleon.App.Hosting
{
    /// <summary>How a viewport should be captured.</summary>
    public sealed class CaptureOptions
    {
        /// <summary>Output width in pixels; null uses the viewport's own width.</summary>
        public int? Width { get; set; }

        /// <summary>Output height in pixels; null uses the viewport's own height.</summary>
        public int? Height { get; set; }
    }
}
