using System.IO;

namespace Chameleon.App.Server
{
    /// <summary>Source of the front-end files served by the local server.</summary>
    public interface IStaticFileProvider
    {
        /// <summary>Opens a file for reading.</summary>
        /// <param name="relativePath">Path relative to the site root, using '/' separators, e.g. "assets/app.js".</param>
        /// <returns>A readable stream the caller disposes, or null when the file does not exist.</returns>
        Stream? Open(string relativePath);
    }
}
