# Runs one playtest (playtest/DESIGN.md, section 1): the player with
# -playtest, off-screen and muted in the Windows mixer, watched by its
# heartbeat. The run lands in playtest/runs/<id>/ (run.json, shots/,
# Player.log). A player that stops beating for 60 s, or overruns, is stopped
# and the run is marked hung.
#
#   powershell -File tools/playtest/run.ps1 [-Seed <n>] [-Seconds <n>] [-Replay <run.json>] [-UntilFrame <n>] [-Exe <player.exe>]
param(
    [string]$Exe = "$PSScriptRoot\..\..\unity\Builds\Windows\SpellChemistryValley.exe",
    [string]$Seed,
    [int]$Seconds = 480,
    [string]$Replay,
    [int]$UntilFrame = 0,
    [string]$Id
)
$ErrorActionPreference = 'Stop'
Add-Type -Path "$PSScriptRoot\AppMute.cs"
$root = Resolve-Path "$PSScriptRoot\..\.."
if (-not $Id) { $Id = (Get-Date -Format 'yyyyMMdd-HHmmss') + $(if ($Replay) { '-replay' } else { '' }) }
$out = Join-Path $root "playtest\runs\$Id"
New-Item -ItemType Directory -Force $out | Out-Null

$argv = @('-playtest', '-playtest-out', $out, '-playtest-seconds', $Seconds, '-playtest-offscreen',
          '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', (Join-Path $out 'Player.log'))
if ($Seed) { $argv += @('-playtest-seed', $Seed) }
if ($Replay) { $argv += @('-playtest-replay', (Resolve-Path $Replay)) }
if ($UntilFrame -gt 0) { $argv += @('-playtest-until-frame', $UntilFrame) }
$coverage = Join-Path $root 'playtest\coverage.json'
if (Test-Path $coverage) { $argv += @('-playtest-coverage', $coverage) }
# Performance findings only count from runs with no Unity Editor open.
$editors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match '-projectpath' -and $_.CommandLine -notmatch 'AssetImportWorker' }
if (-not $editors) { $argv += '-playtest-quiet' }

$started = Get-Date
$p = Start-Process -FilePath (Resolve-Path $Exe) -ArgumentList $argv -PassThru
$muted = $false; $hung = $false
$heartbeat = Join-Path $out 'heartbeat.txt'
$limit = [math]::Max(300, $Seconds * 3)
while (-not $p.HasExited) {
    Start-Sleep -Milliseconds 250
    if (-not $muted) { $muted = [AppMute]::Mute($p.Id) -gt 0 }
    $age = if (Test-Path $heartbeat) { ((Get-Date) - (Get-Item $heartbeat).LastWriteTime).TotalSeconds } else { ((Get-Date) - $started).TotalSeconds - 60 }
    if ($age -gt 60 -or ((Get-Date) - $started).TotalSeconds -gt $limit) {
        $hung = $true
        Stop-Process -Id $p.Id -Force
        break
    }
}
$took = [int]((Get-Date) - $started).TotalSeconds
$report = Join-Path $out 'run.json'
if ($hung -or -not (Test-Path $report)) {
    # The bot never finished: a hang or a crash, filed by triage from Player.log.
    @{ RunId = $Id; Ended = $(if ($hung) { 'hung' } else { 'crashed' }); RealSeconds = $took } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $out 'run.json')
}
$r = Get-Content $report -Raw | ConvertFrom-Json
"$Id : $($r.Ended) in $took s, seed $($r.Seed), $($r.Frames) frames, $(@($r.Goals).Count) goals, $(@($r.Candidates).Count) candidates, muted $muted"
foreach ($c in @($r.Candidates)) { "  [$($c.Category)] $($c.Detector):$($c.Key) at frame $($c.Frame): $($c.Message)" }
