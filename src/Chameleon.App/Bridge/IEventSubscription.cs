using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.App.Bridge
{
    /// <summary>One consumer's view of the <see cref="EventBus"/>.</summary>
    public interface IEventSubscription : IDisposable
    {
        /// <summary>Waits for the next event.</summary>
        /// <param name="timeout">How long to wait before giving up.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The event, or null when the timeout elapsed first.</returns>
        Task<BridgeEvent?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken);
    }
}
