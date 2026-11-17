namespace Chameleon.Core.Prompts
{
    /// <summary>
    /// Every fixed sentence that is sent to a model. Kept in one place so wording changes are
    /// deliberate and reviewable. The style and consistency texts are verbatim from the WPF version.
    /// </summary>
    public static class PromptTexts
    {
        /// <summary>Separator between prompt sections.</summary>
        public const string SectionSeparator = "\n\n";

        /// <summary>Appended when at least one style reference is attached.</summary>
        public const string StyleReference =
            "A style reference image is also provided — match its lighting, atmosphere, and visual style.";

        /// <summary>Appended when at least one material reference is attached.</summary>
        public const string MaterialReference =
            "A material reference image is also provided — apply its material, texture and color to the relevant surfaces.";

        /// <summary>Appended when at least one object reference is attached.</summary>
        public const string ObjectReference =
            "An object reference image is also provided — use it as the reference for the object described in the prompt.";

        /// <summary>Appended when previous results are attached to keep a series of views consistent.</summary>
        public const string Consistency =
            "Using the provided reference rendered image(s) as a strict style guide: " +
            "maintain identical lighting direction, shadow angles, material textures, " +
            "color palette, atmospheric mood, and positions of any people or objects. " +
            "Only change the camera position and angle to match the new architectural viewport shown. " +
            "The scene contents, lighting setup, and visual style must remain completely consistent across all views.";

        /// <summary>Heading of the list of user notes about reference images.</summary>
        public const string ReferenceNotesHeading =
            "Notes on the reference images, in the order they are attached after the main image:";
    }
}
