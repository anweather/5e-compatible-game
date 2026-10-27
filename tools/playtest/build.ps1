# Builds the playtest bot's player (playtest/DESIGN.md, section 6) in its own
# checkout, .claude/worktrees/playtest-build, detached at origin/dev: its own
# Library keeps builds incremental and nobody's Editor is locked. It builds
# only when origin/dev has moved since the last build (or with -Force), and
# only when the preflight passes (memory, no other heavy Unity job). The
# Asset Store packs are copied in from the main checkout (they aren't in
# git); PlaytestBuild.Build builds the yard and then the Windows player.
#
#   powershell -File tools/playtest/build.ps1 [-Force]
param([switch]$Force)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
$root = Resolve-Path "$PSScriptRoot\..\.."
$main = Split-Path ((git -C $root rev-parse --path-format=absolute --git-common-dir).Trim()) -Parent
$wt = Get-PlaytestCheckout $root
$log = Join-Path $root 'playtest\runs\build.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
function Say([string]$line) { $l = "$(Get-Date -Format s) $line"; $l; $l | Add-Content $log }

git -C $main fetch -q origin dev
$target = (git -C $main rev-parse origin/dev).Trim()
$stamp = Join-Path $wt 'unity\Builds\playtest-built-commit.txt'
if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $target)) { Say "up to date at $target"; exit 0 }
$why = Test-Preflight -MinFreeGB 10
if ($why -and -not $Force) { Say "skipped: $why"; exit 0 }

# The checkout is the bot's own throwaway tree (COORDINATION.md): the yard it generates there, and
# NetWizard.prefab with it, are never committed or pushed, and each build starts from origin/dev
# exactly. This script never commits or pushes; the reset is only ever for that one tree.
if ((Split-Path $wt -Leaf) -ne 'playtest-build') { Say "FAILED: refusing to reset $wt, which isn't the playtest-build checkout"; exit 1 }
if (-not (Test-Path $wt)) {
    Say "making the playtest-build checkout at $wt"
    git -C $main worktree add --detach $wt $target
} else {
    git -C $wt checkout -q --detach $target
    git -C $wt reset -q --hard $target
    if ($LASTEXITCODE -ne 0) { Say "FAILED: couldn't reset $wt to $target"; exit 1 }
}

# The Asset Store packs, copied from the main checkout (never edited there).
$packs = (Get-Content (Join-Path $main 'unity\AssetPacks.json') -Raw | ConvertFrom-Json).packs
foreach ($pack in $packs) {
    $from = Join-Path $main "unity\$($pack.folder)"
    $to = Join-Path $wt "unity\$($pack.folder)"
    if ((Test-Path $to) -or -not (Test-Path $from)) { continue }
    Say "copying $($pack.folder)"
    robocopy $from $to /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if (Test-Path "$from.meta") { Copy-Item "$from.meta" "$to.meta" }
}

$version = ((Get-Content (Join-Path $wt 'unity\ProjectSettings\ProjectVersion.txt') | Select-String 'm_EditorVersion:') -split ':\s*')[1].Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
$unityLog = Join-Path $wt 'unity\Builds\playtest-build.log'
New-Item -ItemType Directory -Force (Split-Path $unityLog) | Out-Null
Say "building $target with Unity $version"
$started = Get-Date
$p = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-quit', '-projectPath', (Join-Path $wt 'unity'), '-executeMethod', 'PlaytestBuild.Build', '-logFile', $unityLog) -PassThru -Wait
$took = [int]((Get-Date) - $started).TotalSeconds
if ($p.ExitCode -ne 0) { Say "FAILED after $took s (exit $($p.ExitCode)); see $unityLog"; exit 1 }
Set-Content $stamp $target
Say "built $target in $took s"
