namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "session.updateNode"; omitted fields stay as they are.</summary>
    public sealed class UpdateNodeParams
    {
        /// <summary>Node to change.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>New label; an empty string removes it.</summary>
        public string? Label { get; set; }

        /// <summary>New favourite flag.</summary>
        public bool? Starred { get; set; }
    }
}
