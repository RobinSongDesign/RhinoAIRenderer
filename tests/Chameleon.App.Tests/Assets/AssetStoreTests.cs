using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chameleon.App.Assets;
using Chameleon.Core.Imaging;
using Xunit;

namespace Chameleon.App.Tests.Assets
{
    public class AssetStoreTests : IDisposable
    {
        private readonly TempDirectory _dir = new TempDirectory();

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void Disk_PutThenGet_RoundTripsAndDescribesTheImage()
        {
            var store = new DiskAssetStore(_dir.Path);
            ImageData image = TestImages.Jpeg();

            AssetRef asset = store.Put(image);
            ImageData loaded = new DiskAssetStore(_dir.Path).TryGet(asset.AssetId)!;

            Assert.Equal(image.Sha256Hex, asset.AssetId);
            Assert.Equal("image/jpeg", asset.MimeType);
            Assert.Equal(80, asset.Width);
            Assert.Equal(60, asset.Height);
            Assert.Equal(image.Bytes.Length, asset.Bytes);
            Assert.Equal(image.Bytes, loaded.Bytes);
            Assert.Equal(asset.AssetId + ".jpg", Path.GetFileName(Directory.GetFiles(_dir.Path).Single()));
        }

        [Fact]
        public void Disk_SameContent_IsStoredOnce_EvenUnderConcurrency()
        {
            var store = new DiskAssetStore(_dir.Path);

            Parallel.For(0, 16, _ => store.Put(TestImages.Png()));
            store.Put(TestImages.Webp());

            Assert.Equal(2, Directory.GetFiles(_dir.Path).Length);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-an-id")]
        [InlineData("../../etc/passwd")]
        [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public void MalformedIds_AreRejectedByEveryStore(string id)
        {
            Assert.Null(new DiskAssetStore(_dir.Path).TryGet(id));
            Assert.Null(new InMemoryAssetStore().TryGet(id));
            Assert.False(AssetIds.IsValid(id));
        }

        [Fact]
        public void Disk_UnknownId_IsNull_AndCorruptFileIsNull()
        {
            var store = new DiskAssetStore(_dir.Path);
            string id = new string('a', 64);
            Assert.Null(store.TryGet(id));

            File.WriteAllText(Path.Combine(_dir.Path, id + ".png"), "not an image");
            Assert.Null(store.TryGet(id));
        }

        [Fact]
        public void Disk_PurgeStale_RemovesOnlyUnusedFiles()
        {
            var store = new DiskAssetStore(_dir.Path);
            AssetRef old = store.Put(TestImages.Png());
            AssetRef recent = store.Put(TestImages.Jpeg());
            AssetRef revived = store.Put(TestImages.Webp());
            foreach (string file in Directory.GetFiles(_dir.Path).Where(f => !f.Contains(recent.AssetId)))
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));
            }

            // Reading an asset counts as using it.
            Assert.NotNull(store.TryGet(revived.AssetId));
            int removed = store.PurgeStale(DiskAssetStore.DefaultMaxAge);

            Assert.Equal(1, removed);
            Assert.Null(store.TryGet(old.AssetId));
            Assert.NotNull(store.TryGet(recent.AssetId));
            Assert.NotNull(store.TryGet(revived.AssetId));
        }

        [Fact]
        public void InMemory_RoundTripsAndDeduplicates()
        {
            var store = new InMemoryAssetStore();
            AssetRef a = store.Put(TestImages.Png());
            AssetRef b = store.Put(TestImages.Png());

            Assert.Equal(a.AssetId, b.AssetId);
            Assert.Equal(1, store.Count);
            Assert.Equal(TestImages.Png().Bytes, store.TryGet(a.AssetId)!.Bytes);
        }
    }
}
