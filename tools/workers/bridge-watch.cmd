@echo off
rem Starts bridge-watch.sh (the mailbox and usage-limit watchdog) in a minimised Git Bash window.
rem A copy of this file sits in the Startup folder so it runs at sign-in; a second start exits at once.
start "bridge-watch" /min "C:\Program Files\Git\bin\bash.exe" "/c/Users/Andrew/Development/5e-compatible-game/.claude/worktrees/coordinator/tools/workers/bridge-watch.sh"
