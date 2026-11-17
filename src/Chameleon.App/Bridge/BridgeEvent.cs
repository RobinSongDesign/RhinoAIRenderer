namespace Chameleon.App.Bridge
{
    /// <summary>A notification pushed from the host to the UI.</summary>
    public sealed class BridgeEvent
    {
        /// <summary>Creates an event.</summary>
        /// <param name="name">Event name, e.g. "job.updated".</param>
        /// <param name="json">Payload already serialized with <see cref="BridgeJson"/>.</param>
        public BridgeEvent(string name, string json)
        {
            Name = name;
            Json = json;
        }

        /// <summary>Event name.</summary>
        public string Name { get; }

        /// <summary>Serialized payload.</summary>
        public string Json { get; }
    }
}
