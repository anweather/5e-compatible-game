using System.Diagnostics;

// git, for the playtest runner: its output, or whether it succeeded.
namespace Playtest.Runner
{
    public static class Git
    {
        public static string Run(string dir, string arguments)
        {
            var (code, output) = Exec(dir, arguments);
            return code == 0 ? output : "";
        }

        public static bool Succeeds(string dir, string arguments) => Exec(dir, arguments).code == 0;

        static (int code, string output) Exec(string dir, string arguments)
        {
#pragma warning disable S4036
            var info = new ProcessStartInfo("git", $"-C \"{dir}\" {arguments}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
#pragma warning restore S4036
            using var p = Process.Start(info);
            string output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }
    }
}
