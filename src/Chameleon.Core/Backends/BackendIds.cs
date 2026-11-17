using System;

namespace Chameleon.Core.Backends
{
    /// <summary>Builds and parses backend identifiers.</summary>
    public static class BackendIds
    {
        /// <summary>Identifier of the signed-in account backend.</summary>
        public const string Cloud = "cloud";

        private const string DirectPrefix = "direct:";

        /// <summary>Identifier of the bring-your-own-key backend for a provider.</summary>
        public static string Direct(string profileId)
        {
            return DirectPrefix + profileId;
        }

        /// <summary>Extracts the provider id from a direct backend id.</summary>
        /// <returns>True when <paramref name="backendId"/> is a direct backend id.</returns>
        public static bool TryGetProfileId(string? backendId, out string profileId)
        {
            if (backendId != null &&
                backendId.StartsWith(DirectPrefix, StringComparison.Ordinal) &&
                backendId.Length > DirectPrefix.Length)
            {
                profileId = backendId.Substring(DirectPrefix.Length);
                return true;
            }

            profileId = string.Empty;
            return false;
        }
    }
}
