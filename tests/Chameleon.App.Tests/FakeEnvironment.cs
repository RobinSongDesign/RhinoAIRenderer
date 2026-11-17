using System.Collections.Generic;
using Chameleon.App.Hosting;

namespace Chameleon.App.Tests
{
    /// <summary>Environment with scripted variables.</summary>
    internal sealed class FakeEnvironment : IEnvironmentInfo
    {
        public Dictionary<string, string> Variables { get; } = new Dictionary<string, string>();

        public string UserName { get; set; } = "tester";

        public string MachineName { get; set; } = "test-machine";

        public string? GetVariable(string name) => Variables.TryGetValue(name, out string? value) ? value : null;
    }
}
