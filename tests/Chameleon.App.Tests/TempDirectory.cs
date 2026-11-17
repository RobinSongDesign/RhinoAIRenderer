using System;
using System.IO;

namespace Chameleon.App.Tests
{
    /// <summary>A unique directory that is deleted when the test finishes.</summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chameleon-app-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // Leftover temp files must never fail a test run.
            }
        }
    }
}
