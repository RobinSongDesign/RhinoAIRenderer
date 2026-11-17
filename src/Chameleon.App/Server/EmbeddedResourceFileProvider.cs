using System;
using System.IO;
using System.Reflection;

namespace Chameleon.App.Server
{
    /// <summary>
    /// Serves front-end files embedded in an assembly. Resources are expected under logical names
    /// of the form "{prefix}{relative/path}", e.g. "ui/assets/app.js".
    /// </summary>
    public sealed class EmbeddedResourceFileProvider : IStaticFileProvider
    {
        private readonly Assembly _assembly;
        private readonly string _prefix;

        /// <summary>Creates a provider.</summary>
        /// <param name="assembly">Assembly holding the resources.</param>
        /// <param name="prefix">Logical-name prefix, including the trailing slash.</param>
        public EmbeddedResourceFileProvider(Assembly assembly, string prefix)
        {
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            _prefix = prefix ?? string.Empty;
        }

        /// <inheritdoc />
        public Stream? Open(string relativePath)
        {
            return string.IsNullOrEmpty(relativePath) ? null : _assembly.GetManifestResourceStream(_prefix + relativePath);
        }
    }
}
