using System;
using System.Collections.Generic;
using System.IO;

namespace Chameleon.App.Server
{
    /// <summary>Content types of the file kinds a front-end build produces.</summary>
    public static class MimeTypes
    {
        private static readonly Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".js", "text/javascript; charset=utf-8" },
            { ".mjs", "text/javascript; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".map", "application/json; charset=utf-8" },
            { ".svg", "image/svg+xml" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".webp", "image/webp" },
            { ".ico", "image/x-icon" },
            { ".woff", "font/woff" },
            { ".woff2", "font/woff2" },
            { ".txt", "text/plain; charset=utf-8" }
        };

        /// <summary>Returns the content type for a file name; unknown extensions are served as binary.</summary>
        public static string ForPath(string path)
        {
            return ByExtension.TryGetValue(Path.GetExtension(path), out string? type) ? type : "application/octet-stream";
        }
    }
}
