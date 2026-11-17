using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Chameleon.Core.Settings;
using Xunit;

namespace Chameleon.App.Tests.Platform
{
    public class SecretStoreTests : IDisposable
    {
        private readonly TempDirectory _dir = new TempDirectory();

        private EncryptedFileSecretStore Store(string keyMaterial = "machine|user")
        {
            return new EncryptedFileSecretStore(_dir.Path, keyMaterial);
        }

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void EncryptedFile_RoundTripsOverwritesAndDeletes()
        {
            EncryptedFileSecretStore store = Store();

            Assert.Null(store.Get("provider:BltAI"));
            Assert.False(store.Contains("provider:BltAI"));

            store.Set("provider:BltAI", "test-key-123");
            store.Set("provider:Gemini", "密钥 with unicode ✓");
            Assert.Equal("test-key-123", store.Get("provider:BltAI"));
            Assert.Equal("密钥 with unicode ✓", store.Get("provider:Gemini"));
            Assert.True(store.Contains("provider:BltAI"));

            store.Set("provider:BltAI", "replaced");
            Assert.Equal("replaced", store.Get("provider:BltAI"));

            store.Delete("provider:BltAI");
            store.Delete("provider:BltAI");
            Assert.Null(store.Get("provider:BltAI"));
            Assert.Equal("密钥 with unicode ✓", store.Get("provider:Gemini"));
        }

        [Fact]
        public void EncryptedFile_PersistsAcrossInstances()
        {
            Store().Set("cloud:refreshToken", "test-refresh-token");

            Assert.Equal("test-refresh-token", Store().Get("cloud:refreshToken"));
        }

        [Fact]
        public void EncryptedFile_NeitherNameNorValueAppearsOnDisk()
        {
            Store().Set("provider:BltAI", "test-key-123");

            string file = Directory.GetFiles(_dir.Path).Single();
            string content = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)) + Path.GetFileName(file);
            Assert.DoesNotContain("test-key-123", content);
            Assert.DoesNotContain("BltAI", content);
        }

        [Fact]
        public void EncryptedFile_SameValueEncryptsDifferentlyEachTime()
        {
            EncryptedFileSecretStore store = Store();
            store.Set("a", "same");
            byte[] first = File.ReadAllBytes(Directory.GetFiles(_dir.Path).Single());
            store.Set("a", "same");
            byte[] second = File.ReadAllBytes(Directory.GetFiles(_dir.Path).Single());

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void EncryptedFile_TamperedOrForeignData_ReadsAsAbsent()
        {
            EncryptedFileSecretStore store = Store();
            store.Set("a", "secret");
            string file = Directory.GetFiles(_dir.Path).Single();

            byte[] bytes = File.ReadAllBytes(file);
            bytes[bytes.Length / 2] ^= 0x01;
            File.WriteAllBytes(file, bytes);
            Assert.Null(store.Get("a"));

            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            Assert.Null(store.Get("a"));

            store.Set("a", "secret");
            Assert.Null(Store("other-machine|other-user").Get("a"));
        }

        [Fact]
        public void Factory_OnThisPlatform_ReturnsAWorkingStore()
        {
            HostPlatform platform = HostPlatformDetector.Detect();
            ISecretStore store = SecretStoreFactory.Create(platform, AppPaths.UnderRoot(_dir.Path), new FakeEnvironment());

            Assert.True(SecretStoreFactory.Works(store));
            if (platform == HostPlatform.Other)
            {
                Assert.IsType<EncryptedFileSecretStore>(store);
            }
        }

        [Fact]
        public void Factory_FallsBackWhenTheNativeStoreIsUnusable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // DPAPI works here, so there is nothing to fall back from.
            }

            // Asking for the Windows store on a non-Windows system must not throw or return a broken store.
            ISecretStore store = SecretStoreFactory.Create(HostPlatform.Windows, AppPaths.UnderRoot(_dir.Path), new FakeEnvironment());

            Assert.IsType<EncryptedFileSecretStore>(store);
            Assert.True(SecretStoreFactory.Works(store));
        }

        [Fact]
        public void Dpapi_RoundTrips_OnWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return; // Verified by the Windows CI job and the P0 spike.
            }

            var store = new WindowsDpapiSecretStore(_dir.Path);
            store.Set("provider:BltAI", "test-key-123");

            Assert.Equal("test-key-123", store.Get("provider:BltAI"));
        }

        [Fact]
        public void Works_ReportsFalseForABrokenStore()
        {
            Assert.False(SecretStoreFactory.Works(new BrokenStore()));
            Assert.True(SecretStoreFactory.Works(new InMemorySecretStore()));
        }

        private sealed class BrokenStore : ISecretStore
        {
            public bool Contains(string name) => false;

            public void Delete(string name)
            {
            }

            public string? Get(string name) => "always the same";

            public void Set(string name, string value) => throw new InvalidOperationException("no backing store");
        }
    }
}
