namespace Chameleon.Core.Sessions
{
    /// <summary>How an image entered the edit history.</summary>
    public enum NodeKind
    {
        /// <summary>A capture of a Rhino viewport.</summary>
        Capture,

        /// <summary>A generation from a prompt (and optionally a source image).</summary>
        Generation,

        /// <summary>A change to part of an image, such as an inpainting or a quick action.</summary>
        Edit,

        /// <summary>An image file the user brought in.</summary>
        Import
    }
}
