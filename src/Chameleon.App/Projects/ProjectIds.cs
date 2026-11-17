using System;
using System.Text.RegularExpressions;

namespace Chameleon.App.Projects
{
    /// <summary>Project id rules. Ids become directory names, so only a safe alphabet is accepted.</summary>
    public static class ProjectIds
    {
        /// <summary>Prefix of projects not yet bound to a saved Rhino document.</summary>
        public const string TemporaryPrefix = "unsaved-";

        private static readonly Regex Valid = new Regex("^[A-Za-z0-9_-]{1,100}$", RegexOptions.CultureInvariant);

        /// <summary>True when the id can be used as a directory name.</summary>
        public static bool IsValid(string? projectId)
        {
            return projectId != null && Valid.IsMatch(projectId);
        }

        /// <summary>True for a temporary project.</summary>
        public static bool IsTemporary(string projectId)
        {
            return projectId.StartsWith(TemporaryPrefix, StringComparison.Ordinal);
        }

        /// <summary>A new permanent project id.</summary>
        public static string NewPermanent()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>A new temporary project id.</summary>
        public static string NewTemporary()
        {
            return TemporaryPrefix + Guid.NewGuid().ToString("N");
        }

        /// <summary>Throws when the id is not usable.</summary>
        /// <exception cref="ArgumentException">The id is malformed.</exception>
        public static string Require(string? projectId)
        {
            return IsValid(projectId) ? projectId! : throw new ArgumentException("Malformed project id '" + projectId + "'.", nameof(projectId));
        }
    }
}
