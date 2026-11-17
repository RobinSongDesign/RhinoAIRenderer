using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge.Dto;
using Chameleon.App.Services;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;

namespace Chameleon.DevHost
{
    /// <summary>
    /// Backend that "generates" by returning sample images after a delay. Prompts can script its
    /// behavior: "#fail:rate_limited" (any RenderErrorCode name in snake case) fails the request and
    /// "#slow" makes it take ten seconds, so the UI's error and cancel paths can be exercised.
    /// </summary>
    public sealed class FakeBackend : IRenderBackend, IBackendStatus
    {
        /// <summary>Id of the fake backend.</summary>
        public const string BackendId = "fake";

        private static readonly Regex FailDirective = new Regex(@"#fail:([a-z_]+)", RegexOptions.Compiled);
        private static readonly TimeSpan SlowDelay = TimeSpan.FromSeconds(10);

        private readonly IReadOnlyList<string> _resultPaths;
        private readonly TimeSpan _delay;
        private readonly IReadOnlyList<ModelInfo> _models;
        private int _counter;

        /// <summary>Creates the backend.</summary>
        /// <param name="samples">Source of result images.</param>
        /// <param name="delay">How long a normal generation takes.</param>
        public FakeBackend(SampleImages samples, TimeSpan delay)
        {
            _resultPaths = samples.ResultPaths();
            _delay = delay;
            _models = new[]
            {
                Model("fake-fast", "Fake Fast", supportsMask: false),
                Model("fake-mask", "Fake Mask", supportsMask: true)
            };
        }

        /// <inheritdoc />
        public string Id => BackendId;

        /// <inheritdoc />
        public string DisplayName => "Fake backend (dev)";

        /// <inheritdoc />
        public bool IsReady => true;

        /// <inheritdoc />
        public string Kind => BackendInfoDto.KindDev;

        /// <inheritdoc />
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_models);
        }

        /// <inheritdoc />
        public BackendCapabilities GetCapabilities(string modelId)
        {
            foreach (ModelInfo model in _models)
            {
                if (model.Id == modelId)
                {
                    return model.Capabilities;
                }
            }

            return _models[0].Capabilities;
        }

        /// <inheritdoc />
        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, IProgress<GenerationProgress>? progress, CancellationToken cancellationToken)
        {
            request.Validate(GetCapabilities(request.ModelId));
            string prompt = request.Prompt ?? string.Empty;

            await Task.Delay(prompt.Contains("#slow") ? SlowDelay : _delay, cancellationToken).ConfigureAwait(false);

            Match fail = FailDirective.Match(prompt);
            if (fail.Success)
            {
                throw new RenderException(ParseCode(fail.Groups[1].Value), "Scripted failure from the fake backend.", 500, "fake provider message for " + fail.Groups[1].Value);
            }

            int index = Interlocked.Increment(ref _counter) - 1;
            ImageData image = SampleImages.Load(_resultPaths[index % _resultPaths.Count]);
            return new GenerationResult(new[] { image }, null, null, "fake-" + index);
        }

        /// <inheritdoc />
        public Task ValidateAsync(string modelId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        // "rate_limited" -> RenderErrorCode.RateLimited
        private static RenderErrorCode ParseCode(string snakeCase)
        {
            string pascal = string.Empty;
            foreach (string part in snakeCase.Split('_'))
            {
                if (part.Length > 0)
                {
                    pascal += char.ToUpperInvariant(part[0]) + part.Substring(1);
                }
            }

            return Enum.TryParse(pascal, out RenderErrorCode code) ? code : RenderErrorCode.ProviderError;
        }

        private static ModelInfo Model(string id, string name, bool supportsMask)
        {
            return new ModelInfo
            {
                Id = id,
                DisplayName = name,
                Capabilities = new BackendCapabilities
                {
                    SupportsMask = supportsMask,
                    MaxInputImages = 6,
                    ImageSizes = new[] { "1K", "2K" },
                    AspectRatios = new[] { "1:1", "4:3", "3:2", "16:9", "9:16", "21:9" },
                    MaxConcurrency = 2
                }
            };
        }
    }
}
