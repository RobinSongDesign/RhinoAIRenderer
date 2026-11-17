using Newtonsoft.Json.Linq;

namespace Chameleon.App.Bridge
{
    /// <summary>A call from the UI.</summary>
    public sealed class RpcRequest
    {
        /// <summary>Caller-chosen id echoed in the response.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Method name, e.g. "jobs.submit".</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>Method parameters; absent for parameterless methods.</summary>
        public JToken? Params { get; set; }
    }
}
