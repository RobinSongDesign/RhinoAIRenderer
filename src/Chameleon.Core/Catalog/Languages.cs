using System;

namespace Chameleon.Core.Catalog
{
    /// <summary>Language codes used in settings, catalog texts and the UI.</summary>
    public static class Languages
    {
        /// <summary>Simplified Chinese.</summary>
        public const string Chinese = "zh";

        /// <summary>English.</summary>
        public const string English = "en";

        /// <summary>Maps any input to a supported code; unknown values become English.</summary>
        public static string Normalize(string? language)
        {
            return language != null && language.StartsWith(Chinese, StringComparison.OrdinalIgnoreCase)
                ? Chinese
                : English;
        }
    }
}
