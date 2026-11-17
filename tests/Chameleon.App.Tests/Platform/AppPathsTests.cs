using System;
using System.IO;
using Chameleon.App.Hosting;
using Chameleon.App.Platform;
using Xunit;

namespace Chameleon.App.Tests.Platform
{
    public class AppPathsTests
    {
        [Fact]
        public void Windows_UsesRoamingForSettingsAndLocalForData()
        {
            var env = new FakeEnvironment
            {
                Variables = { ["APPDATA"] = @"C:\Users\u\AppData\Roaming", ["LOCALAPPDATA"] = @"C:\Users\u\AppData\Local" }
            };

            AppPaths paths = AppPaths.ForPlatform(HostPlatform.Windows, env);

            Assert.Equal(@"C:\Users\u\AppData\Roaming\Chameleon\settings.json", paths.SettingsFile);
            Assert.Equal(@"C:\Users\u\AppData\Roaming\Chameleon\secrets", paths.SecretsDirectory);
            Assert.Equal(@"C:\Users\u\AppData\Local\Chameleon\projects", paths.ProjectsDirectory);
            Assert.Equal(@"C:\Users\u\AppData\Local\Chameleon\cache", paths.CacheDirectory);
            Assert.Equal(@"C:\Users\u\AppData\Local\Chameleon\logs", paths.LogsDirectory);
            Assert.Equal(@"C:\Users\u\AppData\Roaming\AIRenderer\settings.json", paths.LegacySettingsFile);
        }

        [Fact]
        public void Windows_WithoutLocalAppData_FallsBackToRoaming()
        {
            var env = new FakeEnvironment { Variables = { ["APPDATA"] = @"C:\R" } };

            Assert.Equal(@"C:\R\Chameleon\cache", AppPaths.ForPlatform(HostPlatform.Windows, env).CacheDirectory);
        }

        [Fact]
        public void MacOS_UsesLibraryFoldersDerivedFromHome()
        {
            // Spelled out on purpose: .NET 7 and .NET 8 disagree on SpecialFolder.ApplicationData here.
            var env = new FakeEnvironment { Variables = { ["HOME"] = "/Users/u/" } };

            AppPaths paths = AppPaths.ForPlatform(HostPlatform.MacOS, env);

            Assert.Equal("/Users/u/Library/Application Support/Chameleon/settings.json", paths.SettingsFile);
            Assert.Equal("/Users/u/Library/Application Support/Chameleon/projects", paths.ProjectsDirectory);
            Assert.Equal("/Users/u/Library/Application Support/Chameleon/secrets", paths.SecretsDirectory);
            Assert.Equal("/Users/u/Library/Caches/Chameleon", paths.CacheDirectory);
            Assert.Equal("/Users/u/Library/Logs/Chameleon", paths.LogsDirectory);
            Assert.Null(paths.LegacySettingsFile);
        }

        [Fact]
        public void Other_FollowsXdgConventions()
        {
            var env = new FakeEnvironment { Variables = { ["HOME"] = "/home/u" } };
            AppPaths byHome = AppPaths.ForPlatform(HostPlatform.Other, env);
            env.Variables["XDG_CONFIG_HOME"] = "/cfg";
            AppPaths byXdg = AppPaths.ForPlatform(HostPlatform.Other, env);

            Assert.Equal("/home/u/.config/Chameleon/settings.json", byHome.SettingsFile);
            Assert.Equal("/home/u/.cache/Chameleon", byHome.CacheDirectory);
            Assert.Equal("/cfg/Chameleon/settings.json", byXdg.SettingsFile);
            Assert.Null(byHome.LegacySettingsFile);
        }

        [Theory]
        [InlineData(HostPlatform.Windows)]
        [InlineData(HostPlatform.MacOS)]
        [InlineData(HostPlatform.Other)]
        public void MissingEnvironment_FailsWithTheVariableName(HostPlatform platform)
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => AppPaths.ForPlatform(platform, new FakeEnvironment()));

            Assert.Contains(platform == HostPlatform.Windows ? "APPDATA" : "HOME", ex.Message);
        }

        [Fact]
        public void UnderRoot_KeepsEverythingInOneDirectory()
        {
            AppPaths paths = AppPaths.UnderRoot(Path.Combine("data", "x"));

            Assert.Equal(Path.Combine("data", "x", "settings.json"), paths.SettingsFile);
            Assert.Equal(Path.Combine("data", "x", "cache"), paths.CacheDirectory);
            Assert.Null(paths.LegacySettingsFile);
        }

        [Fact]
        public void Detector_ReturnsADefinedPlatform()
        {
            Assert.True(Enum.IsDefined(typeof(HostPlatform), HostPlatformDetector.Detect()));
        }
    }
}
