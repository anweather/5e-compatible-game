@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# Runs INSIDE a template, as the worker account (no elevation). Runs
# `claude setup-token` (Andrew signs in in the browser it opens), takes the
# long-lived token from its output and stores it as the user's
# CLAUDE_CODE_OAUTH_TOKEN. The token is masked on screen and written to no file.
# Why: a normal sign-in's refresh token is single-use, so clones of one
# signed-in template void each other's sign-in; this token doesn't rotate.
$claude = "$env:USERPROFILE\.local\bin\claude.exe"
$lines = New-Object System.Collections.Generic.List[string]
& $claude setup-token 2>&1 | ForEach-Object {
  $s = ("$_" -replace '\x1b\[[0-9;?]*[A-Za-z]', '')
  $lines.Add($s)
  $s -replace 'sk-ant-\S+', '<token captured>'
}
# The token may be wrapped over several lines: take its first line, then each
# following line made only of token characters.
$tok = $null
for ($i = 0; $i -lt $lines.Count -and -not $tok; $i++) {
  if ($lines[$i] -match '(sk-ant-[A-Za-z0-9_\-]+)') {
    $tok = $Matches[1]
    for ($j = $i + 1; $j -lt $lines.Count -and $lines[$j].Trim() -match '^[A-Za-z0-9_\-]+$'; $j++) { $tok += $lines[$j].Trim() }
  }
}
$lines.Clear()
if (-not $tok -or $tok.Length -lt 60) { "No token found in setup-token's output; nothing was changed."; exit 1 }
[Environment]::SetEnvironmentVariable('CLAUDE_CODE_OAUTH_TOKEN', $tok, 'User')
"Saved CLAUDE_CODE_OAUTH_TOKEN for $env:USERNAME ($($tok.Length) characters, starts $($tok.Substring(0, 14))...). New processes and the next boot use it."
