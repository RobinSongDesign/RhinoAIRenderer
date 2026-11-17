using System;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.Core.Generation;

namespace Chameleon.Core.Backends
{
    /// <summary>When and how long to wait before sending a failed request again.</summary>
    public sealed class RetryPolicy
    {
        private static readonly Random SharedRandom = new Random();

        private readonly TimeSpan[] _delays;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly Func<double> _nextRandom;

        /// <summary>Creates a policy.</summary>
        /// <param name="delays">Base delay before each retry; its length is the retry count.</param>
        /// <param name="delayAsync">Waits for a duration; injectable so tests do not sleep.</param>
        /// <param name="nextRandom">Returns a value in [0, 1) used for jitter; injectable for tests.</param>
        public RetryPolicy(
            TimeSpan[]? delays = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
            Func<double>? nextRandom = null)
        {
            _delays = delays ?? new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6) };
            _delayAsync = delayAsync ?? Task.Delay;
            _nextRandom = nextRandom ?? NextSharedRandom;
        }

        /// <summary>A policy that never retries.</summary>
        public static RetryPolicy None { get; } = new RetryPolicy(new TimeSpan[0]);

        /// <summary>Longest server-requested delay that is honored.</summary>
        public static TimeSpan MaxRetryAfter { get; } = TimeSpan.FromSeconds(30);

        /// <summary>Fraction by which the base delay is randomly shortened or lengthened.</summary>
        public const double Jitter = 0.2;

        /// <summary>Number of retries after the first attempt.</summary>
        public int MaxRetries => _delays.Length;

        /// <summary>Computes the wait before retry number <paramref name="retryIndex"/> (0-based).</summary>
        public TimeSpan GetDelay(int retryIndex, RenderException failure)
        {
            if (failure.RetryAfter.HasValue)
            {
                // The server knows best, but a hostile or broken value must not stall the queue.
                return failure.RetryAfter.Value < MaxRetryAfter ? failure.RetryAfter.Value : MaxRetryAfter;
            }

            double factor = 1 + ((_nextRandom() * 2) - 1) * Jitter;
            return TimeSpan.FromMilliseconds(_delays[retryIndex].TotalMilliseconds * factor);
        }

        /// <summary>Waits for the given duration.</summary>
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return _delayAsync(delay, cancellationToken);
        }

        private static double NextSharedRandom()
        {
            lock (SharedRandom)
            {
                return SharedRandom.NextDouble();
            }
        }
    }
}
