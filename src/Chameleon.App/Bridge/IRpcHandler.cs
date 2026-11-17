using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Chameleon.App.Bridge
{
    /// <summary>Handles one RPC method.</summary>
    public interface IRpcHandler
    {
        /// <summary>Method name this handler answers.</summary>
        string Method { get; }

        /// <summary>Runs the method and returns its result (serialized with <see cref="BridgeJson"/>).</summary>
        Task<object?> HandleAsync(JToken? parameters, CancellationToken cancellationToken);
    }
}
