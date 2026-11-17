using System.Collections.Generic;

namespace Chameleon.App.Bridge.Dto
{
    /// <summary>The backend list together with the current selection.</summary>
    public sealed class BackendSelectionDto
    {
        /// <summary>All backends.</summary>
        public IReadOnlyList<BackendInfoDto> Backends { get; set; } = new List<BackendInfoDto>();

        /// <summary>Backend used for new generations.</summary>
        public string ActiveBackendId { get; set; } = string.Empty;

        /// <summary>Model used for new generations.</summary>
        public string ActiveModelId { get; set; } = string.Empty;
    }
}
