@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Runs INSIDE the Windows 11 template VM, from an elevated prompt (Andrew
# accepts the UAC prompt). Fetch it in the VM with:
#   curl.exe -LO https://raw.githubusercontent.com/anweather/5e-compatible-game/dev/tools/workers/provision-windows.cmd
# Installs Git, .NET 8 SDK, Node 22, Unity Hub (winget, which pulls each
# vendor's own installer), Unity Editor 6000.3.27f1 (+ Mac Build Support, Mono;
# Windows Mono support comes with the Windows Editor), Claude Code, and clones
# the repo (branch dev) to %USERPROFILE%\5e-compatible-game. Safe to re-run.
$ErrorActionPreference = 'Continue'
$version = '6000.3.27f1'; $changeset = '672d562a42a7'
$repo = Join-Path $env:USERPROFILE '5e-compatible-game'
function Step($t) { "`n== $t" }
function Refresh-Path { $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User') }

Step 'winget: Git, .NET 8 SDK, Node 22, Unity Hub, GitHub CLI'
foreach ($id in 'Git.Git', 'Microsoft.DotNet.SDK.8', 'OpenJS.NodeJS.22', 'Unity.UnityHub', 'GitHub.cli') {
  # --source winget: the msstore source fails its certificate check in the dev environment image.
  winget install --id $id -e --source winget --silent --accept-package-agreements --accept-source-agreements
}
Refresh-Path
git config --global core.longpaths true
git lfs install

$hub = 'C:\Program Files\Unity Hub\Unity Hub.exe'
Step "Unity Editor $version"
& $hub -- --headless install --version $version --changeset $changeset | Out-Host
Step 'Mac Build Support (Mono), if it installs cleanly'
& $hub -- --headless install-modules --version $version --module mac-mono | Out-Host

Step 'Claude Code (official installer; not signed in)'
if (-not (Get-Command claude -ErrorAction SilentlyContinue)) { Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression }
# The installer leaves its folder off the PATH; add it to the user PATH once.
$bin = Join-Path $env:USERPROFILE '.local\bin'
$userPath = [Environment]::GetEnvironmentVariable('Path','User')
if (($userPath -split ';') -notcontains $bin) { [Environment]::SetEnvironmentVariable('Path', "$userPath;$bin", 'User'); Refresh-Path }

Step 'The repo, branch dev'
if (-not (Test-Path "$repo\.git")) { git clone -b dev https://github.com/anweather/5e-compatible-game.git $repo }

Step 'Check: core builds and tests, sandbox tests'
Push-Location $repo
dotnet build core -c Release -warnaserror -v q; dotnet test core -v q
Set-Location sandbox; npm ci --no-audit --no-fund; npm test
Pop-Location
Step 'Done. Next: README W3 (Andrew: Unity licence, unity CLI, GitHub, Claude sign-in), then W4 (seal).'
