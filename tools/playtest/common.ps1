# Shared helpers for tools/playtest/*.ps1 (dot-source it): the preflight, where
# the player build is, its commit, crashes, known issues and coverage.

# The playtest bot's own checkout, next to this one (build.ps1 makes it).
function Get-PlaytestCheckout([string]$root) {
    $common = (git -C $root rev-parse --path-format=absolute --git-common-dir).Trim()
    Join-Path (Split-Path $common -Parent) '.claude\worktrees\playtest-build'
}

# The player to run: the playtest-build checkout's build, else this checkout's own.
function Get-PlaytestExe([string]$root) {
    $own = Join-Path (Get-PlaytestCheckout $root) 'unity\Builds\Windows\SpellChemistryValley.exe'
    if (Test-Path $own) { return $own }
    Join-Path $root 'unity\Builds\Windows\SpellChemistryValley.exe'
}

# Unity Editors open on any project (not import workers or the Hub).
function Get-UnityEditors {
    Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
        Where-Object { $_.CommandLine -match '-projectpath' -and $_.CommandLine -notmatch 'AssetImportWorker' }
}

# Why a heavy job shouldn't start now, or $null: short of memory, a batch-mode Unity (a build) or a
# Multiplayer Play Mode player running. One Editor open is fine; the GPU is shared, which is why
# performance findings only count from quiet runs.
function Test-Preflight([double]$MinFreeGB = 8) {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    if ($free -lt $MinFreeGB) { return ('only {0:N1} GB of memory free' -f $free) }
    $unity = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -notmatch 'AssetImportWorker' }
    if ($unity | Where-Object { $_.CommandLine -match '-batchmode' }) { return 'a Unity batch job (a build) is running' }
    if ($unity | Where-Object { $_.CommandLine -match 'VP\\|mppm|-vpId' }) { return 'a Multiplayer Play Mode player is running' }
    $null
}

# The commit a build was made from (StreamingAssets/build.json), or 'unknown'.
function Get-BuildCommit([string]$exe) {
    $data = Join-Path (Split-Path $exe -Parent) ([IO.Path]::GetFileNameWithoutExtension($exe) + '_Data')
    $stamp = Join-Path $data 'StreamingAssets\build.json'
    if (-not (Test-Path $stamp)) { return 'unknown' }
    $j = Get-Content $stamp -Raw | ConvertFrom-Json
    ($j.commit -replace '-dirty$', '')
}

# A crash of this player process since a time, from Windows' application-error events (matched by
# the process id, so another session's test player of the same build isn't counted) and Unity's
# crash folder: a line naming the faulting module, or $null.
function Find-Crash([string]$exe, [datetime]$since, [int]$processId) {
    $name = Split-Path $exe -Leaf
    $pidText = 'Faulting process id: 0x{0:X}\b' -f $processId
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000; StartTime = $since } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match [regex]::Escape($name) -and $_.Message -match $pidText }
    if ($events) {
        $m = $events[0].Message
        $module = if ($m -match 'Faulting module name: ([^,\r\n]+)') { $Matches[1] } else { 'unknown module' }
        return "APPCRASH in $module"
    }
    $crashes = Join-Path $env:LOCALAPPDATA 'Temp\anweather\Spell Chemistry Valley\Crashes'
    $dump = Get-ChildItem $crashes -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $since } | Select-Object -First 1
    if ($dump) { return "Unity crash report $($dump.Name)" }
    $null
}

# A candidate in run.json's shape, for the runner's own detectors.
function New-Candidate([string]$detector, [string]$key, [string]$message, $run) {
    [pscustomobject]@{
        Detector = $detector; Key = $key; Category = 'GameBreaking'; Frame = [int]$run.Frames; Message = $message
        LowConfidence = $false; Goal = $(if ($run.Goals) { @($run.Goals)[-1] } else { $null }); Screenshot = $null; KnownIssue = $null
        Signature = "${detector}:$key"
    }
}

# The known issue (playtest/known-issues.json) that covers a candidate on this build, or $null:
# one whose fix is in the build is a regression, so not known. The same rule as Playtest.KnownIssues.
function Get-KnownIssue($candidate, [string]$commit, [string]$root) {
    $file = Join-Path $root 'playtest\known-issues.json'
    if (-not (Test-Path $file)) { return $null }
    foreach ($k in (Get-Content $file -Raw | ConvertFrom-Json)) {
        if ($k.detector -ne $candidate.Detector) { continue }
        if ($k.messageContains -and $candidate.Message -notmatch [regex]::Escape($k.messageContains)) { continue }
        if ($k.fixedIn -and $commit -ne 'unknown') {
            $null = cmd /c "git -C `"$root`" merge-base --is-ancestor $($k.fixedIn) $commit 2>nul"
            if ($LASTEXITCODE -eq 0) { continue }
        }
        return $k.name
    }
    $null
}

# Counts goals done in playtest/coverage.json, older counts fading by a fifth each run.
function Update-Coverage([string]$file, [string[]]$goals) {
    $counts = @{}
    if (Test-Path $file) { (Get-Content $file -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $counts[$_.Name] = [int][math]::Floor($_.Value * 0.8) } }
    foreach ($g in $goals) { $counts[$g] = 1 + $(if ($counts.ContainsKey($g)) { $counts[$g] } else { 0 }) }
    $kept = [ordered]@{}
    foreach ($k in ($counts.Keys | Sort-Object)) { if ($counts[$k] -gt 0) { $kept[$k] = $counts[$k] } }
    $kept | ConvertTo-Json | Set-Content -Encoding utf8 $file
}
