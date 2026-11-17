using System;
using System.IO;

namespace Chameleon.Core.Tests
{
    /// <summary>Loads files from the Fixtures folder copied next to the test assembly.</summary>
    internal static class TestFixtures
    {
        public static string PathOf(string relativePath)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public static byte[] ReadBytes(string relativePath) => File.ReadAllBytes(PathOf(relativePath));

        public static string ReadText(string relativePath) => File.ReadAllText(PathOf(relativePath));
    }
}
