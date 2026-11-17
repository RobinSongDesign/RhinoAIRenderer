using System.Runtime.InteropServices;
using Chameleon.App.Hosting;

namespace Chameleon.App.Platform
{
    /// <summary>Determines which operating system family the process runs on.</summary>
    public static class HostPlatformDetector
    {
        /// <summary>Returns the current platform.</summary>
        public static HostPlatform Detect()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return HostPlatform.Windows;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? HostPlatform.MacOS : HostPlatform.Other;
        }
    }
}
