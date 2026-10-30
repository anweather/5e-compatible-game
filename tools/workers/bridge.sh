#!/usr/bin/env bash
# bridge.sh: messages between Claude sessions on different accounts on this PC (they can't
# SendMessage each other), as files in an inbox per role under $BRIDGE (default ~/.agent-bridge).
#   bridge.sh send <to> <subject> < body    write a message into <to>'s inbox (pm, coordinator, ...); prints its id
#   bridge.sh wait <me>                     block until <me>'s inbox has mail, print it, file it in taken/
#   bridge.sh read <me>                     the same without waiting (prints nothing when empty)
#   bridge.sh ack <me> <id>|all             "a Claude has read it": file it in done/
#   bridge.sh status <to> <id>              unread, taken (not acked yet) or acked, with times
#   bridge.sh alive <who>                   exit 0 if <who>'s listener is running
#   bridge.sh wake <who>                    if it isn't, restart its session (replacing the old one, same conversation)
# A session waits with `wait` as a background command: polling files costs no tokens, and the
# command ending is what wakes the session. On each message: ack it as soon as you've read it,
# start the next wait right away (before doing the work), then do the work.
# Delivery is at least once: a message taken but not acked within 10 min (the listener was killed,
# or the session ended, before a Claude saw it) goes back to the inbox. A sender checks `status`:
# not acked within 5 min means nobody is listening, so `wake`.
# `wake` opens a Git Bash window (Claude's screen can freeze in cmd) with Claude on the role's
# account, reachable by Remote Control under the role's name. Everything is logged in bridge.log.
set -euo pipefail
root="${BRIDGE:-$HOME/.agent-bridge}"
cmd="${1:-}"; who="${2:-}"
[[ -n "$cmd" && -n "$who" && "$who" =~ ^[a-z0-9-]+$ ]] || { sed -n 2,18p "$0"; exit 2; }
inbox="$root/$who"
mkdir -p "$inbox/taken" "$inbox/done"
log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $who $*" >> "$root/bridge.log"; }
age() { [[ -e "$1" ]] && echo $(( $(date +%s) - $(date -r "$1" +%s) )) || echo 999999; }

case "$cmd" in
  send)
    subject="${3:?subject}"
    f="$inbox/$(date +%Y%m%d-%H%M%S)-${subject//[^A-Za-z0-9-]/-}.md"
    body="$(cat)"
    # A heredoc that didn't reach stdin sends an empty message (it happened): refuse it.
    [[ -n "${body//[[:space:]]/}" ]] || { echo "Empty message body: nothing sent. Pipe the body in (< file, or a heredoc)."; exit 1; }
    { echo "# $subject"; echo "From: ${BRIDGE_FROM:-?}, $(date '+%Y-%m-%d %H:%M')"; echo; printf '%s
' "$body"; } > "$f.tmp"
    mv "$f.tmp" "$f"   # whole files only: a waiting reader never sees half a message
    log "sent $(basename "$f") from ${BRIDGE_FROM:-?}"
    echo "Sent to $who: $(basename "$f")" ;;
  wait|read)
    [[ "$cmd" == wait ]] && echo $$ > "$inbox/.listener"
    while :; do
      shopt -s nullglob
      for t in "$inbox"/taken/*.md; do   # taken but never acked: deliver again
        (( $(age "$t") < 600 )) || { mv "$t" "$inbox/"; log "returned $(basename "$t") (not acked in 10 min)"; }
      done
      mail=("$inbox"/*.md); shopt -u nullglob
      if (( ${#mail[@]} )); then
        for m in "${mail[@]}"; do
          mv "$m" "$inbox/taken/"; touch "$inbox/taken/$(basename "$m")"
          log "taken $(basename "$m") by pid $$"
          echo "=== $(basename "$m")"; cat "$inbox/taken/$(basename "$m")"; echo
        done
        echo "(Ack each when read: bash $0 ack $who <id>|all)"
        exit 0
      fi
      [[ "$cmd" == read ]] && exit 0
      touch "$inbox/.waiting"
      sleep 15
    done ;;
  ack)
    id="${3:?id or all}"; shopt -s nullglob
    if [[ "$id" == all ]]; then acked=("$inbox"/taken/*.md); else acked=("$inbox/taken/$id"); fi
    for m in "${acked[@]}"; do
      [[ -f "$m" ]] || { echo "Not taken: $(basename "$m")"; continue; }
      mv "$m" "$inbox/done/"; touch "$inbox/done/$(basename "$m")"; log "acked $(basename "$m")"
      echo "Acked $(basename "$m")"
    done ;;
  status)
    id="${3:?id}"
    for state in "" taken done; do
      f="$inbox/${state:+$state/}$id"
      [[ -f "$f" ]] || continue
      when="$(date -r "$f" '+%H:%M')"
      case "$state" in
        "") echo "unread (sent $when, $(( $(age "$f") / 60 )) min ago)" ;;
        taken) echo "taken at $when, not acked yet" ;;
        done) echo "acked at $when" ;;
      esac
      exit 0
    done
    echo "no such message: $id"; exit 1 ;;
  alive)
    # The listener's process is running and still polling (both: a killed one leaves a fresh file).
    pid="$(cat "$inbox/.listener" 2>/dev/null || true)"
    [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null && (( $(age "$inbox/.waiting") < 60 )) ;;
  wake)
    if bash "$0" alive "$who"; then echo "$who is listening; just send."; exit 0; fi
    # Roles: the account's config folder, how to find its running Claude (the command line of the
    # bash that launched it), and the prompt it's restarted with.
    case "$who" in
      coordinator) config="$HOME/.claude-pro"; launcher='workers-startup|\.wake\.sh|claude-pro'
        prompt="Your inbox listener had stopped, so bridge.sh restarted you, continuing this conversation. Run 'bash tools/workers/bridge.sh read coordinator' from your worktree, ack what you read ('bridge.sh ack coordinator all'), start 'bash tools/workers/bridge.sh wait coordinator' as a background command, then carry on with what the PM asks, reporting with 'BRIDGE_FROM=coordinator bash tools/workers/bridge.sh send pm <subject>'." ;;
      *) echo "No wake recipe for $who."; exit 2 ;;
    esac
    # Busy, not gone: a session that wrote its transcript in the last 5 min is mid-turn and starts
    # its wait again when the turn ends. Restarting it would cut that turn off.
    newest="$(find "$config/projects" -name '*.jsonl' -mmin -5 2>/dev/null | head -1)"
    [[ -z "$newest" ]] || { log "not woken: busy (wrote its log in the last 5 min)"; echo "$who is busy (wrote its log in the last 5 min); not waking it."; exit 0; }
    # Replace, don't add: stop the role's running Claude (found by its launcher), so there's only
    # ever one, then continue its last conversation in the main checkout, where it was started.
    pids="$(powershell -NoProfile -Command "\$p = Get-CimInstance Win32_Process; \$p | Where-Object { \$_.Name -eq 'claude.exe' -and \$_.CommandLine -like '*.local\\bin\\claude.exe*' } | Where-Object { \$c = \$_.ParentProcessId; (\$p | Where-Object ProcessId -eq \$c).CommandLine -match '$launcher' } | ForEach-Object ProcessId" 2>/dev/null | tr -d '\r' | xargs)"
    if [[ -n "$pids" ]]; then
      for p in $pids; do powershell -NoProfile -Command "Stop-Process -Id $p -Force" 2>/dev/null; done
      log "stopped the old $who session (pid $pids)"
    fi
    main="$(cd "$(dirname "$(git -C "$(dirname "$0")" rev-parse --path-format=absolute --git-common-dir)")" && pwd)"
    # The launch goes in a script file: quotes don't survive cmd's start. The listener is a
    # background command, which Claude Code would otherwise stop when memory runs low.
    printf 'cd %q && CLAUDE_CODE_DISABLE_BG_SHELL_PRESSURE_REAP=1 CLAUDE_CONFIG_DIR=%q ~/.local/bin/claude.exe --continue --remote-control %q %q\nexec bash\n' "$main" "$config" "$who" "$prompt" > "$inbox/.wake.sh"
    cmd //c start "" "C:\Program Files\Git\git-bash.exe" "$(cygpath -w "$inbox/.wake.sh")"
    log "woke $who (continuing its last conversation)"
    echo "Restarted $who, continuing its last conversation (a Git Bash window; Remote Control name: $who). Your mail waits in its inbox." ;;
  *) sed -n 2,18p "$0"; exit 2 ;;
esac
