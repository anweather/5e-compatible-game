@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# rescue.cmd <worker> [-Force]: gets the work in progress out of a stopped (or
# asleep) Windows worker without putting Claude to work on it, for one whose
# Claude account is out of usage. Starts the VM directly (not start.cmd, so no
# sign-in is copied in and the guest isn't told its name), stops the guest's
# Remote Control task and any Claude as soon as PowerShell Direct answers, then
# shuts it down through stop.cmd, which pushes the work as
# wip/<worker>-<branch> (wip.cmd) and refuses if that fails: the worker is
# then left running, without Claude, for a look. -Force goes to stop.cmd.
param([Parameter(Mandatory)][string]$Name, [switch]$Force, [string]$GuestPassword = 'password')
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$ErrorActionPreference = 'Stop'
if ($Name -like '*template*' -or $Name -eq 'Windows 11 dev environment') { throw "$Name is not a worker; nothing to rescue." }
$vm = Get-VM -Name $Name
if ((Get-VMFirmware -VM $vm).SecureBootTemplate -ne 'MicrosoftWindows') { throw "$Name isn't a Windows worker (no PowerShell Direct): start it and push its work from inside." }
# The same RAM rule as start.cmd (J-017, J-037).
$free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
$keep = if (Get-Process Unity -ErrorAction SilentlyContinue) { 10 } else { 6 }
if ($vm.State -eq 'Off' -and $free - $vm.MemoryStartup / 1GB -lt $keep) { throw ("Only {0:N1} GB free; {1} GB must stay free (J-017, J-037). Not starting $Name." -f $free, $keep) }
if ($vm.State -ne 'Running') { Start-VM -VM $vm; "$Name started for a rescue (no Remote Control)." }
# Remote Control's logon task waits a few minutes for start.cmd's name file
# before it starts Claude; stop it well before then. It retries a failed run a
# minute later, so the Claude processes are stopped again just before the push.
$cred = New-Object PSCredential('User', (ConvertTo-SecureString $GuestPassword -AsPlainText -Force))
$noClaude = {
  Stop-ScheduledTask -TaskName 'Claude Remote Control' -ErrorAction SilentlyContinue
  Get-Process claude -ErrorAction SilentlyContinue | Stop-Process -Force
}
for ($i = 0; ; $i++) {
  try { Invoke-Command -VMName $Name -Credential $cred -ScriptBlock $noClaude -ErrorAction Stop; break }
  catch { if ($i -ge 36) { throw "Can't reach $Name over PowerShell Direct (account User): $_" }; Start-Sleep 5 }
}
"Remote Control held off in $Name."
Invoke-Command -VMName $Name -Credential $cred -ScriptBlock $noClaude
$stop = @($Name) + @(if ($Force) { '-Force' })
cmd /c (Join-Path $env:WORKERS_DIR 'stop.cmd') @stop
if ($LASTEXITCODE) { throw "$Name is still running, without Claude: its work couldn't be pushed (see above)." }
