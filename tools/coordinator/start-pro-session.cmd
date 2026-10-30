@echo off
rem Starts a Claude Code session on the second (Pro) account, in the game repo,
rem inside Git Bash (Claude's screen froze in cmd/Windows Terminal here).
rem Its own config folder keeps the main account signed in.
rem Then type /remote-control to drive it from a phone.
start "" "C:\Program Files\Git\git-bash.exe" -c "export CLAUDE_CONFIG_DIR=$HOME/.claude-pro; cd /c/Users/Andrew/Development/5e-compatible-game; ~/.local/bin/claude.exe; read"
