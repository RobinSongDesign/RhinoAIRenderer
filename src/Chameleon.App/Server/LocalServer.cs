using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.Core.Imaging;

namespace Chameleon.App.Server
{
    /// <summary>
    /// Loopback HTTP server that connects the web UI to the host: static files, RPC, server-sent
    /// events and image transfer. See docs/plan/bridge-protocol.md section 1.1.
    /// </summary>
    /// <remarks>
    /// The server is reachable by any local process, so every request must prove it comes from our
    /// own page: it has to target the loopback host name, carry the random path token and, when a
    /// browser adds an Origin header, originate from this server (or the configured dev origin).
    /// </remarks>
    public sealed class LocalServer : IDisposable
    {
        /// <summary>Largest accepted RPC request body.</summary>
        public const int MaxRpcBodyBytes = 1024 * 1024;

        /// <summary>Largest accepted image upload.</summary>
        public const int MaxUploadBytes = 64 * 1024 * 1024;

        private const int PortAttempts = 5;
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly LocalServerOptions _options;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private HttpListener? _listener;
        private string _pathPrefix = string.Empty;
        private string _origin = string.Empty;
        private string _hostHeader = string.Empty;

        /// <summary>Creates a server; call <see cref="Start"/> to begin listening.</summary>
        public LocalServer(LocalServerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Port the server listens on. Valid after <see cref="Start"/>.</summary>
        public int Port { get; private set; }

        /// <summary>Address of the UI, including the secret token. Valid after <see cref="Start"/>.</summary>
        public string BaseUrl { get; private set; } = string.Empty;

        /// <summary>Starts listening on the loopback interface.</summary>
        /// <exception cref="InvalidOperationException">No port could be bound.</exception>
        public void Start()
        {
            if (_listener != null)
            {
                throw new InvalidOperationException("The server is already running.");
            }

            string token = string.IsNullOrEmpty(_options.Token) ? CreateToken() : _options.Token!;
            Exception? lastError = null;

            for (int attempt = 0; attempt < PortAttempts; attempt++)
            {
                int port = _options.Port ?? FindFreePort();
                var listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                try
                {
                    listener.Start();
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is SocketException)
                {
                    // Another process took the port between probing and binding; try another one.
                    lastError = ex;
                    listener.Close();
                    if (_options.Port.HasValue)
                    {
                        break;
                    }

                    continue;
                }

                _listener = listener;
                Port = port;
                _hostHeader = "127.0.0.1:" + port;
                _origin = "http://" + _hostHeader;
                _pathPrefix = "/" + token + "/";
                BaseUrl = _origin + _pathPrefix;
                Task.Run(AcceptLoopAsync);
                return;
            }

            throw new InvalidOperationException("The local server could not bind a loopback port.", lastError);
        }

        /// <summary>Stops listening and ends every open event stream.</summary>
        public void Dispose()
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            _stop.Cancel();
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed.
            }
        }

        // ── Accepting ──────────────────────────────────────────────────────────

        private async Task AcceptLoopAsync()
        {
            HttpListener listener = _listener!;
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    return; // the listener was stopped
                }

                // Requests are independent: one slow upload must not hold up the accept loop.
                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (!await RouteAsync(context).ConfigureAwait(false))
                {
                    Respond(context, HttpStatusCode.NotFound);
                }
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is IOException || ex is ObjectDisposedException)
            {
                // The client went away mid-request; nothing to report.
            }
            catch (OperationCanceledException)
            {
                // Server is shutting down.
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke("Local server request failed: " + ex.GetType().Name + ": " + ex.Message);
                try
                {
                    Respond(context, HttpStatusCode.InternalServerError);
                }
                catch (Exception)
                {
                    // The response was already started or the client is gone.
                }
            }
        }

        // Returns false when the request should be answered with 404.
        private async Task<bool> RouteAsync(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;

            // Guard 1: the Host header must be ours, which defeats DNS rebinding.
            if (!string.Equals(request.Headers["Host"], _hostHeader, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Guard 2: the secret path token.
            string path = Uri.UnescapeDataString(request.Url?.AbsolutePath ?? string.Empty);
            if (!path.StartsWith(_pathPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            // Guard 3: browsers attach Origin to cross-origin and to all non-GET requests.
            string? origin = request.Headers["Origin"];
            bool devOrigin = origin != null && _options.AllowedDevOrigin != null &&
                             string.Equals(origin, _options.AllowedDevOrigin, StringComparison.OrdinalIgnoreCase);
            if (origin != null && !devOrigin && !string.Equals(origin, _origin, StringComparison.OrdinalIgnoreCase))
            {
                Respond(context, HttpStatusCode.Forbidden);
                return true;
            }

            if (devOrigin)
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                context.Response.Headers["Vary"] = "Origin";
                if (request.HttpMethod == "OPTIONS")
                {
                    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                    context.Response.Headers["Access-Control-Allow-Headers"] = "content-type";
                    context.Response.Headers["Access-Control-Max-Age"] = "600";
                    Respond(context, HttpStatusCode.NoContent);
                    return true;
                }
            }

            string route = path.Substring(_pathPrefix.Length);
            string method = request.HttpMethod;

            if (route == "rpc")
            {
                return method == "POST" && await HandleRpcAsync(context).ConfigureAwait(false);
            }

            if (route == "events")
            {
                return method == "GET" && await HandleEventsAsync(context).ConfigureAwait(false);
            }

            if (route == "assets")
            {
                return method == "POST" && await HandleUploadAsync(context).ConfigureAwait(false);
            }

            if (route.StartsWith("assets/", StringComparison.Ordinal) && AssetIds.IsValid(route.Substring("assets/".Length)))
            {
                return method == "GET" && await HandleAssetAsync(context, route.Substring("assets/".Length)).ConfigureAwait(false);
            }

            return method == "GET" && await HandleStaticAsync(context, route).ConfigureAwait(false);
        }

        // ── Endpoints ──────────────────────────────────────────────────────────

        private async Task<bool> HandleRpcAsync(HttpListenerContext context)
        {
            byte[]? body = await ReadBodyAsync(context.Request, MaxRpcBodyBytes).ConfigureAwait(false);
            if (body == null)
            {
                Respond(context, HttpStatusCode.RequestEntityTooLarge);
                return true;
            }

            RpcRequest? request;
            try
            {
                request = BridgeJson.Deserialize<RpcRequest>(Utf8.GetString(body));
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Respond(context, HttpStatusCode.BadRequest);
                return true;
            }

            if (request == null)
            {
                Respond(context, HttpStatusCode.BadRequest);
                return true;
            }

            RpcResponse response = await _options.Router.DispatchAsync(request, _stop.Token).ConfigureAwait(false);
            await WriteAsync(context, "application/json; charset=utf-8", Utf8.GetBytes(BridgeJson.Serialize(response))).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> HandleEventsAsync(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            response.ContentType = "text/event-stream";
            response.Headers["Cache-Control"] = "no-cache";
            response.SendChunked = true;

            using (IEventSubscription subscription = _options.Events.Subscribe())
            {
                Stream output = response.OutputStream;
                await WriteChunkAsync(output, ": connected\n\n").ConfigureAwait(false);

                while (!_stop.IsCancellationRequested)
                {
                    BridgeEvent? next = await subscription.ReadAsync(KeepAliveInterval, _stop.Token).ConfigureAwait(false);

                    // A comment line keeps idle connections open and reveals clients that went away.
                    string frame = next == null
                        ? ": keep-alive\n\n"
                        : "event: " + next.Name + "\ndata: " + next.Json + "\n\n";
                    await WriteChunkAsync(output, frame).ConfigureAwait(false);
                }
            }

            response.Close();
            return true;
        }

        private async Task<bool> HandleUploadAsync(HttpListenerContext context)
        {
            byte[]? body = await ReadBodyAsync(context.Request, MaxUploadBytes).ConfigureAwait(false);
            if (body == null)
            {
                Respond(context, HttpStatusCode.RequestEntityTooLarge);
                return true;
            }

            // The declared content type is not trusted: the bytes themselves must be an image.
            ImageData? image = ImageData.TryFromBytes(body);
            if (image == null)
            {
                Respond(context, HttpStatusCode.UnsupportedMediaType);
                return true;
            }

            AssetRef asset = _options.Assets.Put(image);
            await WriteAsync(context, "application/json; charset=utf-8", Utf8.GetBytes(BridgeJson.Serialize(asset))).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> HandleAssetAsync(HttpListenerContext context, string assetId)
        {
            string etag = "\"" + assetId + "\"";
            if (context.Request.Headers["If-None-Match"] == etag)
            {
                context.Response.Headers["ETag"] = etag;
                Respond(context, HttpStatusCode.NotModified);
                return true;
            }

            ImageData? image = _options.Assets.TryGet(assetId);
            if (image == null)
            {
                return false;
            }

            // Content-addressed: the bytes behind an id can never change.
            context.Response.Headers["ETag"] = etag;
            context.Response.Headers["Cache-Control"] = "private, max-age=31536000, immutable";
            await WriteAsync(context, image.MimeType, image.Bytes).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> HandleStaticAsync(HttpListenerContext context, string route)
        {
            string relativePath = route.Length == 0 ? "index.html" : route;
            if (ContainsTraversal(relativePath))
            {
                return false;
            }

            Stream? stream = _options.StaticFiles.Open(relativePath);
            bool isClientRoute = Path.GetExtension(relativePath).Length == 0 &&
                                 !relativePath.StartsWith("assets/", StringComparison.Ordinal);
            if (stream == null && isClientRoute)
            {
                // Client-side routes resolve to the application shell.
                relativePath = "index.html";
                stream = _options.StaticFiles.Open(relativePath);
            }

            if (stream == null)
            {
                return false;
            }

            using (stream)
            {
                HttpListenerResponse response = context.Response;
                response.ContentType = MimeTypes.ForPath(relativePath);

                // Only fingerprinted build output is safe to cache; the shell must always be fresh.
                response.Headers["Cache-Control"] = relativePath.StartsWith("assets/", StringComparison.Ordinal)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";

                if (stream.CanSeek)
                {
                    response.ContentLength64 = stream.Length;
                }
                else
                {
                    response.SendChunked = true;
                }

                await stream.CopyToAsync(response.OutputStream, 81920, _stop.Token).ConfigureAwait(false);
                response.Close();
            }

            return true;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool ContainsTraversal(string relativePath)
        {
            if (relativePath.IndexOf('\\') >= 0 || relativePath.StartsWith("/", StringComparison.Ordinal))
            {
                return true;
            }

            foreach (string segment in relativePath.Split('/'))
            {
                if (segment == ".." || segment == ".")
                {
                    return true;
                }
            }

            return false;
        }

        // Returns null when the body exceeds the limit.
        private async Task<byte[]?> ReadBodyAsync(HttpListenerRequest request, int limit)
        {
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[81920];
                long total = 0;
                bool tooLarge = request.ContentLength64 > limit;
                int read;
                while ((read = await request.InputStream.ReadAsync(chunk, 0, chunk.Length, _stop.Token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > limit)
                    {
                        tooLarge = true;
                    }

                    if (!tooLarge)
                    {
                        buffer.Write(chunk, 0, read);
                    }
                    else if (total > (long)limit * 2)
                    {
                        // Keep reading a little so a well-behaved client receives the 413 instead of a
                        // reset connection, but never let a sender keep us busy indefinitely.
                        throw new IOException("Request body far exceeds the limit.");
                    }
                }

                return tooLarge ? null : buffer.ToArray();
            }
        }

        private async Task WriteAsync(HttpListenerContext context, string contentType, byte[] body)
        {
            HttpListenerResponse response = context.Response;
            response.ContentType = contentType;
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, 0, body.Length, _stop.Token).ConfigureAwait(false);
            response.Close();
        }

        private async Task WriteChunkAsync(Stream output, string text)
        {
            byte[] bytes = Utf8.GetBytes(text);
            await output.WriteAsync(bytes, 0, bytes.Length, _stop.Token).ConfigureAwait(false);
            await output.FlushAsync(_stop.Token).ConfigureAwait(false);
        }

        private static void Respond(HttpListenerContext context, HttpStatusCode status)
        {
            context.Response.StatusCode = (int)status;
            context.Response.ContentLength64 = 0;
            context.Response.Close();
        }

        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
