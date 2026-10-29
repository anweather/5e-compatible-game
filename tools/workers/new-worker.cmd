@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# new-worker.cmd <template> <name>: clones a worker VM from a sealed template
# (a full copy with a new id; the MAC stays the same per name). It is left off; start it with start.cmd.
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
# Windows workers: 4 GB start, 8 GB cap, so two run side by side within J-017 (busy Unity peaked at 7.8 GB).
if ((Get-VMFirmware -VM $vm).SecureBootTemplate -eq 'MicrosoftWindows') { Set-VMMemory -VM $vm -StartupBytes 4GB -MinimumBytes 4GB -MaximumBytes 8GB }
# The same MAC every time this name is cloned: Unity's licence file is bound to
# it (with the Windows product id and BIOS serial, which clones share), so a
# licence saved in the worker's sign-in slot stays valid. Chosen once per name,
# from Hyper-V's own prefix (00-15-5D), outside the range it hands out (01).
$macFile = Join-Path $Root "macs\$Name.txt"
if (-not (Test-Path $macFile)) {
  New-Item -ItemType Directory -Force (Split-Path $macFile) | Out-Null
  $b = [byte[]]::new(2); [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  Set-Content -LiteralPath $macFile -Value ('00155DF0{0:X2}{1:X2}' -f $b[0], $b[1])
}
$mac = (Get-Content -LiteralPath $macFile -Raw).Trim()
Get-VMNetworkAdapter -VM $vm | Set-VMNetworkAdapter -StaticMacAddress $mac
"Cloned $Name from $Template (MAC $mac). Start it with: start.cmd $Name"
