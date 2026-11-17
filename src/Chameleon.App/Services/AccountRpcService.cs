using System;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Chameleon.App.Hosting;
using Chameleon.Core.Backends;
using Chameleon.Core.Cloud;
using Chameleon.Core.Generation;
using Chameleon.Core.Jobs;

namespace Chameleon.App.Services
{
    /// <summary>Sign-in, balance, redeem codes and the ledger for the UI (bridge-protocol.md 4.7).</summary>
    public sealed class AccountRpcService
    {
        private const string ClientName = "Chameleon for Rhino";

        private readonly IRhinoHost _host;
        private readonly CloudServices? _cloud;
        private readonly EventBus _events;
        private readonly BackendService _backendService;
        private readonly object _gate = new object();
        private CancellationTokenSource? _loginCancellation;
        private LoginStateDto? _login;

        /// <summary>Creates the service; <paramref name="cloud"/> is null when no gateway is configured.</summary>
        public AccountRpcService(IRhinoHost host, CloudServices? cloud, EventBus events, BackendService backendService, IGenerationQueue queue)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _cloud = cloud;
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _backendService = backendService ?? throw new ArgumentNullException(nameof(backendService));

            if (cloud != null)
            {
                cloud.Session.Changed += (sender, e) =>
                {
                    // While a device sign-in completes, CompleteLoginAsync announces it once the
                    // account's catalog is loaded; announcing now would show the built-in templates.
                    lock (_gate)
                    {
                        if (_loginCancellation != null)
                        {
                            return;
                        }
                    }

                    PublishAll();
                };
                // A finished account job changed the balance.
                queue.JobChanged += (sender, e) =>
                {
                    if (e.Job.BackendId == BackendIds.Cloud && e.Job.IsFinished)
                    {
                        Publish();
                    }
                };
            }
        }

        /// <summary>Registers this service's RPC methods.</summary>
        public void Register(RpcRouter router)
        {
            router.Register("auth.startLogin", StartLoginAsync);
            router.Register<object?>("auth.cancelLogin", ct =>
            {
                CancelLogin();
                return Task.FromResult<object?>(null);
            });
            router.Register<object?>("auth.logout", async ct =>
            {
                await RequireCloud().Session.SignOutAsync(ct).ConfigureAwait(false);
                return null;
            });
            router.RegisterRaw("account.get", async (parameters, ct) =>
            {
                bool refresh = parameters != null && BridgeJson.ToObject<AccountGetParams>(parameters)?.Refresh == true;
                return await GetAsync(refresh, ct).ConfigureAwait(false);
            });
            router.Register<RedeemParams, RedeemResult>("account.redeem", RedeemAsync);
            router.RegisterRaw("account.ledger", async (parameters, ct) =>
            {
                string? cursor = parameters == null ? null : BridgeJson.ToObject<LedgerParams>(parameters)?.Cursor;
                return await RequireCloud().Account.GetLedgerAsync(cursor, ct).ConfigureAwait(false);
            });
            router.Register<EstimateParams, object>("account.estimate", (p, ct) =>
            {
                int? price = RequireCloud().Account.Estimate(p.ModelId, p.ImageSize);
                return Task.FromResult<object>(new { credits = price.HasValue ? price.Value * Math.Max(1, p.Count) : (int?)null });
            });
        }

        /// <summary>Current account state, without network access.</summary>
        public AccountInfoDto Describe()
        {
            if (_cloud == null)
            {
                return new AccountInfoDto { Available = false };
            }

            CloudUser? user = _cloud.Session.User;
            lock (_gate)
            {
                return new AccountInfoDto
                {
                    Available = true,
                    SignedIn = _cloud.Session.IsSignedIn,
                    Email = user?.Email,
                    Credits = user?.Credits,
                    Login = _login
                };
            }
        }

        /// <summary>Starts a device sign-in, opens the browser, and finishes in the background.</summary>
        public async Task<LoginStateDto> StartLoginAsync(CancellationToken ct)
        {
            CloudServices cloud = RequireCloud();
            CancelLogin();

            DeviceAuthorization authorization = await cloud.Login
                .StartAsync(ClientName, _host.Info.AppVersion, PlatformCode(), ct).ConfigureAwait(false);

            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, authorization.ExpiresIn)));
            var state = new LoginStateDto
            {
                State = LoginStateDto.Pending,
                UserCode = authorization.UserCode,
                VerificationUrl = string.IsNullOrEmpty(authorization.VerificationUrlComplete)
                    ? authorization.VerificationUrl
                    : authorization.VerificationUrlComplete,
                ExpiresIn = authorization.ExpiresIn
            };
            lock (_gate)
            {
                _loginCancellation = cancellation;
                _login = state;
            }

            if (Uri.TryCreate(state.VerificationUrl, UriKind.Absolute, out Uri? url) && url.Scheme == Uri.UriSchemeHttps)
            {
                _host.OpenUrl(url.AbsoluteUri);
            }

            Publish();
            _ = Task.Run(() => CompleteLoginAsync(cloud, authorization, cancellation));
            return state;
        }

        /// <summary>Stops waiting for the browser confirmation.</summary>
        public void CancelLogin()
        {
            CancellationTokenSource? cancellation;
            lock (_gate)
            {
                cancellation = _loginCancellation;
                _loginCancellation = null;
                _login = null;
            }

            if (cancellation != null)
            {
                cancellation.Cancel();
                Publish();
            }
        }

        /// <summary>Account state, optionally refreshed from the gateway.</summary>
        public async Task<AccountInfoDto> GetAsync(bool refresh, CancellationToken ct)
        {
            if (refresh && _cloud != null && _cloud.Session.IsSignedIn)
            {
                await _cloud.Account.GetMeAsync(ct).ConfigureAwait(false);
            }

            return Describe();
        }

        /// <summary>Redeems a code; gateway reasons become distinct bridge codes.</summary>
        public async Task<RedeemResult> RedeemAsync(RedeemParams parameters, CancellationToken ct)
        {
            CloudServices cloud = RequireCloud();
            try
            {
                RedeemResult result = await cloud.Account.RedeemAsync(parameters.Code, ct).ConfigureAwait(false);
                Publish();
                return result;
            }
            catch (CloudApiException ex)
            {
                switch (ex.Status)
                {
                    case 404: throw new BridgeException(BridgeErrorCodes.NotFound, "Unknown redeem code.", ex.Detail);
                    case 409: throw new BridgeException(BridgeErrorCodes.Conflict, "The redeem code was already used.", ex.Detail);
                    case 410: throw new BridgeException(BridgeErrorCodes.Gone, "The redeem code expired or was revoked.", ex.Detail);
                    default: throw ex.ToRenderException();
                }
            }
        }

        private async Task CompleteLoginAsync(CloudServices cloud, DeviceAuthorization authorization, CancellationTokenSource cancellation)
        {
            try
            {
                await cloud.Login.WaitForApprovalAsync(authorization, cancellation.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (ReferenceEquals(_loginCancellation, cancellation))
                    {
                        _loginCancellation = null;
                        _login = null;
                    }
                }

                try
                {
                    await cloud.Catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (RenderException ex)
                {
                    _host.Log("Loading the account catalog failed: " + ex.Message);
                }

                PublishAll();
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the user (the state was already cleared) or the code expired.
                MarkFailed(cancellation, new RpcError { Code = BridgeErrorCodes.Cancelled, Message = "The sign-in expired." });
            }
            catch (RenderException ex)
            {
                MarkFailed(cancellation, BridgeErrorMapper.FromRenderException(ex));
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private void MarkFailed(CancellationTokenSource cancellation, RpcError error)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_loginCancellation, cancellation) || _login == null)
                {
                    return;
                }

                _loginCancellation = null;
                _login = new LoginStateDto
                {
                    State = LoginStateDto.Failed,
                    UserCode = _login.UserCode,
                    VerificationUrl = _login.VerificationUrl,
                    ExpiresIn = _login.ExpiresIn,
                    Error = error
                };
            }

            Publish();
        }

        private void Publish()
        {
            _events.Publish(BridgeEvents.AccountChanged, Describe());
        }

        // Signing in or out also changes whether the account backend is ready.
        private void PublishAll()
        {
            Publish();
            _ = _backendService.PublishChangedAsync(CancellationToken.None);
        }

        private CloudServices RequireCloud()
        {
            return _cloud ?? throw new BridgeException(BridgeErrorCodes.NotReady, "Account mode is not available in this build.");
        }

        private string PlatformCode()
        {
            switch (_host.Info.Platform)
            {
                case HostPlatform.Windows: return "win";
                case HostPlatform.MacOS: return "mac";
                default: return "dev";
            }
        }
    }
}
