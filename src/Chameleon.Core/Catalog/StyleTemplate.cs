namespace Chameleon.Core.Catalog
{
    /// <summary>A named prompt preset the user can pick instead of writing a prompt.</summary>
    public sealed class StyleTemplate
    {
        /// <summary>Identifier of the template that leaves the prompt untouched.</summary>
        public const string CustomId = "custom";

        /// <summary>Stable identifier.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Name shown in the style list.</summary>
        public LocalizedText Name { get; set; } = new LocalizedText();

        /// <summary>Prompt text that replaces the user's prompt when the template is picked.</summary>
        public string Prompt { get; set; } = string.Empty;
    }
}
