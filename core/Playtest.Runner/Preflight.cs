using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// Whether a heavy job (a playtest, a build) may start now (playtest/DESIGN.md,
// section 5): enough free memory, no Unity batch job (a build) and no
// Multiplayer Play Mode player running. One Editor open is fine.
namespace Playtest.Runner
{
    public static class Preflight
    {
        public sealed class State
        {
            public double FreeGB { get; set; }
            public IReadOnlyList<string> Unity { get; set; } = Array.Empty<string>();
        }

        public static string Why(State s, double minFreeGB)
        {
            ArgumentNullException.ThrowIfNull(s);
            if (s.FreeGB < minFreeGB) return $"only {s.FreeGB:0.0} GB of memory free";
            if (s.Unity.Any(c => c.Contains("-batchmode", StringComparison.OrdinalIgnoreCase))) return "a Unity batch job (a build) is running";
            if (s.Unity.Any(IsVirtualPlayer)) return "a Multiplayer Play Mode player is running";
            return null;
        }

        public static bool IsEditor(string commandLine) =>
            commandLine.Contains("-projectpath", StringComparison.OrdinalIgnoreCase) && !commandLine.Contains("AssetImportWorker", StringComparison.Ordinal);

        static bool IsVirtualPlayer(string commandLine) =>
            commandLine.Contains("mppm", StringComparison.OrdinalIgnoreCase) || commandLine.Contains(@"\VP\", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("-vpId", StringComparison.OrdinalIgnoreCase);

        public static State Now() => OperatingSystem.IsWindows() ? WindowsNow() : new State { FreeGB = double.MaxValue };

        [SupportedOSPlatform("windows")]
        static State WindowsNow()
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            GlobalMemoryStatusEx(ref status);
            var unity = new List<string>();
            using (var search = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='Unity.exe'"))
                foreach (var o in search.Get())
                {
                    string line = o["CommandLine"] as string ?? "";
                    if (!line.Contains("AssetImportWorker", StringComparison.Ordinal)) unity.Add(line);
                }
            return new State { FreeGB = status.AvailPhys / (1024.0 * 1024 * 1024), Unity = unity };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}
