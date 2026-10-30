@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# wip.cmd <worker>        (host) push a running worker's work in progress
# wip.cmd -Here [-Worker w] (inside a worker) the same, on this machine
# Saves what a worker hasn't pushed before it stops (COORDINATION, 2026-10-29:
# never stop a worker VM with unpushed work). For the repo and each of its
# worktrees (Remote Control's sessions work in worktrees): uncommitted changes
# (untracked files too; ignored ones like Library/ stay out) become one WIP
# commit on top of HEAD, made with a scratch index (git commit-tree), so the
# worktree, its index and its branch are left exactly as they were. That
# commit, or HEAD when it's clean but has commits on no remote, is pushed as
# wip/<worker>-<branch> (forced: each stop replaces the last snapshot). Exits
# non-zero, naming what failed, if any push fails; stop.cmd and sleep.cmd then
# refuse to stop the worker unless -Force. The host reaches the guest over
# PowerShell Direct (account User), as signins.cmd does; Windows workers only.
param([string]$Name, [switch]$Here, [string]$Worker, [string]$Repo = 'C:\Users\User\5e-compatible-game', [string]$GuestPassword = 'password')
$ErrorActionPreference = 'Stop'

# The guest's part: runs in the worker (or here with -Here). Native git writes
# progress to stderr, which remoting turns into errors, so git's failures are
# judged by its exit code alone.
$guest = { param([string]$repo, [string]$worker)
  $ErrorActionPreference = 'Continue'
  $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
  function G([string]$dir) {
    $out = & git -C $dir @args 2>$null
    if ($LASTEXITCODE) { throw "git $($args -join ' ') failed in $dir (exit $LASTEXITCODE)" }
    $out
  }
  if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw "No repo at $repo." }
  $trees = @(G $repo worktree list --porcelain | Where-Object { $_ -like 'worktree *' } | ForEach-Object { $_.Substring(9) })
  $failed = @()
  foreach ($wt in $trees) {
    if (-not (Test-Path -LiteralPath $wt)) { continue }
    try {
      $branch = & git -C $wt symbolic-ref --short -q HEAD
      $head = G $wt rev-parse HEAD
      if (-not $branch) { $branch = 'detached-' + $head.Substring(0, 8) }
      $commit = $head
      if (G $wt status --porcelain) {
        # A scratch index from HEAD, so the worktree's own index (staged or not) is untouched.
        $index = Join-Path $env:TEMP ("wip-index-{0}-{1}" -f $PID, [guid]::NewGuid().ToString('N'))
        $env:GIT_INDEX_FILE = $index
        try {
          G $wt read-tree HEAD | Out-Null
          G $wt add -A | Out-Null
          $tree = G $wt write-tree
        } finally { Remove-Item env:GIT_INDEX_FILE; Remove-Item -LiteralPath $index -ErrorAction SilentlyContinue }
        # A worker may have no git identity of its own.
        if (-not (& git -C $wt config user.email)) {
          $env:GIT_AUTHOR_NAME = $env:GIT_COMMITTER_NAME = "$worker (WIP)"
          $env:GIT_AUTHOR_EMAIL = $env:GIT_COMMITTER_EMAIL = "$worker@workers.invalid"
        }
        $commit = G $wt commit-tree $tree -p $head -m "WIP ($worker, $branch): uncommitted work saved before the worker stopped"
      }
      if ([int](G $wt rev-list --count $commit --not --remotes) -eq 0) { "${wt}: nothing unpushed ($branch)."; continue }
      $ref = "wip/$worker-" + ($branch -replace '[^A-Za-z0-9._/-]', '-')
      G $wt push -q --force origin "${commit}:refs/heads/$ref" | Out-Null
      "${wt}: pushed $ref ($($commit.Substring(0, 8)))."
    } catch { $failed += "${wt}: $($_.Exception.Message)" }
  }
  if ($failed) { throw ("Couldn't push the work in progress of:`n  " + ($failed -join "`n  ")) }
}

if ($Here) {
  if (-not $Worker) {
    $f = 'C:\ProgramData\worker\name.txt'
    $Worker = if (Test-Path $f) { ((Get-Content $f -Raw).Trim() -split ' ')[0] } else { $env:COMPUTERNAME }
  }
  & $guest $Repo $Worker
  return
}

if (-not $Name) { throw 'Usage: wip.cmd <worker>  (or wip.cmd -Here inside a worker)' }
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$vm = Get-VM -Name $Name
if ($Name -like '*template*' -or $Name -eq 'Windows 11 dev environment') { "$Name is a template: no work of its own."; return }
if ($vm.State -ne 'Running') { throw "$Name isn't running ($($vm.State)): rescue.cmd $Name starts it, pushes its work and shuts it down." }
if ((Get-VMFirmware -VM $vm).SecureBootTemplate -ne 'MicrosoftWindows') { throw "$Name isn't a Windows worker, so there's no PowerShell Direct: push its work from inside it (wip.cmd's guest steps by hand)." }
$cred = New-Object PSCredential('User', (ConvertTo-SecureString $GuestPassword -AsPlainText -Force))
# PowerShell Direct takes a while after a boot; a git error is final.
for ($i = 0; ; $i++) {
  try { Invoke-Command -VMName $Name -Credential $cred -ScriptBlock { 'up' } -ErrorAction Stop | Out-Null; break }
  catch { if ($i -ge 24) { throw "Can't reach $Name over PowerShell Direct (account User): $_" }; Start-Sleep 5 }
}
Invoke-Command -VMName $Name -Credential $cred -ScriptBlock $guest -ArgumentList $Repo, $Name -ErrorAction Stop
