@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# start.cmd <vm>: starts (or resumes from sleep) a VM, then prints its IP once the guest reports one.
param([Parameter(Mandatory)][string]$Name)
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$os = Get-CimInstance Win32_OperatingSystem
$free = $os.FreePhysicalMemory/1MB
$vm = Get-VM -Name $Name -ErrorAction Stop
$need = $vm.MemoryStartup/1GB
if ($vm.State -eq 'Off' -and $free - $need -lt 10) { throw ("Only {0:N1} GB free; J-017 keeps 10 GB free. Not starting." -f $free) }
Start-VM -VM $vm
for ($i = 0; $i -lt 60; $i++) {
  $ip = (Get-VMNetworkAdapter -VM $vm).IPAddresses | Where-Object { $_ -match '^\d+\.' } | Select-Object -First 1
  if ($ip) { "$Name is running at $ip"; break }
  Start-Sleep 5
}
if (-not $ip) { "$Name started; no IP reported yet (normal during an OS install)." }
