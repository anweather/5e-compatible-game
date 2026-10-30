@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# stop.cmd <vm> [-Force]: pushes its work in progress (wip.cmd), then asks the guest OS
# to shut down cleanly (no forced power-off).
param([Parameter(Mandatory)][string]$Name, [switch]$Force)
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
# Never with its work unpushed (COORDINATION, 2026-10-29): wip.cmd pushes it as
# wip/<worker>-<branch> first; if that fails, refuse unless -Force.
$vm = Get-VM -Name $Name -ErrorAction Stop
# Linux workers over SSH (linux.sh), Windows ones over PowerShell Direct.
$linux = (Get-VMFirmware -VM $vm).SecureBootTemplate -ne 'MicrosoftWindows' -and $Name -notlike '*template*'
$ip = (Get-VMNetworkAdapter -VM $vm).IPAddresses | Where-Object { $_ -match '^\d+\.' } | Select-Object -First 1
$sh = (Join-Path $env:WORKERS_DIR 'linux.sh') -replace '\\', '/'
function Linux([string]$what) { & 'C:\Program Files\Git\bin\bash.exe' $sh $what $Name $ip }
if ($vm.State -eq 'Running') {
  if ($linux) { Linux wip } else { cmd /c (Join-Path $env:WORKERS_DIR 'wip.cmd') $Name }
  if ($LASTEXITCODE) {
    if (-not $Force) { throw "Not stopping ${Name}: its work in progress couldn't be pushed (see above). Fix that, or add -Force to go ahead with the work left unpushed." }
    "-Force: stopping $Name with its work in progress unpushed."
  }
}
# Save its sign-ins first (signins.cmd skips templates and Linux workers).
if ($linux) { if ($vm.State -eq 'Running') { Linux pull } } else { cmd /c (Join-Path $env:WORKERS_DIR 'signins.cmd') pull $Name }
# What it used since start.cmd reset Hyper-V's meter: one line per run in metering.csv.
$m = Measure-VM -Name $Name -ErrorAction SilentlyContinue
if ($m -and $m.MeteringDuration) {
  $row = [pscustomobject]@{ Stopped = (Get-Date -Format 'yyyy-MM-dd HH:mm'); Worker = $Name; Minutes = [math]::Round($m.MeteringDuration.TotalMinutes)
    AvgCpuMHz = $m.AvgCPU; AvgRamGB = [math]::Round($m.AvgRAM / 1KB, 1); MaxRamGB = [math]::Round($m.MaxRAM / 1KB, 1)
    DiskReadGB = [math]::Round($m.AggregatedDiskDataRead / 1KB, 1); DiskWrittenGB = [math]::Round($m.AggregatedDiskDataWritten / 1KB, 1) }
  $row | Export-Csv -Append -NoTypeInformation -LiteralPath 'C:\HyperV\Workers\metering.csv'
  "{0}: {1} min, CPU avg {2} MHz, RAM avg {3} GB, peak {4} GB, disk read {5} GB, written {6} GB." -f $Name, $row.Minutes, $row.AvgCpuMHz, $row.AvgRamGB, $row.MaxRamGB, $row.DiskReadGB, $row.DiskWrittenGB
}
Stop-VM -Name $Name -ErrorAction Stop
"$Name shut down."
