namespace Chameleon.Core.Generation
{
    /// <summary>Removes secrets from text before it reaches logs, exceptions or the UI.</summary>
    public static class Redactor
    {
        /// <summary>Text that replaces every occurrence of a secret.</summary>
        public const string Mask = "***";

        // Shorter values are too likely to match ordinary words and would mangle the message.
        private const int MinSecretLength = 4;

        /// <summary>Replaces every occurrence of each secret with <see cref="Mask"/>.</summary>
        /// <param name="text">Text to clean; null is returned unchanged.</param>
        /// <param name="secrets">Secrets to remove; null, empty and very short entries are ignored.</param>
        public static string? Redact(string? text, params string?[] secrets)
        {
            if (string.IsNullOrEmpty(text) || secrets == null)
            {
                return text;
            }

            string result = text!;
            foreach (string? secret in secrets)
            {
                if (secret != null && secret.Length >= MinSecretLength)
                {
                    result = result.Replace(secret, Mask);
                }
            }

            return result;
        }
    }
}
