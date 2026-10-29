@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Runs INSIDE the Windows template as the account workers use. The dev
# environment's account has no password, so instead of a task that runs signed
# out (which needs one), Windows signs that account in at boot and a logon task
# starts Claude Code's Remote Control, named after the worker (w1, w2...) so
# every clone shows up as its own session. Run it from an elevated prompt
# (automatic sign-in, and replacing the task). Log: %TEMP%\claude-rc.log.
# -Password sets the account's password too (Andrew chose "password": these VMs
# sit on the private NAT switch). With a password the host can reach the guest
# over PowerShell Direct (Invoke-Command -VMName); Windows refuses that for an
# account with a blank password.
param([string]$PermissionMode = 'auto', [string]$Password)
$ErrorActionPreference = 'Stop'
# An earlier attempt stored a `claude setup-token` token; it would override the sign-in.
[Environment]::SetEnvironmentVariable('CLAUDE_CODE_OAUTH_TOKEN', $null, 'User')
$user = "$env:COMPUTERNAME\$env:USERNAME"
if ($PSBoundParameters.ContainsKey('Password')) { net user $env:USERNAME $Password | Out-Null }
# Automatic sign-in at boot, with the account's password (blank if it has none).
$winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
Set-ItemProperty $winlogon AutoAdminLogon '1'
Set-ItemProperty $winlogon DefaultUserName $env:USERNAME
Set-ItemProperty $winlogon DefaultDomainName $env:COMPUTERNAME
if ($PSBoundParameters.ContainsKey('Password')) { Set-ItemProperty $winlogon DefaultPassword $Password }
elseif ($null -eq (Get-ItemProperty $winlogon).DefaultPassword) { Set-ItemProperty $winlogon DefaultPassword '' }
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
# A clone carries the template's Remote Control registration (environment id in
# bridge-pointer.json, machine id in .claude.json) and would come back under the
# template's name. When the name changes, drop both so Claude registers afresh.
$repo = "$env:USERPROFILE\5e-compatible-game"
$last = "$env:LOCALAPPDATA\worker\rc-name.txt"
if (-not (Test-Path $last) -or (Get-Content $last -Raw).Trim() -ne $n) {
  Remove-Item "$env:USERPROFILE\.claude\projects\$($repo -replace '[:\\]', '-')\bridge-pointer.json" -ErrorAction SilentlyContinue
  $cfg = "$env:USERPROFILE\.claude.json"
  if (Test-Path $cfg) {
    $j = Get-Content $cfg -Raw
    $k = '"remoteControlMachineId"\s*:\s*"[^"]*"'
    $j2 = $j -replace "$k\s*,", '' -replace ",\s*$k", ''
    if ($j2 -ne $j) { [IO.File]::WriteAllText($cfg, $j2) }
  }
  New-Item -ItemType Directory -Force (Split-Path $last) | Out-Null
  Set-Content $last $n
}
Set-Location $repo
# Unity's batch mode finds the signed-in licence only while Hub is running.
$hub = 'C:\Program Files\Unity Hub\Unity Hub.exe'
if ((Test-Path $hub) -and -not (Get-Process 'Unity Hub' -ErrorAction SilentlyContinue)) { Start-Process $hub -WindowStyle Minimized }
# Each worker needs its own sign-in: a claude.ai sign-in's refresh token is
# single-use, so clones sharing the template's void each other, and Remote
# Control refuses `claude setup-token` tokens. Unsigned, open the sign-in
# window on the desktop and wait for it.
$claude = "$env:USERPROFILE\.local\bin\claude.exe"
$signedIn = { ((& $claude auth status 2>$null) -join '') -match '"loggedIn":\s*true' }
if (-not (& $signedIn)) {
  Start-Process powershell.exe -ArgumentList '-NoExit', '-Command', "Write-Host 'Sign in to Claude for $n. Remote Control starts once you have.'; & '$claude' auth login"
  while (-not (& $signedIn)) { Start-Sleep 10 }
}
& $claude remote-control --spawn=worktree --permission-mode PERMISSION_MODE --name $n *> "$env:TEMP\claude-rc.log"
'@ -replace 'PERMISSION_MODE', $PermissionMode
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($run))
$action = New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -WindowStyle Hidden -EncodedCommand $enc"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName 'Claude Remote Control' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force -ErrorAction Stop | Out-Null
"Registered. $user signs in at boot and Remote Control starts at the next boot (spawned sessions: $PermissionMode); log: $env:TEMP\claude-rc.log."
