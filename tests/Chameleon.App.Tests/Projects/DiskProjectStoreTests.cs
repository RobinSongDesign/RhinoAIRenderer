using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Projects;
using Chameleon.Core.Sessions;
using Xunit;

namespace Chameleon.App.Tests.Projects
{
    public class DiskProjectStoreTests : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();
        private readonly ManualDelay _delay = new ManualDelay();
        private readonly List<string> _log = new List<string>();
        private readonly DiskProjectStore _store;

        public DiskProjectStoreTests()
        {
            _store = new DiskProjectStore(_directory.Path, _log.Add, TimeSpan.FromMilliseconds(500), _delay.WaitAsync);
        }

        public void Dispose()
        {
            _store.Dispose();
            _directory.Dispose();
        }

        private string ProjectFile(string id) => Path.Combine(_directory.Path, id, "project.json");

        private static SessionSnapshot Snapshot(string id, int nodes)
        {
            var snapshot = new SessionSnapshot { ProjectId = id };
            for (int i = 0; i < nodes; i++)
            {
                snapshot.Nodes.Add(new ImageNode { Id = "n" + i, AssetId = "a" + i, Kind = NodeKind.Capture });
            }

            return snapshot;
        }

        [Fact]
        public async Task RapidSaves_AreCoalescedIntoOneWriteAfterTheDebounce()
        {
            _store.ScheduleSave(Snapshot("p1", 1));
            _store.ScheduleSave(Snapshot("p1", 2));
            _store.ScheduleSave(Snapshot("p1", 3));
            await _delay.Started(1);

            Assert.False(File.Exists(ProjectFile("p1")));
            Assert.Equal(TimeSpan.FromMilliseconds(500), _delay.Requested.Single());

            _delay.ReleaseAll();
            await _store.FlushAsync(CancellationToken.None);

            SessionSnapshot? loaded = await new DiskProjectStore(_directory.Path).LoadAsync("p1", CancellationToken.None);
            Assert.Equal(3, loaded!.Nodes.Count);
            Assert.Empty(Directory.GetFiles(Path.Combine(_directory.Path, "p1"), "*.tmp"));
        }

        [Fact]
        public async Task Load_SeesChangesNotYetWritten()
        {
            _store.ScheduleSave(Snapshot("p1", 2));

            SessionSnapshot? loaded = await _store.LoadAsync("p1", CancellationToken.None);

            Assert.Equal(2, loaded!.Nodes.Count);
        }

        [Fact]
        public async Task Flush_WritesWithoutWaitingForTheDebounce()
        {
            _store.ScheduleSave(Snapshot("p1", 1));

            await _store.FlushAsync(CancellationToken.None);

            Assert.True(File.Exists(ProjectFile("p1")));
        }

        [Fact]
        public async Task LaterSaves_ReplaceTheFile()
        {
            _store.ScheduleSave(Snapshot("p1", 1));
            await _store.FlushAsync(CancellationToken.None);
            _store.ScheduleSave(Snapshot("p1", 4));
            await _store.FlushAsync(CancellationToken.None);

            Assert.Equal(4, (await new DiskProjectStore(_directory.Path).LoadAsync("p1", CancellationToken.None))!.Nodes.Count);
        }

        [Fact]
        public async Task Load_ReturnsNullForUnknownProjects_AndAnEmptyProjectForUnreadableFiles()
        {
            Assert.Null(await _store.LoadAsync("missing", CancellationToken.None));

            Directory.CreateDirectory(Path.Combine(_directory.Path, "broken"));
            File.WriteAllText(ProjectFile("broken"), "{ not json");
            SessionSnapshot? broken = await _store.LoadAsync("broken", CancellationToken.None);

            Assert.Empty(broken!.Nodes);
            Assert.Contains(_log, l => l.Contains("broken"));
        }

        [Fact]
        public async Task DeleteProject_RemovesFilesAndDropsScheduledSaves()
        {
            _store.ScheduleSave(Snapshot("p1", 1));
            await _store.FlushAsync(CancellationToken.None);
            _store.GetAssets("p1").Put(TestImages.Png());
            _store.ScheduleSave(Snapshot("p1", 2));

            _store.DeleteProject("p1");
            await _store.FlushAsync(CancellationToken.None);

            Assert.False(Directory.Exists(Path.Combine(_directory.Path, "p1")));
        }

        [Fact]
        public void ProjectIds_CannotEscapeTheRoot()
        {
            Assert.Throws<ArgumentException>(() => _store.ScheduleSave(new SessionSnapshot { ProjectId = "../evil" }));
            Assert.Throws<ArgumentException>(() => _store.GetAssets("a/b"));
        }

        /// <summary>A delay the test releases by hand.</summary>
        private sealed class ManualDelay
        {
            private readonly List<TaskCompletionSource<bool>> _waiting = new List<TaskCompletionSource<bool>>();

            public List<TimeSpan> Requested { get; } = new List<TimeSpan>();

            public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => completion.TrySetCanceled());
                lock (_waiting)
                {
                    Requested.Add(delay);
                    _waiting.Add(completion);
                }

                return completion.Task;
            }

            public async Task Started(int count)
            {
                for (int i = 0; i < 500; i++)
                {
                    lock (_waiting)
                    {
                        if (_waiting.Count >= count)
                        {
                            return;
                        }
                    }

                    await Task.Delay(10);
                }

                throw new TimeoutException("The save never started waiting.");
            }

            public void ReleaseAll()
            {
                lock (_waiting)
                {
                    _waiting.ForEach(w => w.TrySetResult(true));
                }
            }
        }
    }
}
