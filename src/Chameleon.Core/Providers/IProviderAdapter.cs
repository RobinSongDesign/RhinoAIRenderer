using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Providers
{
    /// <summary>
    /// Translates between the neutral request model and one wire format. Implementations are
    /// stateless and never send requests themselves; supporting a new protocol means adding one.
    /// </summary>
    public interface IProviderAdapter
    {
        /// <summary>The <see cref="ApiFormats"/> value this adapter implements.</summary>
        string ApiFormat { get; }

        /// <summary>Builds the HTTP request for a generation.</summary>
        /// <param name="context">Endpoint and credential.</param>
        /// <param name="request">The neutral request.</param>
        /// <param name="promptText">Final prompt text, already composed.</param>
        HttpRequestMessage BuildGenerateRequest(AdapterContext context, GenerationRequest request, string promptText);

        /// <summary>Turns a generation response into a result.</summary>
        /// <exception cref="RenderException">The response is an error or carries no image.</exception>
        Task<GenerationResult> ParseGenerateResponseAsync(
            AdapterContext context,
            HttpResponseMessage response,
            CancellationToken cancellationToken);

        /// <summary>Builds a cheap request that only proves the credential is accepted.</summary>
        HttpRequestMessage BuildValidateRequest(AdapterContext context, string modelId);

        /// <summary>Turns a non-success response into the matching failure.</summary>
        Task<RenderException> ParseErrorAsync(
            AdapterContext context,
            HttpResponseMessage response,
            CancellationToken cancellationToken);
    }
}
