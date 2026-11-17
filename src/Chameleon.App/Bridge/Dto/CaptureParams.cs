namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of the capture methods.</summary>
    public sealed class CaptureParams
    {
        /// <summary>Named view to capture ("rhino.captureNamedView" only).</summary>
        public string? Name { get; set; }

        /// <summary>Output width; null uses the viewport width.</summary>
        public int? Width { get; set; }

        /// <summary>Output height; null uses the viewport height.</summary>
        public int? Height { get; set; }
    }
}
