@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# new-worker.cmd <template> <name>: clones a worker VM from a sealed template
# (a full copy with a new id and MAC). It is left off; start it with start.cmd.
# VMs live under C:\HyperV\Workers (the 980 PRO SSD); pass -Root to change it.
param([Parameter(Mandatory)][string]$Template, [Parameter(Mandatory)][string]$Name, [string]$Root = 'C:\HyperV\Workers')
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$ErrorActionPreference = 'Stop'
$dir = Join-Path $Root $Name
if (Get-VM -Name $Name -ErrorAction SilentlyContinue) { throw "A VM named $Name already exists." }
if (Test-Path $dir) { throw "$dir already exists." }
$cfg = Get-ChildItem (Join-Path $Root "templates\$Template") -Recurse -Filter *.vmcx -ErrorAction SilentlyContinue | Where-Object { $_.DirectoryName -like '*Virtual Machines*' } | Select-Object -First 1
if (-not $cfg) { throw "No sealed template at $Root\templates\$Template (run seal-template.cmd)." }
$vm = Import-VM -Path $cfg.FullName -Copy -GenerateNewId -VirtualMachinePath $dir -VhdDestinationPath "$dir\Virtual Hard Disks" -SnapshotFilePath "$dir\Snapshots" -SmartPagingFilePath $dir
Rename-VM -VM $vm -NewName $Name
"Cloned $Name from $Template. Start it with: start.cmd $Name"
