using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Chameleon.App.Platform
{
    /// <summary>
    /// Appends timestamped lines to one log file per day and deletes files older than the
    /// retention period. Thread-safe; logging never throws.
    /// </summary>
    public sealed class FileLogger
    {
        /// <summary>Default number of days log files are kept.</summary>
        public const int DefaultRetentionDays = 7;

        private const string FilePrefix = "chameleon-";
        private const string FileExtension = ".log";

        private readonly string _directory;
        private readonly int _retentionDays;
        private readonly Func<DateTime> _now;
        private readonly object _gate = new object();

        /// <summary>Creates a logger and removes expired files.</summary>
        /// <param name="directory">Folder for the log files; created when missing.</param>
        /// <param name="retentionDays">Days to keep old files.</param>
        /// <param name="now">Clock (local time); injectable for tests.</param>
        public FileLogger(string directory, int retentionDays = DefaultRetentionDays, Func<DateTime>? now = null)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _retentionDays = Math.Max(1, retentionDays);
            _now = now ?? (() => DateTime.Now);
            PurgeExpired();
        }

        /// <summary>Path of today's log file.</summary>
        public string CurrentFile => PathFor(_now());

        /// <summary>Appends one line, prefixed with the time.</summary>
        public void Write(string message)
        {
            DateTime now = _now();
            string line = now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + (message ?? string.Empty) + Environment.NewLine;
            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    File.AppendAllText(PathFor(now), line, Encoding.UTF8);
                }
                catch (IOException)
                {
                    // A log line is never worth failing the operation that produced it.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void PurgeExpired()
        {
            try
            {
                if (!Directory.Exists(_directory))
                {
                    return;
                }

                DateTime oldestKept = _now().Date.AddDays(-(_retentionDays - 1));
                foreach (string file in Directory.GetFiles(_directory, FilePrefix + "*" + FileExtension))
                {
                    string stamp = Path.GetFileNameWithoutExtension(file).Substring(FilePrefix.Length);
                    if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day) &&
                        day < oldestKept)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private string PathFor(DateTime day)
        {
            return Path.Combine(_directory, FilePrefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FileExtension);
        }
    }
}
