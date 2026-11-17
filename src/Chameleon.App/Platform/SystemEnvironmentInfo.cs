using System;
using Chameleon.App.Hosting;

namespace Chameleon.App.Platform
{
    /// <summary><see cref="IEnvironmentInfo"/> backed by the real process environment.</summary>
    public sealed class SystemEnvironmentInfo : IEnvironmentInfo
    {
        /// <inheritdoc />
        public string UserName => Environment.UserName;

        /// <inheritdoc />
        public string MachineName => Environment.MachineName;

        /// <inheritdoc />
        public string? GetVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }
    }
}
