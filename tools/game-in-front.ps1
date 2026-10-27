# Exits 0 when the window in front belongs to the built game (someone's
# playing it), 1 otherwise. tools/probe.sh waits on it before a run.
Add-Type -Namespace Probe -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(System.IntPtr window, out int pid);
'@
$id = 0
[void][Probe.Win]::GetWindowThreadProcessId([Probe.Win]::GetForegroundWindow(), [ref]$id)
if ((Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName -eq 'SpellChemistryValley') { exit 0 }
exit 1
