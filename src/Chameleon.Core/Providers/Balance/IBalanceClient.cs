using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.Core.Providers.Balance
{
    /// <summary>Asks a provider how much is left on a key.</summary>
    public interface IBalanceClient
    {
        /// <summary>Queries the balance of <paramref name="apiKey"/> at the provider.</summary>
        /// <exception cref="Generation.RenderException">
        /// The provider has no balance endpoint, rejected the key, or answered with something unreadable.
        /// </exception>
        Task<ProviderBalance> GetBalanceAsync(ProviderProfile profile, string apiKey, CancellationToken cancellationToken);
    }
}
