@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Creates the Ubuntu 24.04 template VM (Gen 2, 2 vCPU, 4 GB start, 2-8 GB dynamic,
# 128 GB dynamic disk, Default Switch) and boots it from the official server ISO.
# Andrew then runs the installer in the VM window (README step U2).
# VMs live under C:\HyperV\Workers (the 980 PRO SSD); pass -Root to change it.
param([string]$Root = 'C:\HyperV\Workers', [string]$Name = 'ubuntu-template')
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
if (Get-VM -Name $Name -ErrorAction SilentlyContinue) { throw "A VM named $Name already exists; not touching it." }
New-Item -ItemType Directory -Force (Join-Path $Root 'iso') | Out-Null
$base = 'https://releases.ubuntu.com/24.04'
# The list holds several point releases, oldest first: take the newest server ISO.
$sums = [Text.Encoding]::ASCII.GetString((Invoke-WebRequest "$base/SHA256SUMS" -UseBasicParsing).Content)
$line = ($sums -split "`n") | Where-Object { $_ -match 'live-server-amd64\.iso' } | Select-Object -Last 1
$hash, $file = $line.Trim() -split '\s+\*?'
$iso = Join-Path $Root "iso\$file"
if (-not (Test-Path $iso)) { "Downloading $file ..."; Start-BitsTransfer -Source "$base/$file" -Destination $iso }
if ((Get-FileHash $iso -Algorithm SHA256).Hash -ne $hash) { throw "$iso fails its SHA256 check" }
New-VM -Name $Name -Generation 2 -MemoryStartupBytes 4GB -Path $Root -NewVHDPath (Join-Path $Root "$Name\$Name.vhdx") -NewVHDSizeBytes 128GB -SwitchName 'Default Switch' | Out-Null
Set-VM -Name $Name -ProcessorCount 2 -DynamicMemory -MemoryMinimumBytes 2GB -MemoryMaximumBytes 8GB -AutomaticCheckpointsEnabled $false -AutomaticStopAction ShutDown
Set-VMFirmware -VMName $Name -SecureBootTemplate MicrosoftUEFICertificateAuthority
Add-VMDvdDrive -VMName $Name -Path $iso
Set-VMFirmware -VMName $Name -FirstBootDevice (Get-VMDvdDrive -VMName $Name)
Enable-VMIntegrationService -VMName $Name -Name 'Guest Service Interface'
Start-VM -Name $Name
vmconnect.exe localhost $Name
"$Name created and booting the installer. Follow README step U2."
