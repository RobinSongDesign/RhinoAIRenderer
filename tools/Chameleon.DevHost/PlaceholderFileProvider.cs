using System.IO;
using System.Text;
using Chameleon.App.Server;

namespace Chameleon.DevHost
{
    /// <summary>Serves a single explanatory page when no front-end build was given.</summary>
    public sealed class PlaceholderFileProvider : IStaticFileProvider
    {
        private const string Page =
            "<!doctype html><meta charset=\"utf-8\"><title>Chameleon DevHost</title>" +
            "<body style=\"font:15px sans-serif;margin:40px\"><h1>Chameleon DevHost</h1>" +
            "<p>The bridge is running, but no front-end build was provided.</p>" +
            "<p>Build the UI (<code>pnpm --dir src/Chameleon.UI build</code>) and pass <code>--ui src/Chameleon.UI/dist</code>, " +
            "or run the Vite dev server against this host.</p></body>";

        /// <inheritdoc />
        public Stream? Open(string relativePath)
        {
            return relativePath == "index.html" ? new MemoryStream(Encoding.UTF8.GetBytes(Page)) : null;
        }
    }
}
