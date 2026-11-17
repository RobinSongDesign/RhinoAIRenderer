using System;
using Chameleon.Core.Settings;

namespace Chameleon.App.Services
{
    /// <summary>
    /// Serializes read-modify-write cycles on the settings store so concurrent RPC calls cannot
    /// overwrite each other's changes.
    /// </summary>
    public sealed class SettingsEditor
    {
        private readonly ISettingsStore _store;
        private readonly object _gate = new object();

        /// <summary>Creates an editor over a store.</summary>
        public SettingsEditor(ISettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Returns a private copy of the current settings.</summary>
        public AppSettings Read()
        {
            return _store.Load();
        }

        /// <summary>Applies a change and saves; returns the settings after the change.</summary>
        public AppSettings Update(Action<AppSettings> change)
        {
            lock (_gate)
            {
                AppSettings settings = _store.Load();
                change(settings);
                _store.Save(settings);
                return settings;
            }
        }
    }
}
