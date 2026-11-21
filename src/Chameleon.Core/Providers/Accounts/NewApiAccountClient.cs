using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chameleon.Core.Providers.Accounts
{
    /// <summary>
    /// Sign-in against a new-api gateway: its login API issues a short-lived session, which is used
    /// once to find or create this installation's key (unlimited, so it draws on the account's
    /// balance) and read it. The session is not kept; only the key is.
    /// </summary>
    public sealed class NewApiAccountClient : IRelayAccountClient
    {
        private const string KeyPrefix = "sk-";
        private const string TwoFactorMethod = "2fa";

        private readonly HttpClient _http;

        /// <summary>Creates the client over the shared HTTP client.</summary>
        public NewApiAccountClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <inheritdoc />
        public async Task<RelaySignInResult> SignInAsync(ProviderProfile profile, string username, string password, string keyName, CancellationToken cancellationToken)
        {
            RequireNewApi(profile);
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                throw new RenderException(RenderErrorCode.InvalidRequest, "Enter the account name and the password.");
            }

            var body = new JObject { ["username"] = username.Trim(), ["password"] = password };
            JToken data = await SendAsync(profile, HttpMethod.Post, "/api/user/login", body, null, RenderErrorCode.AuthFailed, cancellationToken, password).ConfigureAwait(false);
            return await CompleteAsync(profile, data, keyName, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<RelaySignInResult> VerifyTwoFactorAsync(ProviderProfile profile, string flowToken, string code, string keyName, CancellationToken cancellationToken)
        {
            RequireNewApi(profile);
            var body = new JObject { ["flow_token"] = flowToken, ["method"] = TwoFactorMethod, ["code"] = (code ?? string.Empty).Trim() };
            JToken data = await SendAsync(profile, HttpMethod.Post, "/api/user/login/2fa", body, null, RenderErrorCode.AuthFailed, cancellationToken).ConfigureAwait(false);
            return await CompleteAsync(profile, data, keyName, cancellationToken).ConfigureAwait(false);
        }

        private async Task<RelaySignInResult> CompleteAsync(ProviderProfile profile, JToken data, string keyName, CancellationToken cancellationToken)
        {
            if ((bool?)data["require_verification"] == true)
            {
                bool twoFactor = (data["methods"] as JArray)?.OfType<JObject>()
                    .Any(m => (string?)m["method"] == TwoFactorMethod && (bool?)m["available"] != false) ?? false;
                if (!twoFactor)
                {
                    // Passkeys and similar need the browser.
                    throw new RenderException(RenderErrorCode.AuthFailed, "This account needs a verification the plug-in cannot do; sign in on the website and copy a key instead.");
                }

                return RelaySignInResult.TwoFactorRequired((string?)data["flow_token"] ?? string.Empty);
            }

            string? session = (string?)data["access_token"];
            string? userId = (string?)data["user"]?["id"];
            string username = (string?)data["user"]?["username"] ?? string.Empty;
            if (string.IsNullOrEmpty(session) || string.IsNullOrEmpty(userId))
            {
                throw new RenderException(RenderErrorCode.ProviderError, "The gateway's sign-in answer has no session.");
            }

            var auth = new SessionAuth(session!, userId!);
            int keyId = await FindKeyIdAsync(profile, auth, keyName, cancellationToken).ConfigureAwait(false);
            if (keyId < 0)
            {
                var create = new JObject
                {
                    ["name"] = keyName,
                    // Unlimited: the key spends the account's balance instead of a budget of its own.
                    ["unlimited_quota"] = true,
                    ["remain_quota"] = 0,
                    ["expired_time"] = -1
                };
                await SendAsync(profile, HttpMethod.Post, "/api/token/", create, auth, RenderErrorCode.ProviderError, cancellationToken).ConfigureAwait(false);
                keyId = await FindKeyIdAsync(profile, auth, keyName, cancellationToken).ConfigureAwait(false);
                if (keyId < 0)
                {
                    throw new RenderException(RenderErrorCode.ProviderError, "The gateway did not keep the key it was asked to create.");
                }
            }

            JToken keyData = await SendAsync(profile, HttpMethod.Post, "/api/token/" + keyId + "/key", null, auth, RenderErrorCode.ProviderError, cancellationToken).ConfigureAwait(false);
            string key = ((string?)keyData["key"] ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                throw new RenderException(RenderErrorCode.ProviderError, "The gateway returned an empty key.");
            }

            return RelaySignInResult.SignedIn(key.StartsWith(KeyPrefix, StringComparison.Ordinal) ? key : KeyPrefix + key, username);
        }

        private async Task<int> FindKeyIdAsync(ProviderProfile profile, SessionAuth auth, string keyName, CancellationToken cancellationToken)
        {
            JToken page = await SendAsync(profile, HttpMethod.Get, "/api/token/?p=1&page_size=100", null, auth, RenderErrorCode.ProviderError, cancellationToken).ConfigureAwait(false);
            JObject? match = (page["items"] as JArray)?.OfType<JObject>()
                .Where(t => (string?)t["name"] == keyName)
                .OrderByDescending(t => (long?)t["id"] ?? 0)
                .FirstOrDefault();
            return match != null ? (int?)match["id"] ?? -1 : -1;
        }

        /// <summary>Sends one request and unwraps new-api's { success, message, data } envelope.</summary>
        private async Task<JToken> SendAsync(
            ProviderProfile profile,
            HttpMethod method,
            string path,
            JObject? body,
            SessionAuth? auth,
            RenderErrorCode failureCode,
            CancellationToken cancellationToken,
            params string?[] secrets)
        {
            using (var request = new HttpRequestMessage(method, profile.BaseUrl.TrimEnd('/') + path))
            {
                if (body != null)
                {
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                }

                if (auth != null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session);
                    request.Headers.TryAddWithoutValidation("New-Api-User", auth.UserId);
                }

                string?[] hide = secrets.Concat(new[] { auth?.Session }).ToArray();
                HttpResponseMessage sent;
                try
                {
                    sent = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw NetworkFailures.From(ex, Redactor.Redact(ex.Message, hide), "the account website");
                }
                catch (IOException ex)
                {
                    throw NetworkFailures.From(ex, Redactor.Redact(ex.Message, hide), "the account website");
                }

                using (HttpResponseMessage response = sent)
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw RenderException.FromHttpStatus((int)response.StatusCode, Redactor.Redact(text, hide));
                    }

                    JObject envelope;
                    try
                    {
                        envelope = JObject.Parse(text);
                    }
                    catch (JsonException)
                    {
                        throw new RenderException(RenderErrorCode.ProviderError, "The gateway's answer is not JSON.", (int)response.StatusCode, Redactor.Redact(text, hide));
                    }

                    if ((bool?)envelope["success"] != true)
                    {
                        // The gateway's own message is meant for users ("wrong password", "banned", ...).
                        string message = (string?)envelope["message"] ?? "The gateway refused the request.";
                        throw new RenderException(failureCode, message, (int)response.StatusCode, Redactor.Redact(message, hide));
                    }

                    return envelope["data"] ?? new JObject();
                }
            }
        }

        private static void RequireNewApi(ProviderProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (profile.SignInApi != SignInApis.NewApi)
            {
                throw new RenderException(RenderErrorCode.InvalidRequest, "Provider '" + profile.DisplayName + "' does not support signing in.");
            }
        }

        /// <summary>The short-lived session the sign-in uses to manage the key.</summary>
        private sealed class SessionAuth
        {
            public SessionAuth(string session, string userId)
            {
                Session = session;
                UserId = userId;
            }

            public string Session { get; }

            public string UserId { get; }
        }
    }
}
