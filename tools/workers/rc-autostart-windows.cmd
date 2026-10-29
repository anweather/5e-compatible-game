@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Runs INSIDE the Windows template, from an elevated prompt, as the account
# workers use. The dev environment's account has no password, so instead of a
# task that runs signed out (which needs one), Windows signs that account in
# at boot and a logon task starts Claude Code's Remote Control, named after the
# Hyper-V VM (w1, w2...) so every clone shows up as its own session.
# Log: %TEMP%\claude-rc.log. Re-running replaces the task.
$ErrorActionPreference = 'Stop'
$user = "$env:COMPUTERNAME\$env:USERNAME"
# Automatic sign-in at boot (a blank password is allowed here).
$winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
Set-ItemProperty $winlogon AutoAdminLogon '1'
Set-ItemProperty $winlogon DefaultUserName $env:USERNAME
Set-ItemProperty $winlogon DefaultDomainName $env:COMPUTERNAME
if (-not (Get-ItemProperty $winlogon -Name DefaultPassword -ErrorAction SilentlyContinue)) { Set-ItemProperty $winlogon DefaultPassword '' }
# Hyper-V writes the VM's name into the guest's registry (KVP integration service).
$run = @'
$p = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' -ErrorAction SilentlyContinue
$n = if ($p.VirtualMachineName) { $p.VirtualMachineName } else { $env:COMPUTERNAME }
Set-Location "$env:USERPROFILE\5e-compatible-game"
& "$env:USERPROFILE\.local\bin\claude.exe" remote-control --name $n *> "$env:TEMP\claude-rc.log"
'@
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($run))
$action = New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -WindowStyle Hidden -EncodedCommand $enc"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName 'Claude Remote Control' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName 'Claude Remote Control'
"Registered. $user signs in at boot and Remote Control starts; check claude.ai/code or $env:TEMP\claude-rc.log."
