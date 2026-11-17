using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.App.Bridge;
using Chameleon.App.Projects;
using Chameleon.Core.Generation;
using Chameleon.Core.Imaging;
using Chameleon.Core.Sessions;
using Xunit;

namespace Chameleon.App.Tests.Projects
{
    public class ProjectServiceTests : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();
        private readonly InMemoryAssetStore _cache = new InMemoryAssetStore();
        private readonly DiskProjectStore _store;
        private readonly ProjectService _projects;

        public ProjectServiceTests()
        {
            _store = new DiskProjectStore(Path.Combine(_directory.Path, "projects"));
            _projects = new ProjectService(_store, _cache);
        }

        public void Dispose()
        {
            _projects.Dispose();
            _directory.Dispose();
        }

        private string Cached(ImageData image) => _cache.Put(image).AssetId;

        private ImageNode Add(string asset, string? parent = null, Recipe? recipe = null)
        {
            return _projects.AddNode(new NodeSpec { AssetId = asset, ParentId = parent, Kind = parent == null ? NodeKind.Capture : NodeKind.Generation, Recipe = recipe });
        }

        private string ProjectAssets(string projectId) => Path.Combine(_directory.Path, "projects", projectId, "assets");

        [Fact]
        public void StartsWithAnEmptyTemporaryProject()
        {
            Assert.StartsWith(ProjectIds.TemporaryPrefix, _projects.CurrentProjectId);
            Assert.Empty(_projects.GetCurrent().Nodes);
        }

        [Fact]
        public void AddNode_CopiesItsImagesIntoTheProject()
        {
            string source = Cached(TestImages.Png());
            string mask = Cached(TestImages.Webp());
            ImageNode root = Add(source);

            ImageNode child = Add(Cached(TestImages.Jpeg()), root.Id, new Recipe { MaskAssetId = mask });

            Assert.Equal(root.Id, child.ParentId);
            Assert.Equal(3, Directory.GetFiles(ProjectAssets(_projects.CurrentProjectId)).Length);
            Assert.NotNull(_projects.TryGetAsset(mask));
        }

        [Fact]
        public void AddNode_RejectsUnknownImagesAndParents()
        {
            string asset = Cached(TestImages.Png());

            Assert.Equal(BridgeErrorCodes.NotFound, Assert.Throws<BridgeException>(() => Add(new string('a', 64))).Code);
            Assert.Equal(BridgeErrorCodes.NotFound, Assert.Throws<BridgeException>(() => Add(asset, "nope")).Code);
            Assert.Empty(_projects.GetCurrent().Nodes);
        }

        [Fact]
        public void AddNode_UsesSuppliedBytesForImagesStoredNowhere()
        {
            ImageData image = TestImages.Jpeg();

            ImageNode node = _projects.AddNode(_projects.CurrentProjectId, new NodeSpec { AssetId = image.Sha256Hex, Kind = NodeKind.Import }, new[] { image });

            Assert.NotNull(_projects.TryGetAsset(node.AssetId));
        }

        [Fact]
        public void DeleteNode_RemovesImagesNothingElseUses()
        {
            string shared = Cached(TestImages.Png());
            string own = Cached(TestImages.Jpeg());
            ImageNode root = Add(shared);
            ImageNode child = Add(own, root.Id, new Recipe { References = { new RecipeReference { AssetId = shared, Role = ReferenceRole.Style } } });
            ImageNode other = Add(shared);

            _projects.DeleteNode(child.Id, withDescendants: false);
            _projects.DeleteNode(root.Id, withDescendants: false);

            Assert.Null(new DiskAssetStore(ProjectAssets(_projects.CurrentProjectId)).TryGet(own));
            Assert.NotNull(_projects.TryGetAsset(shared));
            Assert.Equal(new[] { other.Id }, _projects.GetCurrent().Nodes.Select(n => n.Id));
        }

        [Fact]
        public async Task Projects_SurviveARestart()
        {
            await _projects.OpenProjectAsync("doc1", CancellationToken.None);
            ImageNode root = Add(Cached(TestImages.Png()));
            ImageNode child = Add(Cached(TestImages.Jpeg()), root.Id, new Recipe { Prompt = "dusk", ImageSize = "2K" });
            _projects.UpdateNode(child.Id, "Dusk", true);
            _projects.SetCurrent(child.Id);
            await _store.FlushAsync(CancellationToken.None);

            using var restarted = new ProjectService(new DiskProjectStore(Path.Combine(_directory.Path, "projects")), new InMemoryAssetStore());
            await restarted.OpenProjectAsync("doc1", CancellationToken.None);

            SessionSnapshot loaded = restarted.GetCurrent();
            Assert.Equal(child.Id, loaded.CurrentNodeId);
            Assert.Equal(new[] { root.Id, child.Id }, loaded.Nodes.Select(n => n.Id));
            Assert.Equal("dusk", loaded.Nodes[1].Recipe!.Prompt);
            Assert.True(loaded.Nodes[1].Starred);
            // The working cache is gone; the project still has the images.
            Assert.NotNull(restarted.TryGetAsset(child.AssetId));
        }

        [Fact]
        public async Task ResultsCanStillGoToAProjectThatIsNoLongerCurrent()
        {
            string first = _projects.CurrentProjectId;
            await _projects.OpenProjectAsync("doc2", CancellationToken.None);
            string? changed = null;
            _projects.Changed += (sender, e) => changed = e.ProjectId;

            _projects.AddNode(first, new NodeSpec { AssetId = Cached(TestImages.Png()), Kind = NodeKind.Generation });

            Assert.Equal(first, changed);
            Assert.Empty(_projects.GetCurrent().Nodes);
        }

        [Fact]
        public async Task ConcurrentAdditions_AreAllKept()
        {
            string asset = Cached(TestImages.Png());
            ImageNode root = Add(asset);

            await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => Add(asset, root.Id))));

            Assert.Equal(41, _projects.GetCurrent().Nodes.Count);
        }

        [Fact]
        public void Clear_RemovesNodesAndImages()
        {
            Add(Cached(TestImages.Png()));

            _projects.Clear();

            Assert.Empty(_projects.GetCurrent().Nodes);
            Assert.Empty(Directory.GetFiles(ProjectAssets(_projects.CurrentProjectId)));
        }

        [Fact]
        public void Dispose_RemovesTheTemporaryProject()
        {
            var store = new DiskProjectStore(Path.Combine(_directory.Path, "other"));
            var projects = new ProjectService(store, _cache);
            projects.AddNode(new NodeSpec { AssetId = Cached(TestImages.Png()), Kind = NodeKind.Capture });
            string directory = Path.Combine(_directory.Path, "other", projects.CurrentProjectId);
            Assert.True(Directory.Exists(directory));

            projects.Dispose();

            Assert.False(Directory.Exists(directory));
        }

        [Fact]
        public void AbandonedTemporaryProjects_ArePurgedOnlyWhenOld()
        {
            string root = Path.Combine(_directory.Path, "projects");
            string old = Path.Combine(root, ProjectIds.NewTemporary());
            string recent = Path.Combine(root, ProjectIds.NewTemporary());
            string saved = Path.Combine(root, "doc1");
            foreach (string directory in new[] { old, recent, saved })
            {
                Directory.CreateDirectory(directory);
            }

            Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
            Directory.SetLastWriteTimeUtc(saved, DateTime.UtcNow.AddDays(-3));

            Assert.Equal(1, _projects.PurgeAbandonedTemporaryProjects(TimeSpan.FromDays(1)));
            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(recent));
            Assert.True(Directory.Exists(saved));
        }
    }
}
