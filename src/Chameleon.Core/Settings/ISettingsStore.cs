using System;

namespace Chameleon.Core.Settings
{
    /// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
    public interface ISettingsStore
    {
        /// <summary>True when settings have been saved before (the backing file exists).</summary>
        bool Exists { get; }

        /// <summary>
        /// Returns a private copy of the current settings. Never throws: missing or unreadable
        /// settings yield the defaults.
        /// </summary>
        AppSettings Load();

        /// <summary>Persists the settings and raises <see cref="Changed"/>.</summary>
        void Save(AppSettings settings);

        /// <summary>Raised after every successful <see cref="Save"/>.</summary>
        event EventHandler? Changed;
    }
}
