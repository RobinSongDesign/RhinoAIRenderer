using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Chameleon.DevHost
{
    /// <summary>Reads the text parts of a multipart request body (the fake gateway only needs "request").</summary>
    internal static class MultipartReader
    {
        public static async Task<Dictionary<string, string>> ReadAsMultipartAsync(this HttpContent content)
        {
            var parts = new Dictionary<string, string>();
            if (content is MultipartFormDataContent form)
            {
                foreach (HttpContent part in form)
                {
                    string? name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name != null && part.Headers.ContentType?.MediaType == "application/json")
                    {
                        parts[name] = await part.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }

            return parts;
        }
    }
}
