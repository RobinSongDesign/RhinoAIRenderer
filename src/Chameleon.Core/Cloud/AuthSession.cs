using System;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Settings;

namespace Chameleon.Core.Cloud
{
    /// <summary>
    /// The signed-in state of account mode. The refresh token is kept in the secret store (so the
    /// user stays signed in across Rhino sessions); the short-lived access token only in memory.
    /// Concurrent callers share a single refresh.
    /// </summary>
    public sealed class AuthSession
    {
        /// <summary>Tokens are refreshed this long before they expire.</summary>
        public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

        private readonly CloudApiClient _api;
        private readonly ISecretStore _secrets;
        private readonly Func<DateTimeOffset> _now;
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private string? _accessToken;
        private DateTimeOffset _accessTokenExpires;
        private CloudUser? _user;

        /// <summary>Creates a session.</summary>
        public AuthSession(CloudApiClient api, ISecretStore secrets, Func<DateTimeOffset>? now = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>Raised after signing in, signing out, or losing the session.</summary>
        public event EventHandler? Changed;

        /// <summary>True while a refresh token is stored.</summary>
        public bool IsSignedIn => _secrets.Contains(SecretNames.CloudRefreshToken);

        /// <summary>The user as last reported by the gateway.</summary>
        public CloudUser? User => _user;

        /// <summary>Stores the tokens of a completed login or refresh.</summary>
        public void Accept(TokenResponse tokens)
        {
            if (tokens == null)
            {
                throw new ArgumentNullException(nameof(tokens));
            }

            bool wasSignedIn = IsSignedIn;
            _secrets.Set(SecretNames.CloudRefreshToken, tokens.RefreshToken);
            _accessToken = tokens.AccessToken;
            _accessTokenExpires = _now().AddSeconds(Math.Max(0, tokens.ExpiresIn));
            if (tokens.User != null)
            {
                _user = tokens.User;
            }

            if (!wasSignedIn)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Records a newer view of the user (e.g. after spending credits).</summary>
        public void UpdateUser(CloudUser user)
        {
            _user = user;
        }

        /// <summary>Returns a valid access token, refreshing it when needed.</summary>
        /// <exception cref="RenderException"><see cref="RenderErrorCode.AuthFailed"/> when not signed in or the session ended.</exception>
        public async Task<string> GetAccessTokenAsync(CancellationToken ct, bool forceRefresh = false)
        {
            string? current = _accessToken;
            if (!forceRefresh && current != null && _now() < _accessTokenExpires - RefreshMargin)
            {
                return current;
            }

            await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Another caller may have refreshed while this one waited.
                if (_accessToken != null && !ReferenceEquals(_accessToken, current) && _now() < _accessTokenExpires - RefreshMargin)
                {
                    return _accessToken;
                }

                string? refreshToken = _secrets.Get(SecretNames.CloudRefreshToken);
                if (string.IsNullOrEmpty(refreshToken))
                {
                    throw NotSignedIn();
                }

                TokenResponse tokens;
                try
                {
                    tokens = await _api.RefreshAsync(refreshToken!, ct).ConfigureAwait(false);
                }
                catch (CloudApiException ex) when (ex.Status == 401 || ex.Status == 400)
                {
                    // The refresh token was revoked, expired or replayed: the session is over.
                    EndSession();
                    throw NotSignedIn();
                }
                catch (CloudApiException ex)
                {
                    throw ex.ToRenderException();
                }

                Accept(tokens);
                return tokens.AccessToken;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <summary>
        /// Runs an authenticated call. A 401 answer triggers one refresh and one retry; gateway
        /// errors become <see cref="RenderException"/>s.
        /// </summary>
        public async Task<T> RunAuthorizedAsync<T>(Func<string, Task<T>> call, CancellationToken ct)
        {
            string token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
            try
            {
                return await call(token).ConfigureAwait(false);
            }
            catch (CloudApiException ex) when (ex.Status == 401)
            {
                token = await GetAccessTokenAsync(ct, forceRefresh: true).ConfigureAwait(false);
            }
            catch (CloudApiException ex)
            {
                throw ex.ToRenderException();
            }

            try
            {
                return await call(token).ConfigureAwait(false);
            }
            catch (CloudApiException ex) when (ex.Status == 401)
            {
                EndSession();
                throw NotSignedIn();
            }
            catch (CloudApiException ex)
            {
                throw ex.ToRenderException();
            }
        }

        /// <summary>Signs out: tells the gateway (best effort) and forgets the tokens.</summary>
        public async Task SignOutAsync(CancellationToken ct)
        {
            string? token = _accessToken;
            if (token != null)
            {
                try
                {
                    await _api.LogoutAsync(token, ct).ConfigureAwait(false);
                }
                catch (CloudApiException)
                {
                    // Revoking server-side is a courtesy; forgetting the tokens locally is what matters.
                }
            }

            EndSession();
        }

        private void EndSession()
        {
            bool wasSignedIn = IsSignedIn || _accessToken != null;
            _secrets.Delete(SecretNames.CloudRefreshToken);
            _accessToken = null;
            _user = null;
            if (wasSignedIn)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private static RenderException NotSignedIn()
        {
            return new RenderException(RenderErrorCode.AuthFailed, "You are not signed in to a Chameleon account.");
        }
    }
}
