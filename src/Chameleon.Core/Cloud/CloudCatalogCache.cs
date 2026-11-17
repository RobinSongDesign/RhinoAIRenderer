using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Serialization;

namespace Chameleon.Core.Cloud
{
    /// <summary>
    /// The gateway's catalog, kept in memory and on disk so models and prices are available at
    /// once on start-up and while offline. Refreshes use ETags, so unchanged catalogs cost little.
    /// </summary>
    public sealed class CloudCatalogCache
    {
        private readonly CloudApiClient _api;
        private readonly AuthSession _session;
        private readonly string? _cacheFile;
        private readonly object _gate = new object();
        private CloudCatalog? _catalog;
        private string? _etag;

        /// <summary>Creates the cache and loads the last saved copy, if any.</summary>
        /// <param name="api">Gateway client.</param>
        /// <param name="session">Provides access tokens.</param>
        /// <param name="cacheFile">Where to persist the catalog; null keeps it in memory only.</param>
        public CloudCatalogCache(CloudApiClient api, AuthSession session, string? cacheFile)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _cacheFile = cacheFile;
            LoadFromDisk();
        }

        /// <summary>The last known catalog, or null.</summary>
        public CloudCatalog? Current
        {
            get
            {
                lock (_gate)
                {
                    return _catalog;
                }
            }
        }

        /// <summary>Fetches the catalog from the gateway (conditionally) and returns the result.</summary>
        public async Task<CloudCatalog> RefreshAsync(CancellationToken ct)
        {
            string? etag;
            lock (_gate)
            {
                etag = _catalog != null ? _etag : null;
            }

            var (catalog, newEtag) = await _session.RunAuthorizedAsync(token => _api.GetCatalogAsync(token, etag, ct), ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (catalog != null)
                {
                    _catalog = catalog;
                    _etag = newEtag;
                    SaveToDisk();
                }

                return _catalog ?? throw new RenderException(RenderErrorCode.ProviderError, "The Chameleon service sent no catalog.");
            }
        }

        /// <summary>Models as the generation layer sees them: sizes are limited to the ones that have a price.</summary>
        public IReadOnlyList<ModelInfo> GetModels()
        {
            CloudCatalog? catalog = Current;
            if (catalog == null)
            {
                return Array.Empty<ModelInfo>();
            }

            return catalog.Models.Select(model => ToModelInfo(model, catalog.Limits)).ToList();
        }

        /// <summary>Credits one generation costs, or null when the model or size is not offered.</summary>
        public int? GetPrice(string modelId, string imageSize)
        {
            CloudCatalogModel? model = Current?.Models.FirstOrDefault(m => m.Id == modelId);
            return model != null && model.Prices.TryGetValue(imageSize, out int price) ? price : (int?)null;
        }

        private static ModelInfo ToModelInfo(CloudCatalogModel model, CloudLimits limits)
        {
            BackendCapabilities caps = model.Capabilities.Clone();
            var priced = new HashSet<string>(model.Prices.Keys, StringComparer.Ordinal);
            caps.ImageSizes = caps.ImageSizes.Count > 0
                ? caps.ImageSizes.Where(priced.Contains).ToArray()
                : model.Prices.Keys.ToArray();
            caps.MaxConcurrency = Math.Max(1, Math.Min(caps.MaxConcurrency, Math.Max(1, limits.MaxConcurrency)));

            return new ModelInfo
            {
                Id = model.Id,
                DisplayName = string.IsNullOrEmpty(model.DisplayName) ? model.Id : model.DisplayName,
                Capabilities = caps
            };
        }

        private void LoadFromDisk()
        {
            if (_cacheFile == null || !File.Exists(_cacheFile))
            {
                return;
            }

            try
            {
                var cached = CoreJson.Deserialize<CachedCatalog>(File.ReadAllText(_cacheFile));
                if (cached?.Catalog != null)
                {
                    _catalog = cached.Catalog;
                    _etag = cached.ETag;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is Newtonsoft.Json.JsonException || ex is UnauthorizedAccessException)
            {
                // A damaged cache is simply refetched.
            }
        }

        private void SaveToDisk()
        {
            if (_cacheFile == null || _catalog == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile) ?? ".");
                File.WriteAllText(_cacheFile, CoreJson.Serialize(new CachedCatalog { Catalog = _catalog, ETag = _etag }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Caching is an optimization only.
            }
        }

        private sealed class CachedCatalog
        {
            public CloudCatalog? Catalog { get; set; }

            public string? ETag { get; set; }
        }
    }
}
