@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# brief.cmd <vm> <brief.md> [-FollowUp <id>]: puts a brief into a running worker's inbox, where its
# runner (runner.mjs) starts a headless Claude session on it. The id is <yyyyMMdd-HHmmss>-<file name>;
# the worker's report comes back as "<vm> reports: <id>" in the PM's mailbox (bridge-watch.sh).
# -FollowUp <id> resumes that brief's session instead, so it keeps its context.
param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$File, [string]$FollowUp)
if (-not (Test-Path -LiteralPath $File)) { throw "No brief at $File" }
$vm = Get-VM -Name $Name -ErrorAction Stop
if ($vm.State -ne 'Running') { throw "$Name isn't running: start.cmd $Name $File" }
$id = if ($FollowUp) { "$FollowUp.followup-$(Get-Date -Format HHmmss)" } else { "$(Get-Date -Format yyyyMMdd-HHmmss)-$([IO.Path]::GetFileNameWithoutExtension($File) -replace '[^\w.-]', '-')" }
$linux = (Get-VMFirmware -VM $vm).SecureBootTemplate -ne 'MicrosoftWindows'
# Under its final name, then the runner sees it: written whole before it's in the inbox.
$tmpDir = Join-Path $env:TEMP "brief-$Name"; New-Item -ItemType Directory -Force $tmpDir | Out-Null
$tmp = Join-Path $tmpDir "$id.md"; Copy-Item -LiteralPath $File $tmp -Force
try {
  if ($linux) {
    $ip = (Get-VMNetworkAdapter -VM $vm).IPAddresses | Where-Object { $_ -match '^\d+\.' } | Select-Object -First 1
    & 'C:\Program Files\Git\bin\bash.exe' ((Join-Path $env:WORKERS_DIR 'linux.sh') -replace '\\', '/') brief $Name $ip ($tmp -replace '\\', '/')
  } else {
    Copy-VMFile -VM $vm -SourcePath $tmp -DestinationPath "C:\Users\User\.worker\inbox\$id.md" -FileSource Host -CreateFullPath -Force -ErrorAction Stop
  }
  "Briefed $Name`: $id"
} finally { Remove-Item -LiteralPath $tmpDir -Recurse -Force }
