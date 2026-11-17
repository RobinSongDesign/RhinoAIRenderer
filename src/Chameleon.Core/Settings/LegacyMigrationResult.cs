namespace Chameleon.Core.Settings
{
    /// <summary>Outcome of <see cref="LegacySettingsMigrator.TryMigrate"/>.</summary>
    public sealed class LegacyMigrationResult
    {
        /// <summary>Creates a result.</summary>
        public LegacyMigrationResult(bool migrated, int keysMigrated)
        {
            Migrated = migrated;
            KeysMigrated = keysMigrated;
        }

        /// <summary>Result used when nothing was migrated.</summary>
        public static LegacyMigrationResult NotMigrated { get; } = new LegacyMigrationResult(false, 0);

        /// <summary>True when legacy settings were imported.</summary>
        public bool Migrated { get; }

        /// <summary>Number of API keys moved into the secret store.</summary>
        public int KeysMigrated { get; }
    }
}
