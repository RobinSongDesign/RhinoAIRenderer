using System;
using System.Collections.Generic;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Providers
{
    /// <summary>Looks up the adapter for an API format.</summary>
    public sealed class AdapterRegistry
    {
        private readonly Dictionary<string, IProviderAdapter> _adapters =
            new Dictionary<string, IProviderAdapter>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates a registry holding the given adapters.</summary>
        public AdapterRegistry(IEnumerable<IProviderAdapter> adapters)
        {
            foreach (IProviderAdapter adapter in adapters ?? throw new ArgumentNullException(nameof(adapters)))
            {
                _adapters[adapter.ApiFormat] = adapter;
            }
        }

        /// <summary>Creates a registry with every adapter shipped with the core.</summary>
        public static AdapterRegistry CreateDefault()
        {
            return new AdapterRegistry(new IProviderAdapter[] { new GeminiAdapter(), new OpenAIImagesAdapter() });
        }

        /// <summary>Returns the adapter for a format.</summary>
        /// <exception cref="RenderException">No adapter implements the format.</exception>
        public IProviderAdapter Get(string apiFormat)
        {
            if (apiFormat != null && _adapters.TryGetValue(apiFormat, out IProviderAdapter? adapter))
            {
                return adapter;
            }

            throw new RenderException(
                RenderErrorCode.InvalidRequest,
                "Unsupported API format '" + apiFormat + "'.");
        }
    }
}
