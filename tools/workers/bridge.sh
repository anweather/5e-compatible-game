#!/usr/bin/env bash
# bridge.sh: messages between Claude sessions on different accounts on this PC (they can't
# SendMessage each other), as files in an inbox per role under $BRIDGE (default ~/.agent-bridge).
#   bridge.sh send <to> <subject> < body    write a message into <to>'s inbox (pm, coordinator, ...)
#   bridge.sh wait <me>                     block until <me>'s inbox has mail, print it, file it in done/
#   bridge.sh read <me>                     the same without waiting (prints nothing when empty)
#   bridge.sh alive <who>                   exit 0 if <who> is waiting (heartbeat < 2 min) or took mail < 1 h ago
#   bridge.sh wake <who>                    if <who> isn't, start a new session for it (see roles below)
# A session waits with `wait` as a background command: polling files costs no tokens, and the
# command ending is what wakes the session. Run the next wait after handling each message.
# While it waits it touches <inbox>/.waiting, and taking mail touches .took (it's busy on that for a
# while), so `alive` tells a sleeping or ended session from one that's listening or working. `wake` opens a Git Bash window (Claude's screen can freeze in cmd) with Claude on
# the role's account, reachable by Remote Control under the role's name.
set -euo pipefail
root="${BRIDGE:-$HOME/.agent-bridge}"
cmd="${1:-}"; who="${2:-}"
[[ -n "$cmd" && -n "$who" && "$who" =~ ^[a-z0-9-]+$ ]] || { sed -n 2,14p "$0"; exit 2; }
inbox="$root/$who"
mkdir -p "$inbox/done"

case "$cmd" in
  send)
    subject="${3:?subject}"
    f="$inbox/$(date +%Y%m%d-%H%M%S)-${subject//[^A-Za-z0-9-]/-}.md"
    { echo "# $subject"; echo "From: ${BRIDGE_FROM:-?}, $(date '+%Y-%m-%d %H:%M')"; echo; cat; } > "$f.tmp"
    mv "$f.tmp" "$f"   # whole files only: a waiting reader never sees half a message
    echo "Sent to $who: $(basename "$f")" ;;
  wait|read)
    while :; do
      shopt -s nullglob; mail=("$inbox"/*.md); shopt -u nullglob
      if (( ${#mail[@]} )); then
        for m in "${mail[@]}"; do echo "=== $(basename "$m")"; cat "$m"; echo; mv "$m" "$inbox/done/"; done
        touch "$inbox/.took"
        exit 0
      fi
      [[ "$cmd" == read ]] && exit 0
      touch "$inbox/.waiting"
      sleep 20
    done ;;
  alive)
    age() { [[ -f "$inbox/$1" ]] && echo $(( $(date +%s) - $(date -r "$inbox/$1" +%s) )) || echo 999999; }
    (( $(age .waiting) < 120 || $(age .took) < 3600 )) ;;
  wake)
    if bash "$0" alive "$who"; then echo "$who is listening; just send."; exit 0; fi
    # Roles: the account's config folder and the first prompt of its new session.
    case "$who" in
      coordinator) config="$HOME/.claude-pro"
        prompt="You are the Pro account's coordinator of the worker VMs w1 and w2 (your earlier session ended). Update this worktree to origin/dev (git fetch; git switch -C $who origin/dev), then read docs/handoffs/pro-coordinator.md, docs/handoffs/pro-coordinator-report.md and tools/workers/README.md. Run 'bash tools/workers/bridge.sh read coordinator' and do what the PM asks there, reporting back with 'BRIDGE_FROM=coordinator bash tools/workers/bridge.sh send pm <subject>'. Keep 'bash tools/workers/bridge.sh wait coordinator' running as a background command whenever you're idle." ;;
      *) echo "No wake recipe for $who."; exit 2 ;;
    esac
    # Its own worktree, so the main checkout's branch is left alone.
    main="$(cd "$(dirname "$(git -C "$(dirname "$0")" rev-parse --path-format=absolute --git-common-dir)")" && pwd)"
    repo="$main/.claude/worktrees/$who"
    [[ -d "$repo" ]] || git -C "$main" worktree add -q --detach "$repo" origin/dev
    # The launch goes in a script file: quotes don't survive cmd's start.
    printf 'cd %q && CLAUDE_CONFIG_DIR=%q claude --remote-control %q %q\nexec bash\n' "$repo" "$config" "$who" "$prompt" > "$inbox/.wake.sh"
    cmd //c start "" "C:\Program Files\Git\git-bash.exe" "$(cygpath -w "$inbox/.wake.sh")"
    echo "Started a new $who session (a Git Bash window; Remote Control name: $who). Your mail waits in its inbox." ;;
  *) sed -n 2,14p "$0"; exit 2 ;;
esac
