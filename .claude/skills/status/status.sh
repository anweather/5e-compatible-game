#!/usr/bin/env bash
# One cheap look at the PC's work, in about 15 lines: the Editor's checkout, Unity, the queue,
# the mailbox, memory, and new clips. Read-only; it never fetches the Editor's tree or touches Unity state.
# Usage: status.sh [checkout]   (default: the host-ux worktree beside this one)
set -uo pipefail
repo="$(git rev-parse --show-toplevel)"
host="${1:-$(dirname "$repo")/host-ux}"
[[ -d "$host" ]] || host="$repo/.claude/worktrees/host-ux"

git -C "$repo" fetch -q origin 2>/dev/null

if [[ -d "$host" ]]; then
  branch="$(git -C "$host" rev-parse --abbrev-ref HEAD)"
  changed="$(git -C "$host" status --porcelain | grep -vc '^?? unity/ProjectSettings/Packages/')"
  echo "host-ux: $branch, $changed changed file(s)"
  git -C "$host" log --format='  %h %cr  %s' -3 | cut -c1-110
  if git -C "$repo" rev-parse -q --verify "origin/$branch" >/dev/null; then
    echo "  pushed: origin/$branch at $(git -C "$repo" log -1 --format='%h %cr' "origin/$branch")"
  else
    echo "  pushed: not yet"
  fi
fi

if tasklist //FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi unity.exe; then
  unity command console status --result-only 2>/dev/null | tr -d ' \n' |
    sed -nE 's/.*"compilationFailed":([a-z]+),"compiling":([a-z]+),"consoleErrors":([0-9]+).*/Unity: open, compiling \2, compile failed \1, console errors \3/p'
  echo
else
  echo "Unity: not running"
fi

q="$repo/docs/handoffs/briefs/queue"
echo "queue: $(ls "$q"/*.md 2>/dev/null | grep -v README | xargs -r -n1 basename | tr '\n' ' ')"
echo "taken: $(ls "$q"/taken/*.md 2>/dev/null | xargs -r -n1 basename | tr '\n' ' ')"

inbox="${BRIDGE:-$HOME/.agent-bridge}/pm"
echo "mailbox: $(ls "$inbox"/*.md 2>/dev/null | wc -l) unread, $(ls "$inbox"/taken/*.md 2>/dev/null | wc -l) taken not acked"
echo "watchers: $(ps -ef | grep -c '[b]ridge-watch.sh')"

free_mb="$(powershell -NoProfile -Command '[int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024)' 2>/dev/null | tr -d '\r')"
[[ -n "$free_mb" ]] && echo "memory: $(( free_mb / 1024 )).$(( free_mb % 1024 * 10 / 1024 )) GB free"

clips="$(find "$host/unity/Builds/demo-videos" -name '*.mp4' -mmin -180 2>/dev/null | xargs -r -n1 basename | tr '\n' ' ')"
echo "clips (3 h): ${clips:-none}"
