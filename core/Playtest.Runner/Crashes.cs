using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

// A crash of the player the runner started (playtest/DESIGN.md, section 3):
// from Windows' application-error events, matched by the process id (so
// another session's test player of the same build is never counted), or
// Unity's crash folder.
namespace Playtest.Runner
{
    public static class Crashes
    {
        /// <summary>A line naming the faulting module, or null (and always null off Windows).</summary>
        public static string Find(string exe, DateTime since, int processId)
        {
            if (!OperatingSystem.IsWindows()) return null;
            return FromEvents(Path.GetFileName(exe), since, processId) ?? FromUnityFolder(since);
        }

        /// <summary>Whether an application-error message is about this process: its name, and "Faulting process id: 0x…" for its id.</summary>
        public static bool IsAbout(string message, string exeName, int processId) =>
            message != null && message.Contains(exeName, StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(message, $@"Faulting process id: 0x{processId.ToString("X", CultureInfo.InvariantCulture)}\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        /// <summary>The faulting module named in an application-error message.</summary>
        public static string Module(string message)
        {
            var m = Regex.Match(message ?? "", @"Faulting module name: ([^,\r\n]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return m.Success ? m.Groups[1].Value.Trim() : "unknown module";
        }

        [SupportedOSPlatform("windows")]
        static string FromEvents(string exeName, DateTime since, int processId)
        {
            using var log = new EventLog("Application");
            var hit = log.Entries.Cast<EventLogEntry>()
                .Where(e => e.InstanceId == 1000 && e.TimeGenerated >= since)
                .FirstOrDefault(e => IsAbout(e.Message, exeName, processId));
            return hit == null ? null : "APPCRASH in " + Module(hit.Message);
        }

        static string FromUnityFolder(DateTime since)
        {
            string folder = Path.Combine(Path.GetTempPath(), "anweather", "Spell Chemistry Valley", "Crashes");
            if (!Directory.Exists(folder)) return null;
            var dump = new DirectoryInfo(folder).GetDirectories().FirstOrDefault(d => d.LastWriteTime >= since);
            return dump == null ? null : "Unity crash report " + dump.Name;
        }
    }
}
