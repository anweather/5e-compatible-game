@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# start.cmd <vm> [brief.md]: starts (or resumes from sleep) a VM, then prints its IP once the guest
# reports one. With a brief, the worker's runner (runner.mjs) starts on it at once, and its report
# comes back to the PM's mailbox through bridge-watch.sh: no coordinator session in between.
param([Parameter(Mandatory)][string]$Name, [string]$Brief)
if ($Brief -and -not (Test-Path -LiteralPath $Brief)) { throw "No brief at $Brief" }
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$os = Get-CimInstance Win32_OperatingSystem
$free = $os.FreePhysicalMemory/1MB
$vm = Get-VM -Name $Name -ErrorAction Stop
$need = $vm.MemoryStartup/1GB
# J-017 keeps 10 GB free beside a host Editor; with none open (workers do the Unity work), 6 GB (J-037).
# A Linux worker (1 GB start, peaks under 2 GB) may start down to 5 GB free beside an Editor (Andrew, 2026-10-29; 8 -> 5 on 2026-10-30).
$linuxVm = (Get-VMFirmware -VM $vm).SecureBootTemplate -ne 'MicrosoftWindows'
# An Editor, not Unity Hub's own helper (resources\unity.exe, about 20 MB, always running).
$editor = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -notlike '*\Unity Hub\*' }
$keep = if ($editor) { if ($linuxVm) { 5 } else { 10 } } else { 6 }
if ($vm.State -eq 'Off' -and $free - $need -lt $keep) { throw ("Only {0:N1} GB free; {1} GB must stay free (J-017, J-037). Not starting." -f $free, $keep) }
Start-VM -VM $vm
# Hyper-V's own meter (CPU, RAM, disk) from here; stop.cmd logs it.
Enable-VMResourceMetering -VM $vm; Reset-VMResourceMetering -VM $vm
for ($i = 0; $i -lt 60; $i++) {
  $ip = (Get-VMNetworkAdapter -VM $vm).IPAddresses | Where-Object { $_ -match '^\d+\.' } | Select-Object -First 1
  if ($ip) { "$Name is running at $ip"; break }
  Start-Sleep 5
}
if (-not $ip) { "$Name started; no IP reported yet (normal during an OS install)."; return }
# Sign the worker in from its slot before it learns its name (Remote Control
# waits for the name). Windows workers over PowerShell Direct, Linux ones over SSH (linux.sh).
$linux = (Get-VMFirmware -VM $vm).SecureBootTemplate -ne 'MicrosoftWindows'
$bash = 'C:\Program Files\Git\bin\bash.exe'
$sh = (Join-Path $env:WORKERS_DIR 'linux.sh') -replace '\\', '/'
if ($linux) { if ($Name -notlike '*template*') { & $bash $sh push $Name $ip } }
else { cmd /c (Join-Path $env:WORKERS_DIR 'signins.cmd') push $Name }
# Tell the guest its worker name (Remote Control names its session after it):
# "<name> <unix seconds>", newer than the guest's boot. Windows path first, then Linux.
# Linux's copy daemon takes a folder and keeps the source's file name, so the source is name.txt.
$tmpDir = Join-Path $env:TEMP "worker-name-$Name"
New-Item -ItemType Directory -Force $tmpDir | Out-Null
$tmp = Join-Path $tmpDir 'name.txt'
# A trailing newline for Linux, whose `read` fails on a last line without one.
Set-Content -LiteralPath $tmp -NoNewline -Encoding ascii -Value ("$Name " + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + $(if ($linux) { "`n" }))
$sent = $false
for ($i = 0; $i -lt 12 -and -not $sent; $i++) {
  foreach ($dest in $(if ($linux) { '/var/lib/worker' } else { 'C:\ProgramData\worker\name.txt' })) {
    try { Copy-VMFile -VM $vm -SourcePath $tmp -DestinationPath $dest -FileSource Host -CreateFullPath -Force -ErrorAction Stop; $sent = $true; break } catch { }
  }
  if (-not $sent) { Start-Sleep 5 }
}
Remove-Item -LiteralPath $tmpDir -Recurse
if ($sent) { "Told the guest its name ($Name)." } else { "Could not copy the name in (Guest Service Interface off?); Remote Control falls back to the registry/KVP name." }
# Each commit in a worker carries what its work cost (tokens, minutes) as trailers, read from
# Claude Code's own logs by commit-cost.mjs, so measuring costs the agent nothing.
if ((Get-VMFirmware -VM $vm).SecureBootTemplate -eq 'MicrosoftWindows') {
  try {
    foreach ($f in 'commit-cost.mjs', 'attention.mjs', 'worker-settings.mjs', 'runner.mjs') {
      Copy-VMFile -VM $vm -SourcePath (Join-Path $env:WORKERS_DIR $f) -DestinationPath "C:\Users\User\.worker\$f" -FileSource Host -CreateFullPath -Force -ErrorAction Stop
    }
    $cred = New-Object PSCredential('User', (ConvertTo-SecureString 'password' -AsPlainText -Force))
    # The attention hook: a permission prompt in the worker is reported to the host.
    Invoke-Command -VMName $Name -Credential $cred -ErrorAction Stop -ScriptBlock { node "$env:USERPROFILE\.worker\worker-settings.mjs" }
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
    # The mailbox runner: a logon task in the worker's own desktop session (where claude is signed
    # in), registered each start so a new runner.mjs is picked up, and started now.
    Invoke-Command -VMName $Name -Credential $cred -ErrorAction Stop -ScriptBlock {
      $user = "$env:COMPUTERNAME\$env:USERNAME"
      $node = (Get-Command node -ErrorAction Stop).Source
      $action = New-ScheduledTaskAction -Execute $node -Argument "`"$env:USERPROFILE\.worker\runner.mjs`"" -WorkingDirectory $env:USERPROFILE
      $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
      $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
      $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
      Register-ScheduledTask -TaskName 'Worker Runner' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
      Start-ScheduledTask -TaskName 'Worker Runner'
    }
    "The mailbox runner is running in $Name."
  } catch { "Could not install the commit cost hook or the runner: $($_.Exception.Message)" }
} elseif ($Name -notlike '*template*') { & $bash $sh hook $Name $ip; & $bash $sh runner $Name $ip }
# A brief: into the worker's inbox, where the runner picks it up (brief.cmd does the same later).
if ($Brief) { cmd /c (Join-Path $env:WORKERS_DIR 'brief.cmd') $Name $Brief }
