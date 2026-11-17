using System;
using System.IO;

namespace Chameleon.App.Server
{
    /// <summary>Serves front-end files from a directory on disk. Used by the development host.</summary>
    public sealed class DirectoryFileProvider : IStaticFileProvider
    {
        private readonly string _root;

        /// <summary>Creates a provider rooted at a directory.</summary>
        public DirectoryFileProvider(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory))
            {
                throw new ArgumentException("A root directory is required.", nameof(rootDirectory));
            }

            _root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        /// <inheritdoc />
        public Stream? Open(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (ArgumentException)
            {
                return null;
            }

            // Whatever the path looked like, the resolved file must still be inside the root.
            if (!candidate.StartsWith(_root, StringComparison.Ordinal) || !File.Exists(candidate))
            {
                return null;
            }

            return new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }
}
