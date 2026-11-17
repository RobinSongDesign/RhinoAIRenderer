using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Everything the UI needs when it starts or reconnects.</summary>
    public sealed class AppStateDto
    {
        /// <summary>Version of the bridge protocol spoken by this host.</summary>
        public const int CurrentProtocolVersion = 1;

        /// <summary>Bridge protocol version.</summary>
        public int ProtocolVersion { get; set; } = CurrentProtocolVersion;

        /// <summary>Version of the plug-in or development host.</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>"win", "mac" or "dev".</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>Rhino version; empty in the development host.</summary>
        public string RhinoVersion { get; set; } = string.Empty;

        /// <summary>UI language.</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>All backends.</summary>
        public IReadOnlyList<BackendInfoDto> Backends { get; set; } = new List<BackendInfoDto>();

        /// <summary>Backend used for new generations.</summary>
        public string ActiveBackendId { get; set; } = string.Empty;

        /// <summary>Model used for new generations.</summary>
        public string ActiveModelId { get; set; } = string.Empty;

        /// <summary>Catalog data.</summary>
        public CatalogDto Catalog { get; set; } = new CatalogDto();

        /// <summary>User settings.</summary>
        public UiSettingsDto Settings { get; set; } = new UiSettingsDto();
    }
}
