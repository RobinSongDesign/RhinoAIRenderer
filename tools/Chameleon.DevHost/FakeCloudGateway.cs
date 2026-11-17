using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Imaging;
using Newtonsoft.Json.Linq;

namespace Chameleon.DevHost
{
    /// <summary>
    /// In-process stand-in for the Chameleon gateway (contracts/cloud-api.openapi.yaml) so account
    /// mode can be developed and tested offline. One user; sign-ins are approved automatically after
    /// a short delay; the redeem code TEST-TEST-TEST-TEST adds 100 credits (any number of times).
    /// Prompts script failures the same way as <see cref="FakeBackend"/>.
    /// </summary>
    public sealed class FakeCloudGateway : HttpMessageHandler
    {
        /// <summary>Address the development host gives account mode.</summary>
        public static readonly Uri BaseUrl = new Uri("https://fake-cloud.invalid/");

        /// <summary>The redeem code that always works.</summary>
        public const string RedeemCode = "TESTTESTTESTTEST";

        private static readonly Regex FailDirective = new Regex(@"#fail:([a-z_]+)", RegexOptions.Compiled);

        private readonly SampleImages _samples;
        private readonly TimeSpan _generationDelay;
        private readonly TimeSpan _approvalDelay;
        private readonly object _gate = new object();
        private readonly ConcurrentDictionary<string, Job> _jobs = new ConcurrentDictionary<string, Job>();
        private readonly ConcurrentDictionary<string, string> _idempotency = new ConcurrentDictionary<string, string>();
        private DateTime? _deviceApprovedAt;
        private string? _accessToken;
        private string? _refreshToken;
        private readonly List<JObject> _ledger = new List<JObject>();
        private int _credits = 10;
        private int _counter;

        public FakeCloudGateway(SampleImages samples, TimeSpan generationDelay, TimeSpan approvalDelay)
        {
            _samples = samples;
            _generationDelay = generationDelay;
            _approvalDelay = approvalDelay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            string method = request.Method.Method;

            switch (path)
            {
                case "/v1/auth/device":
                    lock (_gate)
                    {
                        _deviceApprovedAt = DateTime.UtcNow + _approvalDelay;
                    }

                    return Json(new JObject
                    {
                        ["deviceCode"] = "dev-device-code", ["userCode"] = "DEVC-ODE1",
                        ["verificationUrl"] = BaseUrl + "device", ["verificationUrlComplete"] = BaseUrl + "device?code=DEVC-ODE1",
                        ["expiresIn"] = 600, ["interval"] = 1
                    });

                case "/v1/auth/token":
                    lock (_gate)
                    {
                        if (_deviceApprovedAt == null || DateTime.UtcNow < _deviceApprovedAt)
                        {
                            return Error(400, "authorization_pending");
                        }

                        _deviceApprovedAt = null;
                    }

                    return Tokens();

                case "/v1/auth/refresh":
                    string presented = (string)JObject.Parse(await request.Content!.ReadAsStringAsync())["refreshToken"]!;
                    return presented == _refreshToken && _refreshToken != null ? Tokens() : Error(401, "auth_failed");
            }

            if (_accessToken == null || request.Headers.Authorization?.Parameter != _accessToken)
            {
                return Error(401, "auth_failed");
            }

            switch (path)
            {
                case "/v1/auth/logout":
                    _accessToken = _refreshToken = null;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/v1/me":
                    return Json(User());
                case "/v1/redeem":
                    string code = (string)JObject.Parse(await request.Content!.ReadAsStringAsync())["code"]!;
                    if (code != RedeemCode)
                    {
                        return Error(code.StartsWith("USED", StringComparison.Ordinal) ? 409 : 404, code.StartsWith("USED", StringComparison.Ordinal) ? "conflict" : "not_found");
                    }

                    int balance = Move(100, "redeem", "redeem_code", code);
                    return Json(new JObject { ["creditsAdded"] = 100, ["credits"] = balance });
                case "/v1/ledger":
                    lock (_gate)
                    {
                        // Newest first, one page: enough for development.
                        var items = new JArray();
                        for (int i = _ledger.Count - 1; i >= 0; i--)
                        {
                            items.Add(_ledger[i]);
                        }

                        return Json(new JObject { ["items"] = items, ["nextCursor"] = null });
                    }
                case "/v1/catalog":
                    var response = Json(Catalog());
                    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"dev-1\"");
                    return response;
            }

            if (path == "/v1/generations" && method == "POST")
            {
                return await SubmitAsync(request).ConfigureAwait(false);
            }

            Match match = Regex.Match(path, @"^/v1/generations/([^/]+)(/images/(\d+))?$");
            if (!match.Success || !_jobs.TryGetValue(match.Groups[1].Value, out Job? job))
            {
                return Error(404, "not_found");
            }

            if (method == "DELETE")
            {
                job.Cancel();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (match.Groups[2].Success)
            {
                return job.Image == null
                    ? Error(404, "not_found")
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(job.Image.Bytes) };
            }

            int.TryParse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["wait"], out int wait);
            await Task.WhenAny(job.Done.Task, Task.Delay(TimeSpan.FromSeconds(Math.Min(wait, 25)), cancellationToken)).ConfigureAwait(false);
            return Json(job.ToJson());
        }

        private async Task<HttpResponseMessage> SubmitAsync(HttpRequestMessage request)
        {
            string key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.First() : Guid.NewGuid().ToString("N");
            if (_idempotency.TryGetValue(key, out string? existing))
            {
                return Json(new JObject { ["id"] = existing, ["status"] = "queued", ["creditsReserved"] = _jobs[existing].Price }, 202);
            }

            var form = await request.Content!.ReadAsMultipartAsync().ConfigureAwait(false);
            JObject spec = JObject.Parse(form["request"]);
            int price = Price((string)spec["modelId"]!, (string)spec["imageSize"]!);
            if (price < 0)
            {
                return Error(400, "invalid_request");
            }

            var job = new Job(Guid.NewGuid().ToString(), price);
            lock (_gate)
            {
                if (_credits < price)
                {
                    return Error(402, "insufficient_credits");
                }

                Move(-price, "reserve", "generation", job.Id);
            }

            _jobs[job.Id] = job;
            _idempotency[key] = job.Id;
            _ = RunAsync(job, (string)spec["prompt"]!);
            return Json(new JObject { ["id"] = job.Id, ["status"] = "queued", ["creditsReserved"] = price }, 202);
        }

        private async Task RunAsync(Job job, string prompt)
        {
            job.Status = "running";
            try
            {
                await Task.Delay(prompt.Contains("#slow") ? TimeSpan.FromSeconds(10) : _generationDelay, job.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Move(job.Price, "refund", "generation", job.Id);
                job.Finish("cancelled");
                return;
            }

            Match fail = FailDirective.Match(prompt);
            if (fail.Success)
            {
                Move(job.Price, "refund", "generation", job.Id);
                job.Error = new JObject { ["code"] = fail.Groups[1].Value, ["message"] = "Scripted failure.", ["detail"] = "fake gateway message for " + fail.Groups[1].Value };
                job.Finish("failed");
                return;
            }

            var results = _samples.ResultPaths();
            job.Image = SampleImages.Load(results[Interlocked.Increment(ref _counter) % results.Count]);
            job.Finish("succeeded");
        }

        /// <summary>Changes the balance and records the movement in the ledger, as the real gateway does.</summary>
        private int Move(int delta, string type, string refType, string refId)
        {
            lock (_gate)
            {
                _credits += delta;
                _ledger.Add(new JObject
                {
                    ["id"] = _ledger.Count + 1,
                    ["delta"] = delta,
                    ["balanceAfter"] = _credits,
                    ["type"] = type,
                    ["refType"] = refType,
                    ["refId"] = refId,
                    ["createdAt"] = DateTime.UtcNow.ToString("o"),
                });
                return _credits;
            }
        }

        private static int Price(string modelId, string imageSize)
        {
            if (modelId != "cloud-standard")
            {
                return -1;
            }

            return imageSize == "1K" ? 2 : imageSize == "2K" ? 4 : -1;
        }

        private HttpResponseMessage Tokens()
        {
            int n = Interlocked.Increment(ref _counter);
            _accessToken = "dev-access-" + n;
            _refreshToken = "dev-refresh-" + n;
            return Json(new JObject { ["accessToken"] = _accessToken, ["expiresIn"] = 900, ["refreshToken"] = _refreshToken, ["user"] = User() });
        }

        private JObject User()
        {
            return new JObject { ["id"] = "00000000-0000-0000-0000-000000000001", ["email"] = "dev@example.com", ["credits"] = _credits, ["createdAt"] = "2026-01-01T00:00:00Z" };
        }

        private static JObject Catalog()
        {
            return new JObject
            {
                ["version"] = "dev-1",
                ["models"] = new JArray(new JObject
                {
                    ["id"] = "cloud-standard",
                    ["displayName"] = "Cloud Standard",
                    ["capabilities"] = new JObject
                    {
                        ["supportsMask"] = false, ["maxInputImages"] = 6, ["imageSizes"] = new JArray("1K", "2K", "4K"),
                        ["aspectRatios"] = new JArray("1:1", "4:3", "16:9"), ["maxConcurrency"] = 2
                    },
                    ["prices"] = new JObject { ["1K"] = 2, ["2K"] = 4 }
                }),
                ["styleTemplates"] = new JArray(
                    new JObject { ["id"] = "custom", ["name"] = new JObject { ["zh"] = "自定义", ["en"] = "Custom" }, ["prompt"] = string.Empty },
                    new JObject { ["id"] = "cloud-night", ["name"] = new JObject { ["zh"] = "云端夜景", ["en"] = "Cloud night" }, ["prompt"] = "night scene from the gateway" }),
                ["defaultSystemPrompt"] = "Gateway system prompt.",
                ["imageSizes"] = new JArray("1K", "2K"),
                ["aspectRatios"] = new JArray("1:1", "4:3", "16:9"),
                ["defaultImageSize"] = "1K",
                ["limits"] = new JObject { ["maxConcurrency"] = 2, ["maxUploadBytes"] = 41943040 }
            };
        }

        private static HttpResponseMessage Json(JToken body, int status = 200)
        {
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };
        }

        private static HttpResponseMessage Error(int status, string code)
        {
            return Json(new JObject { ["error"] = new JObject { ["code"] = code, ["message"] = "Fake gateway: " + code } }, status);
        }

        private sealed class Job
        {
            public Job(string id, int price)
            {
                Id = id;
                Price = price;
            }

            public string Id { get; }

            public int Price { get; }

            public string Status { get; set; } = "queued";

            public ImageData? Image { get; set; }

            public JObject? Error { get; set; }

            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

            public TaskCompletionSource<bool> Done { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Cancel()
            {
                if (!Done.Task.IsCompleted)
                {
                    Cancellation.Cancel();
                }
            }

            public void Finish(string status)
            {
                Status = status;
                Done.TrySetResult(true);
            }

            public JObject ToJson()
            {
                var json = new JObject
                {
                    ["id"] = Id, ["status"] = Status, ["createdAt"] = "2026-11-17T00:00:00Z",
                    ["creditsCharged"] = Status == "succeeded" ? Price : 0
                };
                if (Image != null)
                {
                    json["images"] = new JArray(new JObject
                    {
                        ["index"] = 0, ["url"] = "/v1/generations/" + Id + "/images/0", ["mimeType"] = Image.MimeType,
                        ["width"] = Image.Width, ["height"] = Image.Height
                    });
                }

                if (Error != null)
                {
                    json["error"] = Error;
                }

                return json;
            }
        }
    }
}
