using System;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Cloud
{
    /// <summary>
    /// Device authorization: the plug-in shows a code, the user confirms it in the browser, and the
    /// plug-in polls until tokens are issued. No password ever passes through the plug-in.
    /// </summary>
    public sealed class DeviceLoginFlow
    {
        /// <summary>Added to the poll interval when the gateway answers "slow_down".</summary>
        public static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

        private readonly CloudApiClient _api;
        private readonly AuthSession _session;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        /// <summary>Creates the flow.</summary>
        /// <param name="api">Gateway client.</param>
        /// <param name="session">Receives the tokens.</param>
        /// <param name="delay">Waits between polls; injectable for tests.</param>
        public DeviceLoginFlow(CloudApiClient api, AuthSession session, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _delay = delay ?? Task.Delay;
        }

        /// <summary>Asks the gateway for a device code to show to the user.</summary>
        public async Task<DeviceAuthorization> StartAsync(string clientName, string clientVersion, string platform, CancellationToken ct)
        {
            try
            {
                return await _api.StartDeviceAuthorizationAsync(clientName, clientVersion, platform, ct).ConfigureAwait(false);
            }
            catch (CloudApiException ex)
            {
                throw ex.ToRenderException();
            }
        }

        /// <summary>Polls until the user confirmed (tokens are stored in the session) or the code is no longer usable.</summary>
        /// <exception cref="RenderException">The user refused or the code expired (<see cref="RenderErrorCode.AuthFailed"/>), or the gateway failed.</exception>
        /// <exception cref="OperationCanceledException">The caller stopped waiting.</exception>
        public async Task<CloudUser?> WaitForApprovalAsync(DeviceAuthorization authorization, CancellationToken ct)
        {
            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, authorization.Interval));
            while (true)
            {
                await _delay(interval, ct).ConfigureAwait(false);
                try
                {
                    TokenResponse tokens = await _api.PollDeviceTokenAsync(authorization.DeviceCode, ct).ConfigureAwait(false);
                    _session.Accept(tokens);
                    return tokens.User;
                }
                catch (CloudApiException ex) when (ex.Code == "authorization_pending")
                {
                    // Not confirmed yet.
                }
                catch (CloudApiException ex) when (ex.Code == "slow_down")
                {
                    interval += SlowDownStep;
                }
                catch (CloudApiException ex) when (ex.Code == "expired_token" || ex.Code == "access_denied")
                {
                    throw new RenderException(
                        RenderErrorCode.AuthFailed,
                        ex.Code == "expired_token" ? "The sign-in code expired." : "The sign-in was refused in the browser.",
                        ex.Status,
                        ex.Code);
                }
                catch (CloudApiException ex) when (ex.Status == 0 || ex.Status >= 500)
                {
                    // Connection hiccups during a minutes-long wait should not abort the sign-in.
                }
                catch (CloudApiException ex)
                {
                    throw ex.ToRenderException();
                }
            }
        }
    }
}
