# Runs one playtest (playtest/DESIGN.md, sections 1 and 5): the player with
# -playtest, off-screen and muted in the Windows mixer, watched by its
# heartbeat. The run lands in playtest/runs/<id>/ (run.json, shots/,
# Player.log). Before it starts, a preflight skips the run (and logs why in
# playtest/runs/skipped.log) when memory is short, a heavy Unity job is running,
# or another playtest holds the lock. Afterwards it adds a crash or hang as a
# candidate (marked known per playtest/known-issues.json and the build's
# commit) and, for a run that isn't a replay, counts its goals in
# playtest/coverage.json so the next runs lean elsewhere.
#
#   powershell -File tools/playtest/run.ps1 [-Seed <n>] [-Seconds <n>] [-Replay <run.json>] [-UntilFrame <n>] [-Exe <player.exe>] [-Force]
param(
    [string]$Exe,
    [string]$Seed,
    [int]$Seconds = 480,
    [string]$Replay,
    [int]$UntilFrame = 0,
    [string]$Id,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
$root = Resolve-Path "$PSScriptRoot\..\.."
$runs = Join-Path $root 'playtest\runs'
New-Item -ItemType Directory -Force $runs | Out-Null
if (-not $Exe) { $Exe = Get-PlaytestExe $root }

# Preflight: memory, heavy Unity jobs, the lock.
$why = Test-Preflight -MinFreeGB 8
if (-not $why -and (Test-Path (Join-Path $runs '.lock'))) { $why = 'another playtest is running' }
if (-not (Test-Path $Exe)) { $why = "no player build at $Exe" }
if ($why -and -not $Force) {
    "$(Get-Date -Format s) skipped: $why" | Add-Content (Join-Path $runs 'skipped.log')
    "skipped: $why"
    exit 0
}
Set-Content (Join-Path $runs '.lock') $PID
try {
    Add-Type -Path "$PSScriptRoot\AppMute.cs"
    if (-not $Id) { $Id = (Get-Date -Format 'yyyyMMdd-HHmmss') + $(if ($Replay) { '-replay' } else { '' }) }
    $out = Join-Path $runs $Id
    New-Item -ItemType Directory -Force $out | Out-Null

    $argv = @('-playtest', '-playtest-out', $out, '-playtest-seconds', $Seconds, '-playtest-offscreen',
              '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', (Join-Path $out 'Player.log'))
    if ($Seed) { $argv += @('-playtest-seed', $Seed) }
    if ($Replay) { $argv += @('-playtest-replay', (Resolve-Path $Replay)) }
    if ($UntilFrame -gt 0) { $argv += @('-playtest-until-frame', $UntilFrame) }
    $coverage = Join-Path $root 'playtest\coverage.json'
    if (Test-Path $coverage) { $argv += @('-playtest-coverage', $coverage) }
    # Performance findings only count from runs with no Unity Editor open. (Never -clean-quit: it brings back the old quit crash.)
    if (-not (Get-UnityEditors)) { $argv += '-playtest-quiet' }

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
    Start-Sleep -Seconds 2   # let Windows write any crash report
    $took = [int]((Get-Date) - $started).TotalSeconds
    $report = Join-Path $out 'run.json'
    $r = if (Test-Path $report) { Get-Content $report -Raw | ConvertFrom-Json } else { [pscustomobject]@{ RunId = $Id; Ended = 'no report'; Candidates = @(); Goals = @() } }
    if (-not $r.PSObject.Properties['Candidates'] -or $null -eq $r.Candidates) { $r | Add-Member -Force Candidates @() }

    # A crash or a hang becomes a candidate, known or not per playtest/known-issues.json and the build's commit.
    $commit = Get-BuildCommit $Exe
    $finished = $r.Ended -in @('done', 'until-frame')
    $crash = Find-Crash -Exe $Exe -Since $started -ProcessId $p.Id
    $extra = @()
    if ($hung) { $extra += New-Candidate 'hang' $(if ($finished) { 'quit' } else { 'running' }) "the player stopped beating for 60 s (or overran) after $took s" $r }
    elseif ($crash) { $extra += New-Candidate 'crash' $(if ($finished) { 'quit' } else { 'running' }) $crash $r }
    elseif (-not $finished) { $extra += New-Candidate 'crash' 'running' "the player exited with code $($p.ExitCode) before finishing (no crash report)" $r }
    foreach ($c in $extra) { $c.KnownIssue = Get-KnownIssue $c $commit $root }
    if ($extra) {
        $r.Candidates = @($r.Candidates) + $extra
        $r | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 $report
    }

    # Coverage: what this run did, so the next runs lean elsewhere (older runs count for less).
    if (-not $Replay -and $r.Goals) { Update-Coverage $coverage @($r.Goals) }

    "$Id : $($r.Ended) in $took s, build $commit, seed $($r.Seed), $($r.Frames) frames, $(@($r.Goals).Count) goals, $(@($r.Candidates).Count) candidates, muted $muted"
    foreach ($c in @($r.Candidates)) {
        $known = if ($c.KnownIssue) { " (known: $($c.KnownIssue))" } else { '' }
        "  [$($c.Category)] $($c.Detector):$($c.Key) at frame $($c.Frame): $($c.Message)$known"
    }
}
finally {
    Remove-Item -Force (Join-Path $runs '.lock') -ErrorAction SilentlyContinue
}
