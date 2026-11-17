namespace Chameleon.Core.Providers
{
    /// <summary>Wire formats a provider endpoint can speak. One adapter exists per format.</summary>
    public static class ApiFormats
    {
        /// <summary>Gemini generateContent.</summary>
        public const string Gemini = "gemini";

        /// <summary>OpenAI Images API (edits endpoint).</summary>
        public const string OpenAI = "openai";
    }
}
