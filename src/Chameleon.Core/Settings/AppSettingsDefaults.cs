using System;
using System.Globalization;
using Chameleon.Core.Backends;
using Chameleon.Core.Catalog;

namespace Chameleon.Core.Settings
{
    /// <summary>Builds the settings a first-time user starts with.</summary>
    public static class AppSettingsDefaults
    {
        /// <summary>Creates default settings for a catalog and UI culture.</summary>
        /// <param name="catalog">Source of the default provider and model.</param>
        /// <param name="uiCulture">Culture used to pick the language; null uses the current UI culture.</param>
        public static AppSettings Create(ChameleonCatalog catalog, CultureInfo? uiCulture = null)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var settings = new AppSettings
            {
                Language = Languages.Normalize((uiCulture ?? CultureInfo.CurrentUICulture).Name)
            };

            CatalogProvider? provider = catalog.FindProvider(catalog.DefaultProviderId);
            if (provider != null)
            {
                settings.ActiveBackendId = BackendIds.Direct(provider.Id);
                settings.ActiveModelByBackend[settings.ActiveBackendId] = provider.DefaultModel;
            }

            return settings;
        }
    }
}
