@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# remove-worker.cmd <name>: turns a worker off, deletes the VM and its folder
# (<Root>\<name>, disk included; Hyper-V's own Delete leaves the disk behind).
# Refuses templates, the Quick Create original, and any VM whose files are not
# all inside <Root>\<name>. Its disk goes, so its work in progress is pushed
# first (wip.cmd); it refuses unless -Force if that fails, or if the worker is
# off or asleep (rescue.cmd pushes an off one's work).
param([Parameter(Mandatory)][string]$Name, [string]$Root = 'C:\HyperV\Workers', [switch]$Force)
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$ErrorActionPreference = 'Stop'
if ($Name -like '*template*' -or $Name -eq 'Windows 11 dev environment') { throw "$Name is not a worker; not removing it." }
$dir = [IO.Path]::GetFullPath((Join-Path $Root $Name)).TrimEnd('\') + '\'
$vm = Get-VM -Name $Name -ErrorAction SilentlyContinue
if ($vm) {
  $paths = @($vm.Path, $vm.ConfigurationLocation, $vm.SnapshotFileLocation) + (Get-VMHardDiskDrive -VM $vm).Path
  foreach ($p in $paths) {
    if (-not ([IO.Path]::GetFullPath($p).TrimEnd('\') + '\').StartsWith($dir, 'OrdinalIgnoreCase')) { throw "$Name has files outside $dir ($p); not removing it." }
  }
  # Never with its work unpushed (COORDINATION, 2026-10-29).
  if ($vm.State -eq 'Running') {
    cmd /c (Join-Path $env:WORKERS_DIR 'wip.cmd') $Name
    if ($LASTEXITCODE -and -not $Force) { throw "Not removing ${Name}: its work in progress couldn't be pushed (see above). Fix that, or add -Force to delete it anyway." }
  }
  elseif (-not $Force) { throw "Not removing ${Name}: it's $($vm.State), so its work in progress can't be checked. rescue.cmd $Name pushes it; or add -Force to delete it anyway." }
  # Keep its sign-in (its slot outlives it; the next clone of that name reuses it).
  if ($vm.State -eq 'Running') { cmd /c (Join-Path $env:WORKERS_DIR 'signins.cmd') pull $Name }
  if ($vm.State -ne 'Off') { Stop-VM -VM $vm -TurnOff }
  Remove-VM -VM $vm -Force
}
if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
"Removed $Name."
