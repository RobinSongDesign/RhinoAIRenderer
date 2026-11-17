using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge.Dto;
using Chameleon.App.Services;
using Chameleon.Core.Generation;

namespace Chameleon.App.Tests.Services
{
    /// <summary>
    /// Extra backend for service tests. Completes immediately unless the prompt contains "#hold"
    /// (waits for cancellation) or "#fail" (fails as rate limited). Records every request.
    /// </summary>
    internal sealed class ScriptedBackend : IRenderBackend, IBackendStatus
    {
        private readonly List<GenerationRequest> _requests = new List<GenerationRequest>();

        public string Id => "scripted";

        public string DisplayName => "Scripted";

        public bool IsReady { get; set; } = true;

        public string Kind => BackendInfoDto.KindDev;

        public BackendCapabilities Capabilities { get; } = new BackendCapabilities
        {
            MaxConcurrency = 2,
            MaxInputImages = 6,
            ImageSizes = new[] { "1K", "2K" },
            AspectRatios = new[] { "1:1", "16:9" }
        };

        public IReadOnlyList<GenerationRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToList();
                }
            }
        }

        public BackendCapabilities GetCapabilities(string modelId) => Capabilities;

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ModelInfo>>(new[]
            {
                new ModelInfo { Id = "scripted-a", DisplayName = "Scripted A", Capabilities = Capabilities },
                new ModelInfo { Id = "scripted-b", DisplayName = "Scripted B", Capabilities = Capabilities }
            });
        }

        public Task ValidateAsync(string modelId, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, IProgress<GenerationProgress>? progress, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request);
            }

            if (request.Prompt.Contains("#hold"))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (request.Prompt.Contains("#fail"))
            {
                throw new RenderException(RenderErrorCode.RateLimited, "scripted failure", 429, "slow down please");
            }

            return new GenerationResult(new[] { TestImages.Jpeg() }, "done", new UsageInfo(4), "req-1");
        }
    }
}
