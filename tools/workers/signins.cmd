@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# signins.cmd push|pull <worker> | sync | install
# Keeps each worker's sign-ins on the host, in a slot named after the worker
# (<Root>\signins\<worker>, readable by this account only), so a fresh clone
# boots signed in. Why slots: Claude's and Unity Hub's refresh tokens are
# single-use, so one sign-in can live in only one running VM (and Remote
# Control refuses `claude setup-token` tokens); Unity's licence file is bound
# to the machine, which new-worker keeps the same per name (fixed MAC).
# Andrew signs in once per slot (the worker opens Claude's sign-in window when
# its slot is empty; Hub he signs in to by hand), never again.
#   push <w>  slot -> worker (start.cmd, before the worker starts Remote Control)
#   pull <w>  worker -> slot (stop, sleep, remove-worker)
#   sync      pull from every running worker (the 5-minute host task)
#   install   register that task; it runs a copy of this file from <Root>\signins
# Pull keeps whichever side is newer: for Claude, the token that expires later;
# for Hub and the licence, the later file time. Windows workers only
# (PowerShell Direct, account User).
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
# What a slot holds: the guest's files and the slot folder they go in.
$items = @(
  @{ Item = 'claude'; Files = @('C:\Users\User\.claude\.credentials.json') }
  @{ Item = 'unity-hub'; Files = @('C:\Users\User\AppData\Roaming\UnityHub\accounts.db', 'C:\Users\User\AppData\Roaming\UnityHub\accounts.db-wal', 'C:\Users\User\AppData\Roaming\UnityHub\accounts.db-shm') }
  @{ Item = 'unity-licence'; Files = @('C:\Users\User\AppData\Local\Unity\licenses\UnityEntitlementLicense.xml') }
)
# How new a copy is: Claude's token expiry (0 when signed out); otherwise file time.
function Stamp([string]$item, $files) {
  if (-not $files.Count) { return 0 }
  if ($item -eq 'claude') {
    try { $o = ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($files[0].Data)) | ConvertFrom-Json).claudeAiOauth; if ($o.refreshToken) { return [long]$o.expiresAt } } catch { }
    return 0
  }
  ($files | Measure-Object -Property Time -Maximum).Maximum
}
# A Windows worker: not a template or the Quick Create original, and Windows
# Secure Boot (Ubuntu VMs use the UEFI CA template; PowerShell Direct is Windows-only).
function IsWorker($vm) {
  $vm.Name -notlike '*template*' -and $vm.Name -ne 'Windows 11 dev environment' -and
    (Get-VMFirmware -VM $vm).SecureBootTemplate -eq 'MicrosoftWindows'
}
function Guest([string]$w, [scriptblock]$sb, $arg) {
  for ($i = 0; $i -lt 12; $i++) {
    try { return Invoke-Command -VMName $w -Credential $cred -ScriptBlock $sb -ArgumentList $arg -ErrorAction Stop } catch { Start-Sleep 5 }
  }
  throw "Can't reach $w over PowerShell Direct (account User)."
}
# Files as { Path, Data (base64), Time (UTC ticks) }, from the guest or a slot.
# (Shared read: Hub keeps its database open while it runs.)
$readGuest = { param($paths)
  foreach ($p in $paths) {
    if (Test-Path $p) {
      $fs = [IO.File]::Open($p, 'Open', 'Read', 'ReadWrite, Delete'); $ms = New-Object IO.MemoryStream
      try { $fs.CopyTo($ms) } finally { $fs.Dispose() }
      [pscustomobject]@{ Path = $p; Data = [Convert]::ToBase64String($ms.ToArray()); Time = (Get-Item $p).LastWriteTimeUtc.Ticks }
    }
  }
}
function ReadSlot([string]$w, $it) {
  $dir = Join-Path $bank "$w\$($it.Item)"
  foreach ($p in $it.Files) {
    $f = Join-Path $dir (Split-Path $p -Leaf)
    if (Test-Path $f) { [pscustomobject]@{ Path = $p; Data = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f)); Time = (Get-Item $f).LastWriteTimeUtc.Ticks } }
  }
}
function Pull([string]$w) {
  foreach ($it in $items) {
    $g = @(Guest $w $readGuest (, $it.Files))
    $s = @(ReadSlot $w $it)
    if ((Stamp $it.Item $g) -gt (Stamp $it.Item $s)) {
      $dir = Join-Path $bank "$w\$($it.Item)"
      if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
      New-Item -ItemType Directory -Force $dir | Out-Null
      foreach ($f in $g) {
        $out = Join-Path $dir (Split-Path $f.Path -Leaf)
        [IO.File]::WriteAllBytes($out, [Convert]::FromBase64String($f.Data))
        (Get-Item $out).LastWriteTimeUtc = [datetime]::new([long]$f.Time, 'Utc')
      }
      "Saved $w's $($it.Item) to its slot."
    }
  }
}
function Push([string]$w) {
  # Hub must not hold its database open while it's replaced; the worker's
  # startup task starts Hub again once it knows its name.
  Guest $w { Get-Process 'Unity Hub' -ErrorAction SilentlyContinue | Stop-Process -Force } @()
  foreach ($it in $items) {
    $s = @(ReadSlot $w $it)
    if (-not $s.Count) { "Slot $w has no $($it.Item) yet: sign in on $w once."; continue }
    if ($it.Item -eq 'claude' -and (Stamp 'claude' @(Guest $w $readGuest (, $it.Files))) -ge (Stamp 'claude' $s)) { continue }
    Guest $w { param($files, $all)
      foreach ($p in $all) { if ($files.Path -notcontains $p) { Remove-Item -LiteralPath $p -ErrorAction SilentlyContinue } }
      foreach ($f in $files) {
        New-Item -ItemType Directory -Force (Split-Path $f.Path) | Out-Null
        [IO.File]::WriteAllBytes($f.Path, [Convert]::FromBase64String($f.Data))
        (Get-Item $f.Path).LastWriteTimeUtc = [datetime]::new([long]$f.Time, 'Utc')
      }
    } @($s, $it.Files)
    "Signed $w in to $($it.Item) from its slot."
  }
  Pull $w   # a sign-in newer than the slot's stays in the worker; save it
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
    # A headless console, so no window flashes up every 5 minutes.
    $run = New-ScheduledTaskAction -Execute conhost.exe -Argument "--headless cmd.exe /c `"$copy`" sync -Root `"$Root`""
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 4) -MultipleInstances IgnoreNew -StartWhenAvailable
    # Interactive (runs while Andrew is signed in; register from an admin prompt):
    # no password is stored.
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName 'Worker sign-in sync' -Action $run -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
    "Registered 'Worker sign-in sync': every 5 minutes, saves running workers' sign-ins to $bank."
  }
}
