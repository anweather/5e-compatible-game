@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Lists the VMs with state, CPU and memory (assigned and demand = what the guest wants now).
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
Get-VM | Format-Table Name, State, CPUUsage, ProcessorCount,
  @{n='AssignedGB';e={[math]::Round($_.MemoryAssigned/1GB,1)}},
  @{n='DemandGB';e={[math]::Round($_.MemoryDemand/1GB,1)}}, Uptime -AutoSize
$os = Get-CimInstance Win32_OperatingSystem
"Host free: {0} GB" -f [math]::Round($os.FreePhysicalMemory/1MB,1)
