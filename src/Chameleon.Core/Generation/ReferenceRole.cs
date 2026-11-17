namespace Chameleon.Core.Generation
{
    /// <summary>What a reference image is meant to contribute to the result.</summary>
    public enum ReferenceRole
    {
        /// <summary>Overall look: lighting, atmosphere, rendering style.</summary>
        Style,

        /// <summary>A material or texture to apply.</summary>
        Material,

        /// <summary>An object to place, or an annotated copy of the source that marks a region.</summary>
        Object,

        /// <summary>A previous result of the same scene that later views must stay consistent with.</summary>
        Consistency
    }
}
