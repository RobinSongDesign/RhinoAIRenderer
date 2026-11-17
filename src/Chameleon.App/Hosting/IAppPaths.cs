namespace Chameleon.App.Hosting
{
    /// <summary>
    /// Where the application keeps its files. Paths are spelled out per platform instead of being
    /// derived from Environment.SpecialFolder, whose macOS mapping differs between .NET versions
    /// (and therefore between Rhino 8 and Rhino 9).
    /// </summary>
    public interface IAppPaths
    {
        /// <summary>Full path of settings.json.</summary>
        string SettingsFile { get; }

        /// <summary>Directory holding one sub-directory per project (edit history and its images).</summary>
        string ProjectsDirectory { get; }

        /// <summary>Directory for disposable assets such as captures and results not yet in a project.</summary>
        string CacheDirectory { get; }

        /// <summary>Directory for log files.</summary>
        string LogsDirectory { get; }

        /// <summary>Directory for file-based secret stores.</summary>
        string SecretsDirectory { get; }

        /// <summary>Settings file of the WPF version, or null where that version never ran.</summary>
        string? LegacySettingsFile { get; }
    }
}
