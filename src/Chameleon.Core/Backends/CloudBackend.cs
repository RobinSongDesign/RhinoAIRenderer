using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Cloud;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;

namespace Chameleon.Core.Backends
{
    /// <summary>
    /// Account backend: generations run on the Chameleon gateway and are paid with credits.
    /// Submit, then long-poll until finished, then download the images.
    /// </summary>
    public sealed class CloudBackend : IRenderBackend, IBackendStatus
    {
        /// <summary>Seconds each poll asks the gateway to wait for completion.</summary>
        public const int PollWaitSeconds = 20;

        /// <summary>Shortest time between two polls, in case a gateway answers a long poll immediately.</summary>
        public static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(1);

        private const int MaxSubmitAttempts = 3;
        private const int MaxConsecutivePollFailures = 5;

        private readonly CloudApiClient _api;
        private readonly AuthSession _session;
        private readonly CloudCatalogCache _catalog;
        private readonly Func<string> _displayName;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        /// <summary>Creates the backend.</summary>
        /// <param name="api">Gateway client.</param>
        /// <param name="session">Signed-in state.</param>
        /// <param name="catalog">Models and prices.</param>
        /// <param name="displayName">Localized name, evaluated whenever it is shown.</param>
        /// <param name="delay">Waits between retries; injectable for tests.</param>
        public CloudBackend(CloudApiClient api, AuthSession session, CloudCatalogCache catalog, Func<string> displayName, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _displayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            _delay = delay ?? Task.Delay;
        }

        /// <inheritdoc />
        public string Id => BackendIds.Cloud;

        /// <inheritdoc />
        public string DisplayName => _displayName();

        /// <inheritdoc />
        public bool IsReady => _session.IsSignedIn;

        /// <inheritdoc />
        public string Kind => "cloud";

        /// <inheritdoc />
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            // Served from the cache so listing backends never waits on the network.
            return Task.FromResult(_catalog.GetModels());
        }

        /// <inheritdoc />
        public BackendCapabilities GetCapabilities(string modelId)
        {
            return _catalog.GetModels().FirstOrDefault(m => m.Id == modelId)?.Capabilities ?? new BackendCapabilities();
        }

        /// <inheritdoc />
        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, IProgress<GenerationProgress>? progress, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!_session.IsSignedIn)
            {
                throw new RenderException(RenderErrorCode.AuthFailed, "Sign in to your Chameleon account first.");
            }

            request.Validate(GetCapabilities(request.ModelId));
            progress?.Report(new GenerationProgress(GenerationStage.Uploading));

            GenerationAccepted accepted = await SubmitAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                progress?.Report(new GenerationProgress(GenerationStage.Generating));
                CloudGeneration generation = await WaitForCompletionAsync(accepted.Id, cancellationToken).ConfigureAwait(false);

                switch (generation.Status)
                {
                    case CloudGeneration.Succeeded:
                        progress?.Report(new GenerationProgress(GenerationStage.Downloading));
                        var images = new List<ImageData>();
                        foreach (CloudGenerationImage image in generation.Images.OrderBy(i => i.Index))
                        {
                            images.Add(await _session.RunAuthorizedAsync(token => _api.DownloadImageAsync(token, image, cancellationToken), cancellationToken).ConfigureAwait(false));
                        }

                        if (images.Count == 0)
                        {
                            throw new RenderException(RenderErrorCode.NoImageReturned, "The generation finished without an image.");
                        }

                        if (_session.User != null)
                        {
                            _session.User.Credits = Math.Max(0, _session.User.Credits - generation.CreditsCharged);
                        }

                        return new GenerationResult(images, generation.Text, new UsageInfo(generation.CreditsCharged), generation.Id);

                    case CloudGeneration.Cancelled:
                        throw new OperationCanceledException("The generation was cancelled.");

                    default:
                        CloudError error = generation.Error ?? new CloudError { Code = "provider_error", Message = "The generation failed." };
                        throw new RenderException(
                            CloudApiException.ToRenderErrorCode(error.Code, 0),
                            error.Message,
                            null,
                            error.Detail ?? error.Message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CancelOnGatewayAsync(accepted.Id).ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc />
        public Task ValidateAsync(string modelId, CancellationToken cancellationToken)
        {
            return _session.RunAuthorizedAsync(token => _api.GetMeAsync(token, cancellationToken), cancellationToken);
        }

        private async Task<GenerationAccepted> SubmitAsync(GenerationRequest request, CancellationToken ct)
        {
            // The same key on every attempt: if a response was lost, the retry returns the first job instead of paying twice.
            string idempotencyKey = Guid.NewGuid().ToString("N");
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await _session.RunAuthorizedAsync(token => _api.SubmitGenerationAsync(token, request, idempotencyKey, ct), ct).ConfigureAwait(false);
                }
                catch (RenderException ex) when (attempt < MaxSubmitAttempts && (ex.Code == RenderErrorCode.Network || ex.Code == RenderErrorCode.Timeout || ex.Code == RenderErrorCode.ProviderError))
                {
                    await _delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false);
                }
            }
        }

        private async Task<CloudGeneration> WaitForCompletionAsync(string id, CancellationToken ct)
        {
            int failures = 0;
            while (true)
            {
                CloudGeneration generation;
                var started = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    generation = await _session.RunAuthorizedAsync(token => _api.GetGenerationAsync(token, id, PollWaitSeconds, ct), ct).ConfigureAwait(false);
                    failures = 0;
                }
                catch (RenderException ex) when (++failures < MaxConsecutivePollFailures && (ex.Code == RenderErrorCode.Network || ex.Code == RenderErrorCode.Timeout || ex.Code == RenderErrorCode.ProviderError))
                {
                    // The job keeps running on the gateway; a dropped poll is simply repeated.
                    await _delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    continue;
                }

                if (generation.IsFinished)
                {
                    return generation;
                }

                if (started.Elapsed < MinPollInterval)
                {
                    await _delay(MinPollInterval - started.Elapsed, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task CancelOnGatewayAsync(string id)
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    await _session.RunAuthorizedAsync(async token =>
                    {
                        await _api.CancelGenerationAsync(token, id, timeout.Token).ConfigureAwait(false);
                        return true;
                    }, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is RenderException || ex is OperationCanceledException)
            {
                // Best effort: an uncancelled job still finishes and is charged, which the user can see in the ledger.
            }
        }
    }
}
