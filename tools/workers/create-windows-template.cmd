@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Copies Andrew's Quick Create VM "Windows 11 dev environment" into a new VM,
# windows-template (4 vCPU, 4 GB start, 4-8 GB dynamic: two fit beside J-017). The original is only
# exported (read), never changed. The copy's inherited checkpoints are merged away.
# VMs live under C:\HyperV\Workers (the 980 PRO SSD); pass -Root to change it.
param([string]$Root = 'C:\HyperV\Workers', [string]$Source = 'Windows 11 dev environment', [string]$Name = 'windows-template')
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$ErrorActionPreference = 'Stop'
if (Get-VM -Name $Name -ErrorAction SilentlyContinue) { throw "A VM named $Name already exists; not touching it." }
$tmp = Join-Path $Root 'export-tmp'
if (Test-Path $tmp) { throw "$tmp exists from an earlier run; check it and remove it first." }
"Exporting '$Source' (it may stay running) ..."
Export-VM -Name $Source -Path $tmp
$cfg = Get-ChildItem $tmp -Recurse -Filter *.vmcx | Where-Object { $_.DirectoryName -like '*Virtual Machines*' } | Select-Object -First 1
$dir = Join-Path $Root $Name
$vm = Import-VM -Path $cfg.FullName -Copy -GenerateNewId -VirtualMachinePath $dir -VhdDestinationPath "$dir\Virtual Hard Disks" -SnapshotFilePath "$dir\Snapshots" -SmartPagingFilePath $dir
Rename-VM -VM $vm -NewName $Name
Get-VMSnapshot -VM $vm | Remove-VMSnapshot
Set-VM -VM $vm -ProcessorCount 4 -DynamicMemory -MemoryStartupBytes 4GB -MemoryMinimumBytes 4GB -MemoryMaximumBytes 8GB -AutomaticCheckpointsEnabled $false -AutomaticStopAction ShutDown
Enable-VMIntegrationService -VM $vm -Name 'Guest Service Interface'
Remove-Item -Recurse $tmp
Start-VM -VM $vm
vmconnect.exe localhost $Name
"$Name created and started. Follow README step W2."
