using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Chameleon.Core.Serialization;

namespace Chameleon.Core.Catalog
{
    /// <summary>Loads and validates catalog documents.</summary>
    public static class CatalogLoader
    {
        private const string ResourceName = "Chameleon.Core.Catalog.catalog.json";

        /// <summary>Loads the catalog embedded in this assembly.</summary>
        public static ChameleonCatalog LoadBuiltIn()
        {
            Assembly assembly = typeof(CatalogLoader).GetTypeInfo().Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded catalog resource is missing: " + ResourceName);
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return Parse(reader.ReadToEnd());
                }
            }
        }

        /// <summary>Parses and validates a catalog document.</summary>
        /// <exception cref="FormatException">The document is malformed or violates a catalog rule.</exception>
        public static ChameleonCatalog Parse(string json)
        {
            ChameleonCatalog? catalog;
            try
            {
                catalog = CoreJson.Deserialize<ChameleonCatalog>(json);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw new FormatException("The catalog is not valid JSON: " + ex.Message, ex);
            }

            if (catalog == null)
            {
                throw new FormatException("The catalog is empty.");
            }

            Validate(catalog);
            return catalog;
        }

        private static void Validate(ChameleonCatalog catalog)
        {
            RequireUnique(catalog.Providers.Select(p => p.Id), "provider id");
            RequireUnique(catalog.StyleTemplates.Select(t => t.Id), "style template id");

            foreach (CatalogProvider provider in catalog.Providers)
            {
                Require(!string.IsNullOrWhiteSpace(provider.Id), "A provider has no id.");
                Require(!string.IsNullOrWhiteSpace(provider.BaseUrl), "Provider '" + provider.Id + "' has no base URL.");
                Require(!string.IsNullOrWhiteSpace(provider.ApiFormat), "Provider '" + provider.Id + "' has no API format.");
                Require(provider.Models.Count > 0, "Provider '" + provider.Id + "' has no models.");
                RequireUnique(provider.Models.Select(m => m.Id), "model id in provider '" + provider.Id + "'");
                Require(
                    provider.Models.Any(m => m.Id == provider.DefaultModel),
                    "Default model of provider '" + provider.Id + "' is not in its model list.");
            }

            if (catalog.Providers.Count > 0)
            {
                Require(
                    catalog.FindProvider(catalog.DefaultProviderId) != null,
                    "The default provider '" + catalog.DefaultProviderId + "' does not exist.");
            }

            if (catalog.ImageSizes.Count > 0)
            {
                Require(
                    catalog.ImageSizes.Contains(catalog.DefaultImageSize),
                    "The default image size is not in the image size list.");
            }
        }

        private static void RequireUnique(IEnumerable<string> ids, string what)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                Require(seen.Add(id), "Duplicate " + what + ": '" + id + "'.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new FormatException(message);
            }
        }
    }
}
