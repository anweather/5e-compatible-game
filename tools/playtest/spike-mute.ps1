# Phase 1 audio spike: runs the player off-screen with its sound on, mutes its
# Windows audio session as soon as it has one, and checks the bot still heard
# the sound (result.json audioPeak above 0) while the mixer says it's muted.
param(
    [string]$Exe = "$PSScriptRoot\..\..\unity\Builds\Windows\SpellChemistryValley.exe",
    [int]$Seconds = 12
)
$ErrorActionPreference = 'Stop'
Add-Type -Path "$PSScriptRoot\AppMute.cs"
$root = Resolve-Path "$PSScriptRoot\..\.."
$out = Join-Path $root 'playtest\runs\spike-appmute'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
$argv = @('-playtest', '-playtest-seconds', $Seconds, '-playtest-offscreen', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
          '-playtest-out', $out, '-logFile', (Join-Path $out 'Player.log'))
$started = Get-Date
$p = Start-Process -FilePath (Resolve-Path $Exe) -ArgumentList $argv -PassThru
$mutedAt = $null
while (-not $p.HasExited -and ((Get-Date) - $started).TotalSeconds -lt 180) {
    if ($null -eq $mutedAt -and [AppMute]::Mute($p.Id) -gt 0) { $mutedAt = [math]::Round(((Get-Date) - $started).TotalSeconds, 1) }
    Start-Sleep -Milliseconds 100
}
"muted the player's session after $mutedAt s"
Get-Content (Join-Path $out 'result.json')
