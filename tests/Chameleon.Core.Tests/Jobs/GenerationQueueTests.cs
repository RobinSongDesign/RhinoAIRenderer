using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;
using Chameleon.Core.Jobs;
using Xunit;

namespace Chameleon.Core.Tests.Jobs
{
    public class GenerationQueueTests : IDisposable
    {
        private readonly ControllableBackend _backend = new ControllableBackend();
        private readonly GenerationQueue _queue;
        private readonly List<GenerationJob> _events = new List<GenerationJob>();

        public GenerationQueueTests()
        {
            _queue = new GenerationQueue(id => id == _backend.Id ? _backend : null);
            _queue.JobChanged += (s, e) =>
            {
                lock (_events)
                {
                    _events.Add(e.Job);
                }
            };
        }

        public void Dispose() => _queue.Dispose();

        private static GenerationRequest Request(string prompt = "p")
        {
            return new GenerationRequest { ModelId = "m", Prompt = prompt, Source = TestImages.Png() };
        }

        private async Task<GenerationJob> WaitForStateAsync(string jobId, JobState state)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                GenerationJob? job = _queue.Find(jobId);
                if (job != null && job.State == state)
                {
                    return job;
                }

                await Task.Delay(5);
            }

            throw new TimeoutException("Job " + jobId + " never reached " + state + " (is " + _queue.Find(jobId)?.State + ").");
        }

        private List<JobState> StatesOf(string jobId)
        {
            lock (_events)
            {
                return _events.Where(e => e.Id == jobId).Select(e => e.State).ToList();
            }
        }

        [Fact]
        public async Task Job_RunsAndSucceeds_WithOrderedEvents()
        {
            GenerationJob job = _queue.Enqueue(_backend.Id, Request(), new JobOptions { Label = "View A" });
            (await _backend.WaitForCallAsync(1)).Succeed();

            GenerationJob done = await WaitForStateAsync(job.Id, JobState.Succeeded);

            Assert.Single(done.Result!.Images);
            Assert.Equal("View A", done.Label);
            Assert.NotNull(done.StartedAt);
            Assert.NotNull(done.FinishedAt);
            Assert.True(done.IsFinished);
            await Task.Delay(30); // let the last notification drain
            Assert.Equal(new[] { JobState.Queued, JobState.Running, JobState.Succeeded }, StatesOf(job.Id));
        }

        [Fact]
        public async Task Failure_IsReportedWithItsErrorCode()
        {
            GenerationJob job = _queue.Enqueue(_backend.Id, Request());
            (await _backend.WaitForCallAsync(1)).Fail(RenderErrorCode.RateLimited);

            GenerationJob done = await WaitForStateAsync(job.Id, JobState.Failed);

            Assert.Equal(RenderErrorCode.RateLimited, done.Error!.Code);
            Assert.Null(done.Result);
        }

        [Fact]
        public async Task UnexpectedBackendException_FailsOnlyThatJob()
        {
            GenerationJob crashed = _queue.Enqueue(_backend.Id, Request());
            GenerationJob healthy = _queue.Enqueue(_backend.Id, Request());
            (await _backend.WaitForCallAsync(1)).Crash();
            (await _backend.WaitForCallAsync(2)).Succeed();

            Assert.Equal(RenderErrorCode.Unknown, (await WaitForStateAsync(crashed.Id, JobState.Failed)).Error!.Code);
            await WaitForStateAsync(healthy.Id, JobState.Succeeded);
        }

        [Fact]
        public async Task UnknownBackend_FailsImmediately()
        {
            GenerationJob job = _queue.Enqueue("nope", Request());

            GenerationJob done = await WaitForStateAsync(job.Id, JobState.Failed);

            Assert.Equal(RenderErrorCode.InvalidRequest, done.Error!.Code);
        }

        [Fact]
        public async Task Concurrency_IsLimitedPerBackend()
        {
            List<GenerationJob> jobs = Enumerable.Range(0, 5).Select(_ => _queue.Enqueue(_backend.Id, Request())).ToList();
            await _backend.WaitForCallAsync(2);
            await Task.Delay(40);

            Assert.Equal(2, _backend.Calls.Count);
            Assert.Equal(3, _queue.Snapshot().Count(j => j.State == JobState.Queued));

            _backend.Calls[0].Succeed();
            await _backend.WaitForCallAsync(3);
            await Task.Delay(40);
            Assert.Equal(3, _backend.Calls.Count);
            Assert.Equal(2, _backend.PendingCount);

            foreach (int i in Enumerable.Range(1, 4))
            {
                (await _backend.WaitForCallAsync(i + 1)).Succeed();
            }

            foreach (GenerationJob job in jobs)
            {
                await WaitForStateAsync(job.Id, JobState.Succeeded);
            }
        }

        [Fact]
        public async Task Backends_HaveIndependentLimits()
        {
            var other = new ControllableBackend("other", maxConcurrency: 1);
            using var queue = new GenerationQueue(id => id == "fake" ? _backend : id == "other" ? other : null);

            queue.Enqueue("fake", Request());
            queue.Enqueue("fake", Request());
            queue.Enqueue("other", Request());
            queue.Enqueue("other", Request());

            await _backend.WaitForCallAsync(2);
            await other.WaitForCallAsync(1);
            await Task.Delay(40);
            Assert.Single(other.Calls);
        }

        [Fact]
        public async Task Dependency_ResultIsAttachedAsConsistencyReference()
        {
            ImageData firstResult = TestImages.Jpeg();
            GenerationJob first = _queue.Enqueue(_backend.Id, Request("one"));
            var options = new JobOptions();
            options.DependsOn.Add(first.Id);
            GenerationJob second = _queue.Enqueue(_backend.Id, Request("two"), options);

            await _backend.WaitForCallAsync(1);
            await Task.Delay(40);
            Assert.Single(_backend.Calls); // the second job waits although a slot is free

            _backend.Calls[0].Succeed(firstResult);
            ControllableBackend.Call secondCall = await _backend.WaitForCallAsync(2);

            ReferenceImage reference = Assert.Single(secondCall.Request.References);
            Assert.Equal(ReferenceRole.Consistency, reference.Role);
            Assert.Same(firstResult, reference.Image);
            // The stored request stays as submitted.
            Assert.Empty(_queue.Find(second.Id)!.Request.References);
            Assert.Equal(new[] { first.Id }, _queue.Find(second.Id)!.DependsOn.ToArray());
        }

        [Fact]
        public async Task FailedAndCancelledDependencies_DoNotBlockAndAreNotReferenced()
        {
            GenerationJob failed = _queue.Enqueue(_backend.Id, Request("fails"));
            GenerationJob cancelled = _queue.Enqueue(_backend.Id, Request("cancelled"));
            var options = new JobOptions();
            options.DependsOn.Add(failed.Id);
            options.DependsOn.Add(cancelled.Id);
            options.DependsOn.Add("no-such-job");
            GenerationJob dependent = _queue.Enqueue(_backend.Id, Request("after"), options);

            (await _backend.WaitForCallAsync(1)).Fail();
            await _backend.WaitForCallAsync(2);
            _queue.Cancel(cancelled.Id);

            ControllableBackend.Call call = await _backend.WaitForCallAsync(3);
            Assert.Empty(call.Request.References);
            call.Succeed();
            await WaitForStateAsync(dependent.Id, JobState.Succeeded);
        }

        [Fact]
        public async Task ConsistencyReferences_AreCappedKeepingTheMostRecent()
        {
            // Regression for legacy defect L8: every earlier result was attached without limit.
            ImageData[] results = { TestImages.Png(), TestImages.Jpeg(), TestImages.Webp(), TestImages.ProgressiveJpeg() };
            var ids = new List<string>();
            for (int i = 0; i < results.Length; i++)
            {
                ids.Add(_queue.Enqueue(_backend.Id, Request("dep" + i)).Id);
                (await _backend.WaitForCallAsync(i + 1)).Succeed(results[i]);
                await WaitForStateAsync(ids[i], JobState.Succeeded);
            }

            var options = new JobOptions { MaxConsistencyRefs = 2 };
            foreach (string id in ids)
            {
                options.DependsOn.Add(id);
            }

            _queue.Enqueue(_backend.Id, Request("final"), options);
            ControllableBackend.Call call = await _backend.WaitForCallAsync(5);

            Assert.Equal(new[] { results[2], results[3] }, call.Request.References.Select(r => r.Image).ToArray());
        }

        [Fact]
        public async Task ConsistencyReferences_RespectModelInputLimitButNeverDropUserReferences()
        {
            var limited = new ControllableBackend("limited", maxConcurrency: 4, maxInputImages: 3);
            using var queue = new GenerationQueue(id => id == "limited" ? limited : null);

            var deps = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                deps.Add(queue.Enqueue("limited", Request("dep" + i)).Id);
            }

            for (int i = 0; i < 3; i++)
            {
                (await limited.WaitForCallAsync(i + 1)).Succeed(i == 2 ? TestImages.Webp() : TestImages.Jpeg());
            }

            GenerationRequest request = Request("final");
            var userStyle = new ReferenceImage(TestImages.Png(), ReferenceRole.Style);
            request.References.Add(userStyle);
            var options = new JobOptions { MaxConsistencyRefs = 5 };
            foreach (string id in deps)
            {
                options.DependsOn.Add(id);
            }

            queue.Enqueue("limited", request, options);
            ControllableBackend.Call call = await limited.WaitForCallAsync(4);

            // source + user style reference leaves room for exactly one automatic reference: the latest.
            Assert.Equal(3, call.Request.InputImageCount);
            Assert.Contains(userStyle, call.Request.References);
            Assert.Equal("image/webp", call.Request.References.Single(r => r.Role == ReferenceRole.Consistency).Image.MimeType);
        }

        [Fact]
        public async Task Cancel_QueuedJob_EndsImmediatelyWithoutReachingTheBackend()
        {
            _queue.Enqueue(_backend.Id, Request());
            _queue.Enqueue(_backend.Id, Request());
            GenerationJob waiting = _queue.Enqueue(_backend.Id, Request());
            await _backend.WaitForCallAsync(2);

            _queue.Cancel(waiting.Id);

            Assert.Equal(JobState.Cancelled, _queue.Find(waiting.Id)!.State);
            _backend.Calls[0].Succeed();
            _backend.Calls[1].Succeed();
            await Task.Delay(50);
            Assert.Equal(2, _backend.Calls.Count);
        }

        [Fact]
        public async Task Cancel_RunningJob_AbortsTheBackendCall()
        {
            // Regression for legacy defect L6.
            GenerationJob job = _queue.Enqueue(_backend.Id, Request());
            ControllableBackend.Call call = await _backend.WaitForCallAsync(1);

            _queue.Cancel(job.Id);

            await WaitForStateAsync(job.Id, JobState.Cancelled);
            Assert.True(call.Completion.Task.IsCanceled);
        }

        [Fact]
        public async Task CancelGroup_CancelsQueuedAndRunningJobsOfThatGroupOnly()
        {
            GenerationJob outsider = _queue.Enqueue(_backend.Id, Request());
            GenerationJob a = _queue.Enqueue(_backend.Id, Request(), new JobOptions { GroupId = "g" });
            GenerationJob b = _queue.Enqueue(_backend.Id, Request(), new JobOptions { GroupId = "g" });
            await _backend.WaitForCallAsync(2);

            _queue.CancelGroup("g");

            await WaitForStateAsync(a.Id, JobState.Cancelled);
            await WaitForStateAsync(b.Id, JobState.Cancelled);
            Assert.Equal(JobState.Running, _queue.Find(outsider.Id)!.State);
        }

        [Fact]
        public async Task EnqueueBatch_WiresPlanIndexesToJobIds()
        {
            var plan = new List<PlannedJob>
            {
                new PlannedJob(Request("v1"), Array.Empty<int>(), "View 1"),
                new PlannedJob(Request("v2"), new[] { 0 }, "View 2"),
                new PlannedJob(Request("v3"), new[] { 0, 1, 7, -1 }, "View 3")
            };
            plan[1].Tags["node"] = "n2";

            IReadOnlyList<GenerationJob> jobs = _queue.EnqueueBatch(_backend.Id, plan, "batch-1");

            Assert.Equal(3, jobs.Count);
            Assert.All(jobs, j => Assert.Equal("batch-1", j.GroupId));
            Assert.Empty(jobs[0].DependsOn);
            Assert.Equal(new[] { jobs[0].Id }, jobs[1].DependsOn.ToArray());
            Assert.Equal(new[] { jobs[0].Id, jobs[1].Id }, jobs[2].DependsOn.ToArray());
            Assert.Equal("n2", jobs[1].Tags["node"]);

            // Serial chain: one call at a time even though two slots are free.
            await _backend.WaitForCallAsync(1);
            await Task.Delay(40);
            Assert.Single(_backend.Calls);
            _backend.Calls[0].Succeed();
            (await _backend.WaitForCallAsync(2)).Succeed();
            ControllableBackend.Call third = await _backend.WaitForCallAsync(3);
            Assert.Equal(2, third.Request.References.Count);
        }

        [Fact]
        public async Task Retention_DropsOldestFinishedJobsButNeverUnfinishedOnes()
        {
            using var queue = new GenerationQueue(id => _backend, maxRetainedJobs: 3);
            var ids = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                ids.Add(queue.Enqueue(_backend.Id, Request()).Id);
                (await _backend.WaitForCallAsync(i + 1)).Succeed();
            }

            while (queue.Snapshot().Any(j => !j.IsFinished))
            {
                await Task.Delay(5);
            }

            for (int i = 0; i < 3; i++)
            {
                ids.Add(queue.Enqueue(_backend.Id, Request()).Id);
            }

            IReadOnlyList<GenerationJob> retained = queue.Snapshot();
            Assert.Equal(3, retained.Count);
            Assert.Null(queue.Find(ids[0]));
            Assert.Null(queue.Find(ids[1]));
            Assert.All(retained, j => Assert.False(j.IsFinished));
        }

        [Fact]
        public async Task FaultySubscriber_DoesNotBreakTheQueue()
        {
            _queue.JobChanged += (s, e) => throw new InvalidOperationException("bad subscriber");

            GenerationJob job = _queue.Enqueue(_backend.Id, Request());
            (await _backend.WaitForCallAsync(1)).Succeed();

            await WaitForStateAsync(job.Id, JobState.Succeeded);
        }

        [Fact]
        public async Task Dispose_CancelsEverythingAndRejectsNewJobs()
        {
            GenerationJob running = _queue.Enqueue(_backend.Id, Request());
            await _backend.WaitForCallAsync(1);

            _queue.Dispose();

            await WaitForStateAsync(running.Id, JobState.Cancelled);
            Assert.Throws<ObjectDisposedException>(() => _queue.Enqueue(_backend.Id, Request()));
        }
    }
}
