#!/bin/bash
# Started at Windows sign-in (Startup folder): the Workers session on the Pro
# account, with Remote Control on, picking up tools/workers/HANDOFF.md.
export CLAUDE_CONFIG_DIR=$HOME/.claude-pro
# The inbox listener is a background command: keep it when memory runs low.
export CLAUDE_CODE_DISABLE_BG_SHELL_PRESSURE_REAP=1
cd /c/Users/Andrew/Development/5e-compatible-game || exit 1
~/.local/bin/claude.exe --remote-control workers "You are the Workers session. workers-vms is merged into dev (4afe4aa). Make your own worktree of origin/dev under .claude/worktrees/workers (don't switch the main checkout's branch), then read tools/workers/HANDOFF.md there and follow its prompt. Andrew has restarted since joining Hyper-V Administrators. Write your reports to tools/workers/REPORT.md, since you can't message the coordinator across accounts."
read -p "Claude exited. Press Enter to close."
