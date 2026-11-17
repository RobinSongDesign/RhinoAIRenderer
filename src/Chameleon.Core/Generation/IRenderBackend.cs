using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.Core.Generation
{
    /// <summary>
    /// One way of turning a <see cref="GenerationRequest"/> into images. The queue and the UI only
    /// see this interface; "bring your own key" and "signed-in account" are two implementations.
    /// </summary>
    public interface IRenderBackend
    {
        /// <summary>Stable identifier: "direct:{profileId}" or "cloud".</summary>
        string Id { get; }

        /// <summary>Name shown to the user.</summary>
        string DisplayName { get; }

        /// <summary>Lists the models this backend offers.</summary>
        Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken);

        /// <summary>Returns what the given model supports. Unknown models get the backend's defaults.</summary>
        BackendCapabilities GetCapabilities(string modelId);

        /// <summary>Runs one generation.</summary>
        /// <exception cref="RenderException">The generation failed.</exception>
        /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
        Task<GenerationResult> GenerateAsync(
            GenerationRequest request,
            IProgress<GenerationProgress>? progress,
            CancellationToken cancellationToken);

        /// <summary>Cheap credential check that does not generate an image.</summary>
        /// <exception cref="RenderException">
        /// <see cref="RenderErrorCode.AuthFailed"/> when the credential is rejected, or another code
        /// when the endpoint could not be reached.
        /// </exception>
        Task ValidateAsync(string modelId, CancellationToken cancellationToken);
    }
}
