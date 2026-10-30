@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
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
# J-017 keeps 10 GB free beside a host Editor; with none open (workers do the Unity work), 6 GB (J-037).
$keep = if (Get-Process Unity -ErrorAction SilentlyContinue) { 10 } else { 6 }
if ($vm.State -eq 'Off' -and $free - $need -lt $keep) { throw ("Only {0:N1} GB free; {1} GB must stay free (J-017, J-037). Not starting." -f $free, $keep) }
Start-VM -VM $vm
for ($i = 0; $i -lt 60; $i++) {
  $ip = (Get-VMNetworkAdapter -VM $vm).IPAddresses | Where-Object { $_ -match '^\d+\.' } | Select-Object -First 1
  if ($ip) { "$Name is running at $ip"; break }
  Start-Sleep 5
}
if (-not $ip) { "$Name started; no IP reported yet (normal during an OS install)."; return }
# Sign the worker in from its slot before it learns its name (Remote Control
# waits for the name). It skips templates and Ubuntu workers.
cmd /c (Join-Path $env:WORKERS_DIR 'signins.cmd') push $Name
# Tell the guest its worker name (Remote Control names its session after it):
# "<name> <unix seconds>", newer than the guest's boot. Windows path first, then Linux.
$tmp = Join-Path $env:TEMP "worker-name-$Name.txt"
Set-Content -LiteralPath $tmp -NoNewline -Encoding ascii -Value ("$Name " + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
$sent = $false
for ($i = 0; $i -lt 12 -and -not $sent; $i++) {
  foreach ($dest in 'C:\ProgramData\worker\name.txt', '/var/lib/worker/name.txt') {
    try { Copy-VMFile -VM $vm -SourcePath $tmp -DestinationPath $dest -FileSource Host -CreateFullPath -Force -ErrorAction Stop; $sent = $true; break } catch { }
  }
  if (-not $sent) { Start-Sleep 5 }
}
Remove-Item -LiteralPath $tmp
if ($sent) { "Told the guest its name ($Name)." } else { "Could not copy the name in (Guest Service Interface off?); Remote Control falls back to the registry/KVP name." }
# Each commit in a Windows worker carries what its work cost (tokens, minutes) as trailers, read from
# Claude Code's own logs by commit-cost.mjs, so measuring costs the agent nothing.
if ((Get-VMFirmware -VM $vm).SecureBootTemplate -eq 'MicrosoftWindows') {
  try {
    Copy-VMFile -VM $vm -SourcePath (Join-Path $env:WORKERS_DIR 'commit-cost.mjs') -DestinationPath 'C:\Users\User\.worker\commit-cost.mjs' -FileSource Host -CreateFullPath -Force -ErrorAction Stop
    $cred = New-Object PSCredential('User', (ConvertTo-SecureString 'password' -AsPlainText -Force))
    Invoke-Command -VMName $Name -Credential $cred -ErrorAction Stop -ScriptBlock {
      $hook = @'
#!/bin/sh
# Worker cost trailers (tools/workers/commit-cost.mjs); not for merges, squashes or amends.
case "$2" in merge|squash|commit) exit 0;; esac
node "$HOME/.worker/commit-cost.mjs" "$1" || true
'@
      [IO.File]::WriteAllText("$env:USERPROFILE\5e-compatible-game\.git\hooks\prepare-commit-msg", ($hook -replace "`r`n", "`n"))
    }
    "Commits in $Name carry their token and time cost."
  } catch { "Could not install the commit cost hook: $($_.Exception.Message)" }
}
