# Starts the voice in the woods (a VivoxSpike beacon: a small window that loops a recorded voice at
# (75, -20), 60 m east-south-east of the yard spawn, and plays nothing itself), then the game with
# -vivox-walker so it hears that voice where the wizard stands. Closing the game closes the beacon.
#   powershell -ExecutionPolicy Bypass -File tools\vivox\voice-in-the-woods.ps1 [build folder]
# The build is unity\Builds\PcVoice (from the vivox-spike branch) unless another folder is given.
param([string]$Build = (Join-Path $PSScriptRoot "..\..\unity\Builds\PcVoice"))
$exe = Join-Path (Resolve-Path $Build) "SpellChemistryValley.exe"
$wav = Join-Path $PSScriptRoot "voice-in-the-woods.wav"
$logs = Join-Path $env:TEMP "voice-in-the-woods"
New-Item -ItemType Directory -Force $logs | Out-Null
$beacon = Start-Process -PassThru -WindowStyle Minimized $exe -ArgumentList "-screen-fullscreen", "0", "-screen-width", "480", "-screen-height", "270", "-logFile", "`"$logs\beacon.log`"", "-vivox-speaker", "`"$wav`""
Write-Host "The voice in the woods is starting (pid $($beacon.Id)); the game opens in 20 s."
Start-Sleep 20
$game = Start-Process -PassThru $exe -ArgumentList "-logFile", "`"$logs\game.log`"", "-vivox-walker"
$game.WaitForExit()
Stop-Process -Id $beacon.Id -ErrorAction SilentlyContinue
Write-Host "Closed the voice. Logs: $logs (lines starting VIVOX:)"
