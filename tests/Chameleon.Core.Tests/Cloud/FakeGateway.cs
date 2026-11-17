using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Tests.Cloud
{
    /// <summary>
    /// In-memory implementation of contracts/cloud-api.openapi.yaml, with switches to script
    /// failures. Every request is recorded.
    /// </summary>
    internal sealed class FakeGateway : HttpMessageHandler
    {
        public static readonly Uri BaseUrl = new Uri("https://gateway.test/");

        private readonly object _gate = new object();
        private int _tokenCounter;

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public List<string> RequestBodies { get; } = new List<string>();

        public int Credits { get; set; } = 20;

        public int Price { get; set; } = 4;

        /// <summary>Polls of /v1/auth/token answered "authorization_pending" before success.</summary>
        public int PendingPolls { get; set; }

        /// <summary>Error code returned by /v1/auth/token instead of tokens (e.g. "expired_token").</summary>
        public string? TokenError { get; set; }

        /// <summary>Statuses returned, in order, before the generation is accepted.</summary>
        public Queue<int> SubmitFailures { get; } = new Queue<int>();

        /// <summary>Polls answered "running" before the generation finishes.</summary>
        public int RunningPolls { get; set; }

        /// <summary>When set, generations end in this error instead of succeeding.</summary>
        public string? GenerationError { get; set; }

        /// <summary>When true, generation polls never finish (to test cancellation).</summary>
        public bool NeverFinish { get; set; }

        public bool RefreshRejected { get; set; }

        public string? CurrentAccessToken { get; private set; }

        public string? CurrentRefreshToken { get; private set; }

        public int RefreshCount { get; private set; }

        public int CatalogFetches { get; private set; }

        public ConcurrentDictionary<string, string> GenerationStatus { get; } = new ConcurrentDictionary<string, string>();

        public Dictionary<string, string> IdempotentSubmissions { get; } = new Dictionary<string, string>();

        public byte[] ResultImage { get; set; } = TestImages.Webp().Bytes;

        public string IssueTokens()
        {
            lock (_gate)
            {
                _tokenCounter++;
                CurrentAccessToken = "access-" + _tokenCounter;
                CurrentRefreshToken = "refresh-" + _tokenCounter;
                return CurrentAccessToken;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            lock (_gate)
            {
                Requests.Add(request);
                RequestBodies.Add(body);
            }

            string path = request.RequestUri!.AbsolutePath;
            string method = request.Method.Method;

            if (path == "/v1/auth/device" && method == "POST")
            {
                return Json(200, new JObject
                {
                    ["deviceCode"] = "device-secret", ["userCode"] = "WDJB-MJHT", ["verificationUrl"] = "https://gateway.test/device",
                    ["verificationUrlComplete"] = "https://gateway.test/device?code=WDJB-MJHT", ["expiresIn"] = 600, ["interval"] = 3
                });
            }

            if (path == "/v1/auth/token" && method == "POST")
            {
                if (TokenError != null)
                {
                    return Error(400, TokenError);
                }

                if (PendingPolls > 0)
                {
                    PendingPolls--;
                    return Error(400, "authorization_pending");
                }

                return Tokens();
            }

            if (path == "/v1/auth/refresh" && method == "POST")
            {
                RefreshCount++;
                string presented = (string)JObject.Parse(body)["refreshToken"]!;
                if (RefreshRejected || presented != CurrentRefreshToken)
                {
                    return Error(401, "auth_failed");
                }

                await Task.Delay(20, cancellationToken);
                return Tokens();
            }

            // Everything below needs a valid access token.
            if (request.Headers.Authorization?.Parameter != CurrentAccessToken || CurrentAccessToken == null)
            {
                return Error(401, "auth_failed");
            }

            if (path == "/v1/auth/logout")
            {
                CurrentAccessToken = null;
                CurrentRefreshToken = null;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/v1/me")
            {
                return Json(200, User());
            }

            if (path == "/v1/redeem")
            {
                string code = (string)JObject.Parse(body)["code"]!;
                if (code == "USEDUSEDUSEDUSED")
                {
                    return Error(409, "conflict");
                }

                if (code != "TESTTESTTESTTEST")
                {
                    return Error(404, "not_found");
                }

                Credits += 50;
                return Json(200, new JObject { ["creditsAdded"] = 50, ["credits"] = Credits });
            }

            if (path == "/v1/ledger")
            {
                return Json(200, new JObject
                {
                    ["items"] = new JArray(new JObject { ["id"] = 1, ["delta"] = 50, ["balanceAfter"] = 70, ["type"] = "redeem", ["createdAt"] = "2026-11-17T00:00:00Z" }),
                    ["nextCursor"] = null
                });
            }

            if (path == "/v1/catalog")
            {
                CatalogFetches++;
                if (request.Headers.TryGetValues("If-None-Match", out var etags) && etags.Contains("\"v1\""))
                {
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }

                var response = Json(200, Catalog());
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
                return response;
            }

            if (path == "/v1/generations" && method == "POST")
            {
                if (SubmitFailures.Count > 0)
                {
                    return Error(SubmitFailures.Dequeue(), "provider_error");
                }

                string key = request.Headers.GetValues("Idempotency-Key").Single();
                lock (_gate)
                {
                    if (IdempotentSubmissions.TryGetValue(key, out string? existing))
                    {
                        return Json(202, new JObject { ["id"] = existing, ["status"] = "queued", ["creditsReserved"] = Price });
                    }

                    if (Credits < Price)
                    {
                        return Error(402, "insufficient_credits");
                    }

                    Credits -= Price;
                    string id = Guid.NewGuid().ToString();
                    IdempotentSubmissions[key] = id;
                    GenerationStatus[id] = "queued";
                    return Json(202, new JObject { ["id"] = id, ["status"] = "queued", ["creditsReserved"] = Price });
                }
            }

            if (path.StartsWith("/v1/generations/", StringComparison.Ordinal))
            {
                string[] parts = path.Split('/');
                string id = parts[3];
                if (!GenerationStatus.ContainsKey(id))
                {
                    return Error(404, "not_found");
                }

                if (method == "DELETE")
                {
                    GenerationStatus[id] = "cancelled";
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                if (parts.Length == 6 && parts[4] == "images")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ResultImage) };
                }

                return Json(200, Generation(id));
            }

            return Error(404, "not_found");
        }

        private JObject Generation(string id)
        {
            string status = GenerationStatus[id];
            if (status == "queued" || status == "running")
            {
                if (NeverFinish || RunningPolls > 0)
                {
                    RunningPolls = Math.Max(0, RunningPolls - 1);
                    GenerationStatus[id] = "running";
                }
                else
                {
                    GenerationStatus[id] = GenerationError == null ? "succeeded" : "failed";
                    if (GenerationError != null)
                    {
                        Credits += Price; // refunded
                    }
                }
            }

            status = GenerationStatus[id];
            var json = new JObject { ["id"] = id, ["status"] = status, ["createdAt"] = "2026-11-17T00:00:00Z", ["creditsCharged"] = status == "succeeded" ? Price : 0 };
            if (status == "succeeded")
            {
                json["images"] = new JArray(new JObject { ["index"] = 0, ["url"] = "/v1/generations/" + id + "/images/0", ["mimeType"] = "image/webp", ["width"] = 50, ["height"] = 70 });
                json["text"] = "here you go";
            }

            if (status == "failed")
            {
                json["error"] = new JObject { ["code"] = GenerationError, ["message"] = "upstream failed", ["detail"] = "provider said no" };
            }

            return json;
        }

        private JObject User()
        {
            return new JObject { ["id"] = "11111111-1111-1111-1111-111111111111", ["email"] = "user@example.com", ["credits"] = Credits, ["createdAt"] = "2026-01-01T00:00:00Z" };
        }

        private static JObject Catalog()
        {
            var caps = new JObject
            {
                ["supportsMask"] = false, ["maxInputImages"] = 6, ["imageSizes"] = new JArray("1K", "2K", "4K"),
                ["aspectRatios"] = new JArray("1:1", "16:9"), ["maxConcurrency"] = 4
            };
            return new JObject
            {
                ["version"] = "v1",
                ["models"] = new JArray(new JObject { ["id"] = "cloud-pro", ["displayName"] = "Cloud Pro", ["capabilities"] = caps, ["prices"] = new JObject { ["1K"] = 4, ["2K"] = 8 } }),
                ["styleTemplates"] = new JArray(),
                ["defaultSystemPrompt"] = "server prompt",
                ["imageSizes"] = new JArray("1K", "2K", "4K"),
                ["aspectRatios"] = new JArray("1:1", "16:9"),
                ["defaultImageSize"] = "1K",
                ["limits"] = new JObject { ["maxConcurrency"] = 3, ["maxUploadBytes"] = 41943040 }
            };
        }

        private HttpResponseMessage Tokens()
        {
            IssueTokens();
            return Json(200, new JObject
            {
                ["accessToken"] = CurrentAccessToken, ["expiresIn"] = 900, ["refreshToken"] = CurrentRefreshToken, ["user"] = User()
            });
        }

        private static HttpResponseMessage Json(int status, JToken body)
        {
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };
        }

        private static HttpResponseMessage Error(int status, string code)
        {
            return Json(status, new JObject { ["error"] = new JObject { ["code"] = code, ["message"] = "fake " + code } });
        }
    }
}
