namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "session.setCurrent".</summary>
    public sealed class SetCurrentNodeParams
    {
        /// <summary>Node to select; null clears the selection.</summary>
        public string? Id { get; set; }
    }
}
