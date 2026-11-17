namespace Chameleon.Core.Generation
{
    /// <summary>Coarse progress of a running generation.</summary>
    public sealed class GenerationProgress
    {
        /// <summary>Creates a progress report.</summary>
        public GenerationProgress(GenerationStage stage, string? message = null)
        {
            Stage = stage;
            Message = message;
        }

        /// <summary>Current stage.</summary>
        public GenerationStage Stage { get; }

        /// <summary>Optional detail, e.g. the retry attempt number.</summary>
        public string? Message { get; }
    }

    /// <summary>Stages a generation passes through.</summary>
    public enum GenerationStage
    {
        /// <summary>The request is being sent.</summary>
        Uploading,

        /// <summary>The request was accepted and the provider is working.</summary>
        Generating,

        /// <summary>A retryable failure occurred and the request will be sent again.</summary>
        Retrying,

        /// <summary>Result images are being downloaded.</summary>
        Downloading
    }
}
