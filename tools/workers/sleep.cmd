@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# sleep.cmd <vm> [-Force]: pushes its work in progress (wip.cmd), then saves the VM's state
# to disk and frees all its RAM. start.cmd resumes it in seconds.
param([Parameter(Mandatory)][string]$Name, [switch]$Force)
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
# Never with its work unpushed (COORDINATION, 2026-10-29): wip.cmd pushes it as
# wip/<worker>-<branch> first; if that fails, refuse unless -Force.
if ((Get-VM -Name $Name -ErrorAction Stop).State -eq 'Running') {
  cmd /c (Join-Path $env:WORKERS_DIR 'wip.cmd') $Name
  if ($LASTEXITCODE) {
    if (-not $Force) { throw "Not sleeping ${Name}: its work in progress couldn't be pushed (see above). Fix that, or add -Force to go ahead with the work left unpushed." }
    "-Force: sleeping $Name with its work in progress unpushed."
  }
}
cmd /c (Join-Path $env:WORKERS_DIR 'signins.cmd') pull $Name   # save its sign-in first (skips templates, Ubuntu)
Save-VM -Name $Name -ErrorAction Stop
"$Name saved (asleep); its RAM is back on the host."
