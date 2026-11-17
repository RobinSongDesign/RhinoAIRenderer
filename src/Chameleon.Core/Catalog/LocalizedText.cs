namespace Chameleon.Core.Catalog
{
    /// <summary>A short text available in every supported UI language.</summary>
    public sealed class LocalizedText
    {
        /// <summary>Creates an empty text (for deserialization).</summary>
        public LocalizedText()
        {
        }

        /// <summary>Creates a text with both translations.</summary>
        public LocalizedText(string zh, string en)
        {
            Zh = zh;
            En = en;
        }

        /// <summary>Simplified Chinese text.</summary>
        public string Zh { get; set; } = string.Empty;

        /// <summary>English text.</summary>
        public string En { get; set; } = string.Empty;

        /// <summary>Returns the text for a language, falling back to the other one when empty.</summary>
        public string Get(string? language)
        {
            bool chinese = Languages.Normalize(language) == Languages.Chinese;
            string primary = chinese ? Zh : En;
            string fallback = chinese ? En : Zh;
            return string.IsNullOrEmpty(primary) ? fallback : primary;
        }
    }
}
