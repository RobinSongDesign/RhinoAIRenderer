using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chameleon.App.Platform;
using Xunit;

namespace Chameleon.App.Tests.Platform
{
    public class FileLoggerTests : IDisposable
    {
        private readonly TempDirectory _dir = new TempDirectory();

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void Write_AppendsTimestampedLinesToTodaysFile()
        {
            var now = new DateTime(2026, 11, 17, 9, 30, 15, 250);
            var logger = new FileLogger(Path.Combine(_dir.Path, "logs"), now: () => now);

            logger.Write("first");
            logger.Write("second");

            Assert.EndsWith("chameleon-20261117.log", logger.CurrentFile);
            Assert.Equal(new[] { "09:30:15.250  first", "09:30:15.250  second" }, File.ReadAllLines(logger.CurrentFile));
        }

        [Fact]
        public void Write_RollsOverToANewFileEachDay()
        {
            var now = new DateTime(2026, 11, 17, 23, 59, 0);
            var logger = new FileLogger(_dir.Path, now: () => now);

            logger.Write("late");
            now = now.AddMinutes(2);
            logger.Write("early");

            Assert.Equal(new[] { "chameleon-20261117.log", "chameleon-20261118.log" }, Directory.GetFiles(_dir.Path).Select(Path.GetFileName).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void Constructor_RemovesFilesOlderThanTheRetentionPeriodOnly()
        {
            foreach (string day in new[] { "20261101", "20261110", "20261111", "20261117" })
            {
                File.WriteAllText(Path.Combine(_dir.Path, "chameleon-" + day + ".log"), "x");
            }

            File.WriteAllText(Path.Combine(_dir.Path, "other.log"), "not ours");

            _ = new FileLogger(_dir.Path, retentionDays: 7, now: () => new DateTime(2026, 11, 17, 12, 0, 0));

            Assert.Equal(
                new[] { "chameleon-20261111.log", "chameleon-20261117.log", "other.log" },
                Directory.GetFiles(_dir.Path).Select(Path.GetFileName).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void Write_IsSafeFromManyThreads()
        {
            var logger = new FileLogger(_dir.Path);

            Parallel.For(0, 200, i => logger.Write("line " + i));

            Assert.Equal(200, File.ReadAllLines(logger.CurrentFile).Length);
        }
    }
}
