using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.DevHost
{
    /// <summary>
    /// The parts of a new-api gateway's account API the plug-in's sign-in uses: password login,
    /// the two-factor step, and listing, creating and reading keys. Two accounts exist:
    /// "dev" and "dev-2fa" (two-factor code <see cref="TwoFactorCode"/>), both with password
    /// <see cref="Password"/>. Every key reads as <see cref="FakeProviderHandler.GoodKey"/>.
    /// </summary>
    public sealed class FakeRelayAccounts
    {
        /// <summary>Password of both development accounts.</summary>
        public const string Password = "dev-password";

        /// <summary>Two-factor code of the "dev-2fa" account.</summary>
        public const string TwoFactorCode = "123456";

        private const string FlowToken = "dev-flow-token";

        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _userIds = new Dictionary<string, int>(StringComparer.Ordinal) { ["dev"] = 1, ["dev-2fa"] = 2 };
        private readonly List<JObject> _keys = new List<JObject>();
        private int _nextKeyId = 1;

        /// <summary>Answers an account API request, or returns null for any other path. Messages are the ones new-api rc.51 sends.</summary>
        public async Task<HttpResponseMessage?> TryHandleAsync(HttpRequestMessage request)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.StartsWith("/api/user/login", StringComparison.Ordinal) && !path.StartsWith("/api/token/", StringComparison.Ordinal))
            {
                return null;
            }

            string text = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject body;
            try
            {
                body = text.Length == 0 ? new JObject() : JObject.Parse(text);
            }
            catch (JsonException)
            {
                return Fail("Invalid request body.");
            }

            if (path == "/api/user/login")
            {
                return Login((string?)body["username"], (string?)body["password"]);
            }

            if (path == "/api/user/login/2fa")
            {
                return (string?)body["flow_token"] == FlowToken && (string?)body["code"] == TwoFactorCode
                    ? Session("dev-2fa")
                    : Fail("Verification failed. Please try again.");
            }

            int? userId = Authorize(request);
            if (userId == null)
            {
                return Json(HttpStatusCode.Unauthorized, new JObject { ["success"] = false, ["message"] = "Unauthorized, not logged in and no access token provided" });
            }

            return HandleKeys(request.Method, path, body, userId.Value);
        }

        private HttpResponseMessage Login(string? username, string? password)
        {
            if (username == null || !_userIds.ContainsKey(username) || password != Password)
            {
                return Fail("Username or password is incorrect, or user has been banned");
            }

            if (username == "dev-2fa")
            {
                return Ok(new JObject
                {
                    ["require_verification"] = true,
                    ["flow_token"] = FlowToken,
                    ["methods"] = new JArray(new JObject { ["method"] = "2fa", ["available"] = true })
                });
            }

            return Session(username);
        }

        private HttpResponseMessage Session(string username)
        {
            return Ok(new JObject
            {
                ["access_token"] = "dev-session-" + username,
                ["token_type"] = "Bearer",
                ["user"] = new JObject { ["id"] = _userIds[username], ["username"] = username }
            });
        }

        private int? Authorize(HttpRequestMessage request)
        {
            string? session = request.Headers.Authorization?.Parameter;
            string? user = request.Headers.TryGetValues("New-Api-User", out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
            KeyValuePair<string, int> match = _userIds.FirstOrDefault(u => "dev-session-" + u.Key == session);
            return match.Key != null && user == match.Value.ToString() ? match.Value : (int?)null;
        }

        private HttpResponseMessage HandleKeys(HttpMethod method, string path, JObject body, int userId)
        {
            lock (_gate)
            {
                if (method == HttpMethod.Get && path == "/api/token/")
                {
                    var items = new JArray(_keys.Where(k => (int)k["user_id"]! == userId).Select(k => new JObject { ["id"] = k["id"], ["name"] = k["name"], ["key"] = "dev-****" }));
                    return Ok(new JObject { ["page"] = 1, ["page_size"] = 100, ["total"] = items.Count, ["items"] = items });
                }

                if (method == HttpMethod.Post && path == "/api/token/")
                {
                    _keys.Add(new JObject { ["id"] = _nextKeyId++, ["user_id"] = userId, ["name"] = (string?)body["name"] ?? string.Empty });
                    return Ok(null);
                }

                string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (method == HttpMethod.Post && parts.Length == 4 && parts[3] == "key" && int.TryParse(parts[2], out int id) &&
                    _keys.Any(k => (int)k["id"]! == id && (int)k["user_id"]! == userId))
                {
                    // new-api returns keys without their "sk-" prefix; the fake provider accepts both spellings.
                    return Ok(new JObject { ["key"] = FakeProviderHandler.GoodKey });
                }

                return Fail("Token does not exist");
            }
        }

        private static HttpResponseMessage Ok(JObject? data)
        {
            var envelope = new JObject { ["success"] = true, ["message"] = string.Empty };
            if (data != null)
            {
                envelope["data"] = data;
            }

            return Json(HttpStatusCode.OK, envelope);
        }

        private static HttpResponseMessage Fail(string message)
        {
            // new-api reports most refusals as 200 with success=false.
            return Json(HttpStatusCode.OK, new JObject { ["success"] = false, ["message"] = message });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, JObject body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json") };
        }
    }
}
