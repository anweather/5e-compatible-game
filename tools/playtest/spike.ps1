# Phase 1 spike for the playtest bot (playtest/DESIGN.md, section 5): runs the
# player with -playtest in four ways and records whether each rendered, took
# screenshots, heard its sound and stole the focus. Results go to
# playtest/runs/spike-<name>/ (not in git).
#
#   powershell -File tools/playtest/spike.ps1 [-Exe <player.exe>]
param(
    [string]$Exe = "$PSScriptRoot\..\..\unity\Builds\Windows\SpellChemistryValley.exe",
    [int]$Seconds = 12
)
$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Fg {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    public static uint Pid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
}
"@
$root = Resolve-Path "$PSScriptRoot\..\.."
$exe = Resolve-Path $Exe
$common = @('-playtest', '-playtest-seconds', $Seconds, '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720')
$cases = @(
    @{ Name = 'window';    Style = 'Normal';    Extra = @() },
    @{ Name = 'minimised'; Style = 'Minimized'; Extra = @() },
    @{ Name = 'offscreen'; Style = 'Normal';    Extra = @('-playtest-offscreen') },
    @{ Name = 'muted';     Style = 'Normal';    Extra = @('-playtest-offscreen', '-playtest-mute') }
)
foreach ($c in $cases) {
    $out = Join-Path $root "playtest\runs\spike-$($c.Name)"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Force $out | Out-Null
    $argv = $common + $c.Extra + @('-playtest-out', $out, '-logFile', (Join-Path $out 'Player.log'))
    $started = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList $argv -WindowStyle $c.Style -PassThru
    $stole = 0; $samples = 0
    while (-not $p.HasExited -and ((Get-Date) - $started).TotalSeconds -lt 180) {
        Start-Sleep -Milliseconds 500
        $samples++
        if ([Fg]::Pid() -eq $p.Id) { $stole++ }
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $timedOut = $true } else { $timedOut = $false }
    $took = [int]((Get-Date) - $started).TotalSeconds
    $result = Join-Path $out 'result.json'
    "== $($c.Name): $took s, timed out: $timedOut, foreground $stole of $samples samples"
    if (Test-Path $result) { Get-Content $result } else { "   no result.json" }
}
