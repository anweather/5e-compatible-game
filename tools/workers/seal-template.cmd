@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# seal-template.cmd <vm>: shuts the VM down, takes a checkpoint named "template"
# and exports that checkpoint as a flat VM to <Root>\templates\<vm>, which
# new-worker.cmd clones from. Refuses to overwrite an earlier export.
# -Replace first deletes the earlier 'template' checkpoint (merged into the VM,
# nothing in it is lost) and the earlier export, for refreshing a template.
param([Parameter(Mandatory)][string]$Name, [string]$Root = 'C:\HyperV\Workers', [switch]$Replace)
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$ErrorActionPreference = 'Stop'
$out = Join-Path $Root "templates\$Name"
if ($Name -notlike '*template*') { throw "$Name is not a template (its name has no 'template')." }
if ($Replace) {
  Get-VMSnapshot -VMName $Name -Name template -ErrorAction SilentlyContinue | Remove-VMSnapshot
  if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
}
if (Test-Path $out) { throw "$out exists. Andrew removes the old export before re-sealing." }
$vm = Get-VM -Name $Name
if (Get-VMSnapshot -VM $vm -Name template -ErrorAction SilentlyContinue) { throw "$Name already has a 'template' checkpoint; rename or remove it first." }
if ($vm.State -ne 'Off') { Stop-VM -VM $vm }
$cp = Checkpoint-VM -VM $vm -SnapshotName template -Passthru
Export-VMSnapshot -VMSnapshot $cp -Path $out
"Sealed: checkpoint 'template' on $Name, exported to $out"
