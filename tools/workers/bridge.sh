#!/usr/bin/env bash
# bridge.sh: messages between Claude sessions on different accounts on this PC (they can't
# SendMessage each other), as files in an inbox per role under $BRIDGE (default ~/.agent-bridge).
#   bridge.sh send <to> <subject> < body    write a message into <to>'s inbox (pm, coordinator, ...)
#   bridge.sh wait <me>                     block until <me>'s inbox has mail, print it, file it in done/
#   bridge.sh read <me>                     the same without waiting (prints nothing when empty)
# A session waits with `wait` as a background command: polling files costs no tokens, and the
# command ending is what wakes the session. Run the next wait after handling each message.
set -euo pipefail
root="${BRIDGE:-$HOME/.agent-bridge}"
cmd="${1:-}"; who="${2:-}"
[[ -n "$cmd" && -n "$who" && "$who" =~ ^[a-z0-9-]+$ ]] || { sed -n 2,8p "$0"; exit 2; }
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
        exit 0
      fi
      [[ "$cmd" == read ]] && exit 0
      sleep 20
    done ;;
  *) sed -n 2,8p "$0"; exit 2 ;;
esac
