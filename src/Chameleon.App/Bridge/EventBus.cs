using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.App.Bridge
{
    /// <summary>
    /// Fan-out of host events to any number of UI connections. Each subscriber has its own bounded
    /// queue, so a stalled connection loses its oldest events instead of blocking publishers.
    /// </summary>
    public sealed class EventBus
    {
        /// <summary>Events a subscriber can have pending before the oldest are dropped.</summary>
        public const int SubscriberCapacity = 256;

        private readonly object _gate = new object();
        private readonly List<Subscription> _subscriptions = new List<Subscription>();
        private readonly Action<string>? _log;

        /// <summary>Creates a bus.</summary>
        /// <param name="log">Receives a line when a subscriber overflows; may be null.</param>
        public EventBus(Action<string>? log = null)
        {
            _log = log;
        }

        /// <summary>Number of active subscriptions.</summary>
        public int SubscriberCount
        {
            get
            {
                lock (_gate)
                {
                    return _subscriptions.Count;
                }
            }
        }

        /// <summary>Sends an event to every subscriber. Never blocks on a subscriber.</summary>
        public void Publish(string eventName, object? data)
        {
            var bridgeEvent = new BridgeEvent(eventName, BridgeJson.Serialize(data));
            Subscription[] targets;
            lock (_gate)
            {
                targets = _subscriptions.ToArray();
            }

            foreach (Subscription subscription in targets)
            {
                if (subscription.Enqueue(bridgeEvent))
                {
                    _log?.Invoke("An event subscriber fell behind; its oldest event was dropped.");
                }
            }
        }

        /// <summary>Starts receiving events. Dispose the subscription to stop.</summary>
        public IEventSubscription Subscribe()
        {
            var subscription = new Subscription(this);
            lock (_gate)
            {
                _subscriptions.Add(subscription);
            }

            return subscription;
        }

        private void Remove(Subscription subscription)
        {
            lock (_gate)
            {
                _subscriptions.Remove(subscription);
            }
        }

        private sealed class Subscription : IEventSubscription
        {
            private readonly EventBus _owner;
            private readonly Queue<BridgeEvent> _queue = new Queue<BridgeEvent>();
            private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
            private bool _disposed;

            public Subscription(EventBus owner)
            {
                _owner = owner;
            }

            /// <summary>Adds an event; returns true when an older one had to be dropped.</summary>
            public bool Enqueue(BridgeEvent bridgeEvent)
            {
                lock (_queue)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    if (_queue.Count >= SubscriberCapacity)
                    {
                        // Replace the oldest entry; the semaphore count stays in step with the queue.
                        _queue.Dequeue();
                        _queue.Enqueue(bridgeEvent);
                        return true;
                    }

                    _queue.Enqueue(bridgeEvent);
                }

                _available.Release();
                return false;
            }

            public async Task<BridgeEvent?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                if (!await _available.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                lock (_queue)
                {
                    return _queue.Count > 0 ? _queue.Dequeue() : null;
                }
            }

            public void Dispose()
            {
                lock (_queue)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    _queue.Clear();
                }

                _owner.Remove(this);
            }
        }
    }
}
