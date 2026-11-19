using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Prompts;
using Chameleon.Core.Providers;
using Chameleon.Core.Settings;

namespace Chameleon.Core.Backends
{
    /// <summary>
    /// "Bring your own key" backend: calls a provider endpoint directly with a key from the
    /// secret store. Owns the HTTP concerns every provider shares: timeout, retry, cancellation
    /// and error translation; the wire format is delegated to an <see cref="IProviderAdapter"/>.
    /// </summary>
    public sealed class DirectBackend : IRenderBackend
    {
        /// <summary>Time allowed for a single attempt, upload to last response byte.</summary>
        public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);

        private readonly ProviderProfile _profile;
        private readonly IProviderAdapter _adapter;
        private readonly ISecretStore _secrets;
        private readonly HttpClient _httpClient;
        private readonly PromptComposer _composer;
        private readonly RetryPolicy _retryPolicy;
        private readonly TimeSpan _requestTimeout;

        /// <summary>Creates a backend for one provider.</summary>
        /// <param name="profile">Endpoint description.</param>
        /// <param name="adapter">Adapter for the profile's API format.</param>
        /// <param name="secrets">Where the provider's API key is stored.</param>
        /// <param name="httpClient">Shared client; its own Timeout must be infinite.</param>
        /// <param name="composer">Builds the final prompt text.</param>
        /// <param name="retryPolicy">Retry behavior; defaults to two retries with backoff.</param>
        /// <param name="requestTimeout">Per-attempt timeout; defaults to five minutes.</param>
        public DirectBackend(
            ProviderProfile profile,
            IProviderAdapter adapter,
            ISecretStore secrets,
            HttpClient httpClient,
            PromptComposer composer,
            RetryPolicy? retryPolicy = null,
            TimeSpan? requestTimeout = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
            _retryPolicy = retryPolicy ?? new RetryPolicy();
            _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        }

        /// <inheritdoc />
        public string Id => BackendIds.Direct(_profile.Id);

        /// <inheritdoc />
        public string DisplayName => _profile.DisplayName;

        /// <summary>The provider this backend talks to.</summary>
        public ProviderProfile Profile => _profile;

        /// <inheritdoc />
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ModelInfo>>(_profile.Models.ToList());
        }

        /// <inheritdoc />
        public BackendCapabilities GetCapabilities(string modelId)
        {
            // Unknown models are not restricted: the provider is the authority on what it accepts.
            return _profile.FindModel(modelId)?.Capabilities ?? new BackendCapabilities();
        }

        /// <inheritdoc />
        public async Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            IProgress<GenerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            AdapterContext context = CreateContext();
            request.Validate(GetCapabilities(request.ModelId));
            string promptText = _composer.Compose(request);

            for (int attempt = 0; ; attempt++)
            {
                progress?.Report(new GenerationProgress(
                    attempt == 0 ? GenerationStage.Uploading : GenerationStage.Retrying,
                    attempt == 0 ? null : "Attempt " + (attempt + 1)));

                try
                {
                    return await SendOnceAsync(
                        context,
                        // A request message can only be sent once, so every attempt builds a new one.
                        () => _adapter.BuildGenerateRequest(context, request, promptText),
                        (response, ct) => _adapter.ParseGenerateResponseAsync(context, response, ct),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (RenderException ex) when (ex.IsRetryable && attempt < _retryPolicy.MaxRetries)
                {
                    await _retryPolicy.WaitAsync(_retryPolicy.GetDelay(attempt, ex), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task ValidateAsync(string modelId, CancellationToken cancellationToken)
        {
            AdapterContext context = CreateContext();
            string model = string.IsNullOrEmpty(modelId) ? _profile.DefaultModel : modelId;

            try
            {
                await SendOnceAsync(
                    context,
                    () => _adapter.BuildValidateRequest(context, model),
                    async (response, ct) =>
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw await _adapter.ParseErrorAsync(context, response, ct).ConfigureAwait(false);
                        }

                        return true;
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (RenderException ex) when (ex.Code == RenderErrorCode.InvalidRequest)
            {
                // The endpoint authenticated the key and only objected to the probe itself
                // (some image-only models reject a text-only request), so the key is fine.
            }
        }

        /// <summary>Asks the endpoint which image models it offers to the stored key.</summary>
        /// <exception cref="RenderException">No key is stored, the key is rejected, or the endpoint fails.</exception>
        public Task<IReadOnlyList<string>> DiscoverModelsAsync(CancellationToken cancellationToken)
        {
            AdapterContext context = CreateContext();
            return SendOnceAsync(
                context,
                () => _adapter.BuildListModelsRequest(context),
                (response, ct) => _adapter.ParseListModelsResponseAsync(context, response, ct),
                cancellationToken);
        }

        private AdapterContext CreateContext()
        {
            string? apiKey = _secrets.Get(SecretNames.ForProvider(_profile.Id));
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new RenderException(
                    RenderErrorCode.AuthFailed,
                    "No API key is set for provider '" + _profile.DisplayName + "'.");
            }

            return new AdapterContext(_profile, apiKey!.Trim(), _httpClient);
        }

        private async Task<T> SendOnceAsync<T>(
            AdapterContext context,
            Func<HttpRequestMessage> buildRequest,
            Func<HttpResponseMessage, CancellationToken, Task<T>> parse,
            CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(_requestTimeout);
                try
                {
                    using (HttpRequestMessage message = buildRequest())
                    using (HttpResponseMessage response = await _httpClient
                               .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                               .ConfigureAwait(false))
                    {
                        return await parse(response, timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Our own timer fired, not the caller.
                    throw new RenderException(
                        RenderErrorCode.Timeout,
                        "The provider did not answer within " + (int)_requestTimeout.TotalSeconds + " seconds.");
                }
                catch (HttpRequestException ex)
                {
                    throw NetworkFailure(context, ex);
                }
                catch (IOException ex)
                {
                    throw NetworkFailure(context, ex);
                }
            }
        }

        private static RenderException NetworkFailure(AdapterContext context, Exception ex)
        {
            return new RenderException(
                RenderErrorCode.Network,
                "Could not reach the provider.",
                null,
                context.Redact(ex.Message),
                ex);
        }
    }
}
