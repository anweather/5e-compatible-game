@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# ANDREW, ONCE, from an elevated prompt: adds you to Hyper-V Administrators
# (SID S-1-5-32-578), so later non-elevated sessions can run the VM scripts.
# Sign out and back in afterwards.
$me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
Add-LocalGroupMember -SID 'S-1-5-32-578' -Member $me -ErrorAction Stop
"Added $me to Hyper-V Administrators. Sign out and in for it to take effect."
