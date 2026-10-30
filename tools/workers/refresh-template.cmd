@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
set "WORKERS_DIR=%~dp0"
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# refresh-template.cmd [-Template windows-template] [-Packs <checkout>]
# Brings a Windows template up to date and re-seals it, so clones start ready
# to work: the repo on the latest dev; the Asset Store packs from
# unity/AssetPacks.json, copied from the host's checkout (Andrew restores a new
# pack there first with Prototype > Restore Asset Packs: downloading one needs
# the Unity account that owns it); Unity's Library imported, the yard built
# and EditMode tests run once, so a clone's first Unity job doesn't import
# everything again. Run it when AssetPacks.json changes or dev has moved a lot.
# Existing workers keep their old copy: remove-worker + new-worker brings
# them up to date (their sign-ins come back from their slots).
param([string]$Template = 'windows-template', [string]$Packs, [string]$GuestPassword = 'password')
$ErrorActionPreference = 'Stop'
# The packs live in the main checkout (worktrees don't have them).
if (-not $Packs) { $Packs = Split-Path (git -C $env:WORKERS_DIR rev-parse --path-format=absolute --git-common-dir) }
$list = (Get-Content -Raw (Join-Path $Packs 'unity\AssetPacks.json') | ConvertFrom-Json).packs
$folders = foreach ($p in $list) {
  $name = $p.folder -replace '^Assets/', ''
  if (-not (Test-Path (Join-Path $Packs "unity\Assets\$name"))) { throw "$name isn't in $Packs\unity\Assets: run Prototype > Restore Asset Packs there first." }
  $name, "$name.meta"
}
$cred = New-Object PSCredential('User', (ConvertTo-SecureString $GuestPassword -AsPlainText -Force))
$here = $env:WORKERS_DIR
if ((Get-VM -Name $Template).State -ne 'Running') { cmd /c (Join-Path $here 'start.cmd') $Template; if ($LASTEXITCODE) { throw "Couldn't start $Template." } }
# The packs go in as one tar file (Copy-VMFile copies single files).
$tar = Join-Path $env:TEMP "packs-$Template.tar"
& "$env:SystemRoot\System32\tar.exe" -C (Join-Path $Packs 'unity\Assets') -cf $tar @folders
if ($LASTEXITCODE) { throw "tar failed." }
"Copying {0:N1} GB of packs into $Template..." -f ((Get-Item $tar).Length / 1GB)
Copy-VMFile $Template -SourcePath $tar -DestinationPath 'C:\Users\User\packs.tar' -FileSource Host -CreateFullPath -Force
Remove-Item $tar
Invoke-Command -VMName $Template -Credential $cred -ArgumentList (, [string[]]$folders) -ScriptBlock { param($folders)
  $ErrorActionPreference = 'Stop'
  $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
  $repo = "$env:USERPROFILE\5e-compatible-game"
  Set-Location $repo
  if (git status --porcelain) { throw "$repo has local changes; not touching it." }
  git fetch -q origin; git switch -q dev; git merge -q --ff-only origin/dev
  "Repo at " + (git log -1 --format='%h %s')
  foreach ($f in $folders) { Remove-Item -LiteralPath "$repo\unity\Assets\$f" -Recurse -Force -ErrorAction SilentlyContinue }
  & "$env:SystemRoot\System32\tar.exe" -C "$repo\unity\Assets" -xf "$env:USERPROFILE\packs.tar"
  Remove-Item "$env:USERPROFILE\packs.tar"
  $unity = (Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe' | Select-Object -First 1).FullName
  $proj = "$repo\unity"
  function Unity([string[]]$more, [string]$log) {
    $p = Start-Process $unity -ArgumentList (@('-batchmode', '-nographics', '-projectPath', $proj, '-logFile', "$env:USERPROFILE\$log") + $more) -Wait -PassThru
    if ($p.ExitCode) { throw "Unity $($more -join ' ') exited $($p.ExitCode); see $env:USERPROFILE\$log in $env:COMPUTERNAME." }
  }
  # A batch run with -quit exits before the yard's auto-build, so build it directly.
  Unity @('-quit', '-executeMethod', 'ChemYardBuilder.Build') 'build-yard.log'
  $res = "$env:USERPROFILE\editmode-results.xml"
  Unity @('-runTests', '-testPlatform', 'EditMode', '-testResults', $res) 'editmode.log'
  $r = ([xml](Get-Content $res)).'test-run'; Remove-Item $res
  "Library ready; EditMode $($r.passed)/$($r.total) passed."
  if (git status --porcelain) { throw "Unity left tracked changes in $repo (a generated-output leak to fix)." }
}
cmd /c (Join-Path $here 'stop.cmd') $Template
cmd /c (Join-Path $here 'seal-template.cmd') -Name $Template -Replace
