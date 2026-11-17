namespace Chameleon.App.Hosting
{
    /// <summary>The few facts about the process environment the app layer depends on; injectable for tests.</summary>
    public interface IEnvironmentInfo
    {
        /// <summary>Returns an environment variable, or null when it is not set.</summary>
        string? GetVariable(string name);

        /// <summary>Name of the signed-in operating system user.</summary>
        string UserName { get; }

        /// <summary>Name of the machine.</summary>
        string MachineName { get; }
    }
}
