@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Runs INSIDE the Windows template, from an elevated prompt (Andrew types the
# account's password once). Registers a task that starts Claude Code's Remote
# Control at boot, with nobody signed in, named after the Hyper-V VM (w1, w2...)
# so every clone shows up as its own session. Log: %TEMP%\claude-rc.log.
# Re-running replaces the task.
$ErrorActionPreference = 'Stop'
$user = "$env:COMPUTERNAME\$env:USERNAME"
# Hyper-V writes the VM's name into the guest's registry (KVP integration service).
$run = @'
$p = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' -ErrorAction SilentlyContinue
$n = if ($p.VirtualMachineName) { $p.VirtualMachineName } else { $env:COMPUTERNAME }
Set-Location "$env:USERPROFILE\5e-compatible-game"
& "$env:USERPROFILE\.local\bin\claude.exe" remote-control --name $n *> "$env:TEMP\claude-rc.log"
'@
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($run))
$action = New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -WindowStyle Hidden -EncodedCommand $enc"
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
$cred = Get-Credential -UserName $user -Message "Password for $user (the task runs as this account at boot)"
Register-ScheduledTask -TaskName 'Claude Remote Control' -Action $action -Trigger $trigger -Settings $settings `
  -User $user -Password $cred.GetNetworkCredential().Password -RunLevel Limited -Force | Out-Null
Start-ScheduledTask -TaskName 'Claude Remote Control'
"Registered. It starts at every boot; check claude.ai/code or the log in $env:TEMP\claude-rc.log."
