using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Bridge;
using Xunit;

namespace Chameleon.App.Tests.Bridge
{
    public class EventBusTests
    {
        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task Publish_DeliversToEverySubscriberInOrder()
        {
            var bus = new EventBus();
            using IEventSubscription a = bus.Subscribe();
            using IEventSubscription b = bus.Subscribe();

            bus.Publish("one", new { value = 1 });
            bus.Publish("two", new { value = 2 });

            foreach (IEventSubscription subscription in new[] { a, b })
            {
                BridgeEvent first = (await subscription.ReadAsync(Long, CancellationToken.None))!;
                BridgeEvent second = (await subscription.ReadAsync(Long, CancellationToken.None))!;
                Assert.Equal("one", first.Name);
                Assert.Equal("{\"value\":1}", first.Json);
                Assert.Equal("two", second.Name);
            }
        }

        [Fact]
        public async Task Read_TimesOutWithNullWhenNothingIsPublished()
        {
            var bus = new EventBus();
            using IEventSubscription subscription = bus.Subscribe();

            Assert.Null(await subscription.ReadAsync(Short, CancellationToken.None));
        }

        [Fact]
        public async Task Read_WakesUpWhenAnEventArrivesLater()
        {
            var bus = new EventBus();
            using IEventSubscription subscription = bus.Subscribe();

            Task<BridgeEvent?> pending = subscription.ReadAsync(Long, CancellationToken.None);
            bus.Publish("late", null);

            Assert.Equal("late", (await pending)!.Name);
        }

        [Fact]
        public async Task Read_HonorsCancellation()
        {
            var bus = new EventBus();
            using IEventSubscription subscription = bus.Subscribe();
            using var cts = new CancellationTokenSource(Short);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscription.ReadAsync(Long, cts.Token));
        }

        [Fact]
        public async Task SlowSubscriber_LosesOldestEventsWithoutBlockingThePublisher()
        {
            var log = new List<string>();
            var bus = new EventBus(log.Add);
            using IEventSubscription slow = bus.Subscribe();

            int total = EventBus.SubscriberCapacity + 10;
            for (int i = 0; i < total; i++)
            {
                bus.Publish("e" + i, null);
            }

            var received = new List<string>();
            BridgeEvent? next;
            while ((next = await slow.ReadAsync(Short, CancellationToken.None)) != null)
            {
                received.Add(next.Name);
            }

            Assert.Equal(EventBus.SubscriberCapacity, received.Count);
            Assert.Equal("e10", received[0]);
            Assert.Equal("e" + (total - 1), received[received.Count - 1]);
            Assert.Equal(10, log.Count);
        }

        [Fact]
        public void Dispose_RemovesTheSubscription()
        {
            var bus = new EventBus();
            IEventSubscription subscription = bus.Subscribe();
            Assert.Equal(1, bus.SubscriberCount);

            subscription.Dispose();
            subscription.Dispose();
            bus.Publish("ignored", null);

            Assert.Equal(0, bus.SubscriberCount);
        }
    }
}
