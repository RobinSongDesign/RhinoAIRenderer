using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChameleonSpike
{
    /// <summary>
    /// Loopback HTTP server exercising everything the real bridge will need: static page, JSON
    /// round trip, server-sent events, a large image download and a large binary upload.
    /// </summary>
    public sealed class SpikeServer : IDisposable
    {
        /// <summary>Size of the test image served at big.png.</summary>
        public const int BigImageWidth = 3840;

        /// <summary>Height of the test image served at big.png.</summary>
        public const int BigImageHeight = 2160;

        private readonly HttpListener _listener = new HttpListener();
        private readonly string _pageHtml;
        private readonly Func<string, string, string> _handleCommand;
        private readonly Action<string> _handleReport;
        private readonly Lazy<byte[]> _bigImage = new Lazy<byte[]>(() => PngWriter.Gradient(BigImageWidth, BigImageHeight));
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        /// <summary>Starts listening on a free loopback port.</summary>
        /// <param name="pageHtml">The page served at the root.</param>
        /// <param name="handleCommand">Runs a host command (name, argument) and returns its result text.</param>
        /// <param name="handleReport">Receives check results posted by the page as JSON.</param>
        public SpikeServer(string pageHtml, Func<string, string, string> handleCommand, Action<string> handleReport)
        {
            _pageHtml = pageHtml;
            _handleCommand = handleCommand;
            _handleReport = handleReport;

            Port = FindFreePort();
            Token = CreateToken();
            _listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
            _listener.Start();
            Task.Run(AcceptLoopAsync);
        }

        /// <summary>Port the server listens on.</summary>
        public int Port { get; }

        /// <summary>Secret path prefix; requests without it are answered with 404.</summary>
        public string Token { get; }

        /// <summary>Address of the page, including the token.</summary>
        public string BaseUrl => "http://127.0.0.1:" + Port + "/" + Token + "/";

        /// <inheritdoc />
        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static string CreateToken()
        {
            var bytes = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return; // listener stopped
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                string prefix = "/" + Token + "/";
                string path = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                string route = path.Substring(prefix.Length);
                string method = context.Request.HttpMethod;

                if (method == "GET" && (route.Length == 0 || route == "index.html"))
                {
                    await WriteAsync(context, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(_pageHtml)).ConfigureAwait(false);
                }
                else if (method == "GET" && route == "big.png")
                {
                    await WriteAsync(context, "image/png", _bigImage.Value).ConfigureAwait(false);
                }
                else if (method == "GET" && route == "events")
                {
                    await WriteEventsAsync(context).ConfigureAwait(false);
                }
                else if (method == "POST" && route == "rpc")
                {
                    string body = await ReadTextAsync(context).ConfigureAwait(false);
                    int split = body.IndexOf('\n');
                    string name = split < 0 ? body : body.Substring(0, split);
                    string argument = split < 0 ? string.Empty : body.Substring(split + 1);
                    string result = _handleCommand(name.Trim(), argument);
                    await WriteAsync(context, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(result)).ConfigureAwait(false);
                }
                else if (method == "POST" && route == "report")
                {
                    _handleReport(await ReadTextAsync(context).ConfigureAwait(false));
                    await WriteAsync(context, "text/plain", Encoding.UTF8.GetBytes("ok")).ConfigureAwait(false);
                }
                else if (method == "POST" && route == "upload")
                {
                    string summary = await ReadUploadAsync(context).ConfigureAwait(false);
                    await WriteAsync(context, "text/plain", Encoding.UTF8.GetBytes(summary)).ConfigureAwait(false);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
            catch (Exception)
            {
                try
                {
                    context.Response.Abort();
                }
                catch (Exception)
                {
                    // The client is already gone.
                }
            }
        }

        private async Task WriteEventsAsync(HttpListenerContext context)
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.SendChunked = true;
            Stream output = context.Response.OutputStream;

            for (int i = 1; i <= 3 && !_stop.IsCancellationRequested; i++)
            {
                byte[] message = Encoding.UTF8.GetBytes("event: tick\ndata: " + i + "\n\n");
                await output.WriteAsync(message, 0, message.Length).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                await Task.Delay(300).ConfigureAwait(false);
            }

            context.Response.Close();
        }

        // The page uploads bytes following the pattern b[i] = i % 251, so the server can verify
        // the content without the page needing a hash implementation.
        private static async Task<string> ReadUploadAsync(HttpListenerContext context)
        {
            var buffer = new byte[81920];
            long total = 0;
            bool patternOk = true;
            int read;
            while ((read = await context.Request.InputStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)((total + i) % 251))
                    {
                        patternOk = false;
                    }
                }

                total += read;
            }

            return total + "|" + (patternOk ? "ok" : "corrupt");
        }

        private static async Task<string> ReadTextAsync(HttpListenerContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        private static async Task WriteAsync(HttpListenerContext context, string contentType, byte[] body)
        {
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            context.Response.Close();
        }
    }
}
