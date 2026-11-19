using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;

namespace Chameleon.Core.Tests.Jobs
{
    /// <summary>Backend whose calls stay pending until the test completes, fails or cancels them.</summary>
    internal sealed class ControllableBackend : IRenderBackend
    {
        private readonly object _gate = new object();
        private readonly List<Call> _calls = new List<Call>();

        public ControllableBackend(string id = "fake", int maxConcurrency = 2, int maxInputImages = 0)
        {
            Id = id;
            Capabilities = new BackendCapabilities { MaxConcurrency = maxConcurrency, MaxInputImages = maxInputImages };
        }

        public string Id { get; }

        public string DisplayName => Id;

        public BackendCapabilities Capabilities { get; }

        public IReadOnlyList<Call> Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToList();
                }
            }
        }

        public int PendingCount => Calls.Count(c => !c.Completion.Task.IsCompleted);

        public BackendCapabilities GetCapabilities(string modelId) => Capabilities;

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ModelInfo>>(Array.Empty<ModelInfo>());
        }

        public Task ValidateAsync(string modelId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, IProgress<GenerationProgress>? progress, CancellationToken cancellationToken)
        {
            var call = new Call(request);
            cancellationToken.Register(() => call.Completion.TrySetCanceled(cancellationToken));
            lock (_gate)
            {
                _calls.Add(call);
            }

            return call.Completion.Task;
        }

        /// <summary>Waits until the backend has received <paramref name="count"/> calls in total.</summary>
        /// <remarks>
        /// Only meaningful when the jobs reach the backend one at a time. Jobs that run in parallel
        /// are started on separate thread-pool threads, so their arrival order is not their enqueue
        /// order; use <see cref="WaitForCallAsync(string)"/> for those.
        /// </remarks>
        public Task<Call> WaitForCallAsync(int count)
        {
            return WaitForAsync(calls => calls.Count >= count ? calls[count - 1] : null, "call #" + count);
        }

        /// <summary>Waits for the call made on behalf of the job whose request carries <paramref name="prompt"/>.</summary>
        public Task<Call> WaitForCallAsync(string prompt)
        {
            return WaitForAsync(
                calls => calls.FirstOrDefault(c => c.Request.Prompt == prompt),
                "a call with prompt '" + prompt + "'");
        }

        private async Task<Call> WaitForAsync(Func<IReadOnlyList<Call>, Call?> pick, string description)
        {
            // The deadline only turns a hang into a readable failure; no test relies on its length.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Call? call = pick(Calls);
                if (call != null)
                {
                    return call;
                }

                await Task.Delay(5);
            }

            throw new TimeoutException("The backend never received " + description + ".");
        }

        public sealed class Call
        {
            public Call(GenerationRequest request)
            {
                Request = request;
            }

            public GenerationRequest Request { get; }

            public TaskCompletionSource<GenerationResult> Completion { get; } =
                new TaskCompletionSource<GenerationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Succeed(ImageData? image = null)
            {
                Completion.TrySetResult(new GenerationResult(new[] { image ?? TestImages.Png() }, null, null, "req"));
            }

            public void Fail(RenderErrorCode code = RenderErrorCode.ProviderError)
            {
                Completion.TrySetException(new RenderException(code, "scripted failure"));
            }

            public void Crash()
            {
                Completion.TrySetException(new InvalidOperationException("backend bug"));
            }
        }
    }
}
