@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Runs INSIDE the Windows template as the account workers use. The dev
# environment's account has no password, so instead of a task that runs signed
# out (which needs one), Windows signs that account in at boot and a logon task
# starts Claude Code's Remote Control, named after the worker (w1, w2...) so
# every clone shows up as its own session. The first run needs an elevated
# prompt (it turns on automatic sign-in); re-runs don't. Log: %TEMP%\claude-rc.log.
param([string]$PermissionMode = 'auto')
$ErrorActionPreference = 'Stop'
$user = "$env:COMPUTERNAME\$env:USERNAME"
# Automatic sign-in at boot (a blank password is allowed here).
$winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$wl = Get-ItemProperty $winlogon
if ($wl.AutoAdminLogon -ne '1' -or $wl.DefaultUserName -ne $env:USERNAME) {
  Set-ItemProperty $winlogon AutoAdminLogon '1'
  Set-ItemProperty $winlogon DefaultUserName $env:USERNAME
  Set-ItemProperty $winlogon DefaultDomainName $env:COMPUTERNAME
  if ($null -eq $wl.DefaultPassword) { Set-ItemProperty $winlogon DefaultPassword '' }
}
# The name: start.cmd copies "<name> <unix seconds>" into C:\ProgramData\worker\name.txt
# after each boot. The registry's VirtualMachineName can still hold the
# template's name this early, so it is only the fallback.
$run = @'
$boot = ([DateTimeOffset](Get-CimInstance Win32_OperatingSystem).LastBootUpTime).ToUnixTimeSeconds()
$f = 'C:\ProgramData\worker\name.txt'; $n = $null
for ($i = 0; $i -lt 36 -and -not $n; $i++) {
  if (Test-Path $f) { $name, $at = (Get-Content $f -Raw).Trim() -split ' '; if ([long]$at -gt $boot) { $n = $name } }
  if (-not $n) { Start-Sleep 5 }
}
if (-not $n) { $n = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' -ErrorAction SilentlyContinue).VirtualMachineName }
if (-not $n) { $n = $env:COMPUTERNAME }
Set-Location "$env:USERPROFILE\5e-compatible-game"
& "$env:USERPROFILE\.local\bin\claude.exe" remote-control --spawn=worktree --permission-mode PERMISSION_MODE --name $n *> "$env:TEMP\claude-rc.log"
'@ -replace 'PERMISSION_MODE', $PermissionMode
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($run))
$action = New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -WindowStyle Hidden -EncodedCommand $enc"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName 'Claude Remote Control' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
"Registered. $user signs in at boot and Remote Control starts at the next boot (spawned sessions: $PermissionMode); log: $env:TEMP\claude-rc.log."
