@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# signins.cmd push|pull <worker> | sync | install
# Keeps each worker's Claude sign-in on the host, in a slot named after the
# worker (<Root>\signins\<worker>, readable by this account only), so a fresh
# clone boots signed in. Why slots: a claude.ai sign-in's refresh token is
# single-use, so one sign-in can live in only one running VM; Remote Control
# refuses `claude setup-token` tokens. Andrew signs in once per slot (the
# worker opens a sign-in window when its slot is empty), never again.
#   push <w>  slot -> worker (start.cmd, before the worker starts Remote Control)
#   pull <w>  worker -> slot (stop, sleep, remove-worker)
#   sync      pull from every running worker (the 5-minute host task)
#   install   register that task; it runs a copy of this file from <Root>\signins
# Whichever side's token expires later wins, so a stale copy never overwrites
# a fresh one. Windows workers only (PowerShell Direct, account User).
param([Parameter(Mandatory)][ValidateSet('push', 'pull', 'sync', 'install')][string]$Action,
  [string]$Name, [string]$Root = 'C:\HyperV\Workers', [string]$GuestPassword = 'password')
if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V PowerShell module missing" }
try { Get-VM -ErrorAction Stop | Out-Null } catch { throw "No Hyper-V access: run grant-hyperv-access.cmd as admin once, then sign out and in." }
$ErrorActionPreference = 'Stop'
$bank = Join-Path $Root 'signins'
if (-not (Test-Path $bank)) {
  New-Item -ItemType Directory -Force $bank | Out-Null
  icacls $bank /inheritance:r /grant:r "${env:USERNAME}:(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null
}
$cred = New-Object PSCredential('User', (ConvertTo-SecureString $GuestPassword -AsPlainText -Force))
$guestPath = 'C:\Users\User\.claude\.credentials.json'
function Expiry([string]$json) {
  try { $o = ($json | ConvertFrom-Json).claudeAiOauth; if ($o.refreshToken) { [long]$o.expiresAt } else { 0 } } catch { 0 }
}
# A Windows worker: not a template or the Quick Create original, and Windows
# Secure Boot (Ubuntu VMs use the UEFI CA template; PowerShell Direct is Windows-only).
function IsWorker($vm) {
  $vm.Name -notlike '*template*' -and $vm.Name -ne 'Windows 11 dev environment' -and
    (Get-VMFirmware -VM $vm).SecureBootTemplate -eq 'MicrosoftWindows'
}
function Slot([string]$w) { Join-Path $bank "$w\claude-credentials.json" }
function Guest([string]$w, [scriptblock]$sb, $arg) {
  for ($i = 0; $i -lt 12; $i++) {
    try { return Invoke-Command -VMName $w -Credential $cred -ScriptBlock $sb -ArgumentList $arg -ErrorAction Stop } catch { Start-Sleep 5 }
  }
  throw "Can't reach $w over PowerShell Direct (account User)."
}
function Pull([string]$w) {
  $g = Guest $w { param($p) if (Test-Path $p) { Get-Content $p -Raw } } $guestPath
  $s = Slot $w
  $old = if (Test-Path $s) { Get-Content $s -Raw }
  if ((Expiry $g) -gt (Expiry $old)) {
    New-Item -ItemType Directory -Force (Split-Path $s) | Out-Null
    [IO.File]::WriteAllText($s, $g); "Saved $w's sign-in to its slot."
  } elseif (-not $old -and -not (Expiry $g)) { "$w isn't signed in yet; slot $w stays empty." }
}
function Push([string]$w) {
  $s = Slot $w
  if (-not (Test-Path $s)) { "Slot $w is empty: sign in on $w's desktop once."; return }
  $new = Get-Content $s -Raw
  $r = Guest $w { param($p, $j, $exp)
    $cur = if (Test-Path $p) { Get-Content $p -Raw }
    $e = try { $o = ($cur | ConvertFrom-Json).claudeAiOauth; if ($o.refreshToken) { [long]$o.expiresAt } else { 0 } } catch { 0 }
    if ($exp -le $e) { 'kept' } else { New-Item -ItemType Directory -Force (Split-Path $p) | Out-Null; [IO.File]::WriteAllText($p, $j); 'written' }
  } @($guestPath, $new, (Expiry $new))
  if ($r -eq 'written') { "Signed $w in from its slot." } else { "$w already has a newer sign-in; saving that instead."; Pull $w }
}
switch ($Action) {
  'push' { if (IsWorker (Get-VM -Name $Name)) { Push $Name } }
  'pull' { $vm = Get-VM -Name $Name; if ($vm.State -eq 'Running' -and (IsWorker $vm)) { Pull $Name } }
  'sync' {
    foreach ($vm in Get-VM | Where-Object { $_.State -eq 'Running' -and (IsWorker $_) }) {
      try { Pull $vm.Name } catch { "$($vm.Name): $_" }
    }
  }
  'install' {
    $copy = Join-Path $bank 'sync.cmd'
    Copy-Item -LiteralPath (Join-Path $env:WORKERS_DIR 'signins.cmd') -Destination $copy -Force
    $action = New-ScheduledTaskAction -Execute cmd.exe -Argument "/c `"$copy`" sync -Root `"$Root`""
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 4) -MultipleInstances IgnoreNew -StartWhenAvailable
    # S4U: runs without a window and without storing a password.
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Limited
    Register-ScheduledTask -TaskName 'Worker sign-in sync' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
    "Registered 'Worker sign-in sync': every 5 minutes, saves running workers' sign-ins to $bank."
  }
}
