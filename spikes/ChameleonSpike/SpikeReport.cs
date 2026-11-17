using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Rhino;

namespace ChameleonSpike
{
    /// <summary>Outcome of one check.</summary>
    public sealed class CheckResult
    {
        /// <summary>Check id from docs/plan/phase-0-spikes.md, e.g. "A3".</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>True, false, or null when the check did not run.</summary>
        [JsonProperty("passed")]
        public bool? Passed { get; set; }

        /// <summary>Measured value or error text.</summary>
        [JsonProperty("detail")]
        public string Detail { get; set; } = string.Empty;

        /// <summary>How long the check took.</summary>
        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }
    }

    /// <summary>Collects check results from the host and the page and writes the report file.</summary>
    public sealed class SpikeReport
    {
        /// <summary>Every check the spike knows about, in display order.</summary>
        public static readonly string[] AllIds =
        {
            "A1", "A2", "A3", "A4", "A5", "A6", "A7",
            "B1", "B2", "B3", "B4",
            "C1", "C2", "C3", "C4",
            "D1", "D2", "D3", "D4", "D5", "D6",
            "E1", "E2",
            "F1", "F2",
            "G1",
            "H1", "H2", "H3", "H4"
        };

        private readonly object _gate = new object();
        private readonly Dictionary<string, CheckResult> _results = new Dictionary<string, CheckResult>(StringComparer.Ordinal);

        /// <summary>Raised whenever a result is recorded.</summary>
        public event EventHandler? Changed;

        /// <summary>Records or replaces the result of a check.</summary>
        public void Set(string id, bool? passed, string detail, long durationMs = 0)
        {
            lock (_gate)
            {
                _results[id] = new CheckResult { Id = id, Passed = passed, Detail = detail ?? string.Empty, DurationMs = durationMs };
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>True when a result exists for the check.</summary>
        public bool Has(string id)
        {
            lock (_gate)
            {
                return _results.ContainsKey(id);
            }
        }

        /// <summary>Serializes the full report, including checks that never ran.</summary>
        public string ToJson()
        {
            var checks = new List<CheckResult>();
            lock (_gate)
            {
                foreach (string id in AllIds)
                {
                    checks.Add(_results.TryGetValue(id, out CheckResult? result)
                        ? result
                        : new CheckResult { Id = id, Passed = null, Detail = "not run" });
                }
            }

            var report = new
            {
                generatedAt = DateTimeOffset.Now.ToString("o"),
                platform = Rhino.Runtime.HostUtils.RunningOnOSX ? "mac" : "win",
                os = RuntimeInformation.OSDescription,
                rhinoVersion = RhinoApp.Version.ToString(),
                runtime = RuntimeInformation.FrameworkDescription,
#if NET48
                pluginTargetFramework = "net48",
#else
                pluginTargetFramework = "net7.0",
#endif
                checks
            };
            return JsonConvert.SerializeObject(report, Formatting.Indented);
        }

        /// <summary>Writes the report to the desktop and returns the path.</summary>
        public string Save()
        {
            string path = Path.Combine(DesktopDirectory(), "chameleon-spike-report.json");
            File.WriteAllText(path, ToJson());
            return path;
        }

        /// <summary>The user's desktop folder, falling back to the home folder.</summary>
        public static string DesktopDirectory()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
            {
                return desktop;
            }

            string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string candidate = Path.Combine(home, "Desktop");
            return Directory.Exists(candidate) ? candidate : home;
        }
    }
}
