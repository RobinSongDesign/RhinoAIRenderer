namespace Chameleon.App.Bridge.Dto
{
    /// <summary>Parameters of "session.deleteNode".</summary>
    public sealed class DeleteNodeParams
    {
        /// <summary>Node to remove.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Remove the whole subtree; otherwise children move up to the node's parent.</summary>
        public bool WithDescendants { get; set; }
    }
}
