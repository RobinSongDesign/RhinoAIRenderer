using Newtonsoft.Json;

namespace Chameleon.App.Bridge
{
    /// <summary>Answer to an <see cref="RpcRequest"/>: either a result or an error.</summary>
    public sealed class RpcResponse
    {
        /// <summary>Id of the request being answered.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Method result; null is a valid result and is written explicitly.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public object? Result { get; set; }

        /// <summary>Set when the call failed.</summary>
        public RpcError? Error { get; set; }

        /// <summary>Tells the serializer to omit "result" when the call failed.</summary>
        public bool ShouldSerializeResult()
        {
            return Error == null;
        }
    }
}
