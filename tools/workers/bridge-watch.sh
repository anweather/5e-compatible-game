#!/usr/bin/env bash
# bridge-watch.sh: the host's watchdog for the bridge mailbox and the two accounts' usage limits.
# A plain loop with no Claude in it, so it keeps working when a session dies, is reaped, or its
# account runs out. Started at sign-in (Startup: bridge-watch.cmd); one copy runs at a time.
# Every 30 s:
#   1. Usage: a new "You've hit your ... limit · resets <time>" line in an account's transcripts
#      means that account is out. It stops that account's running worker VMs with stop.cmd (which
#      pushes their work in progress to wip/ branches first), and mails the other account's role
#      "<account> out until <time>", with what it stopped.
#   2. Back: two minutes after the reset time it mails both roles "<account> is back", and wakes
#      the account's role (bridge.sh wake) if its listener isn't running.
#   3. Delivery: mail unread for 5 min while its role's listener isn't running and its account
#      isn't out wakes the role (at most once every 30 min); the PM has no wake recipe, so its
#      mail is only logged as waiting.
# Everything is logged in bridge.log as "watch". State: $BRIDGE/watch/.
#   BRIDGE_WATCH_DRY=1    log what it would do, but stop no VM and wake nobody (testing)
#   BRIDGE_WATCH_ONCE=1   one pass, then exit (testing)
#   BRIDGE_WATCH_HOMES    override the accounts' config folders: "pro=<dir> max=<dir>" (testing)
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="${BRIDGE:-$HOME/.agent-bridge}"
state="$root/watch"; mkdir -p "$state"
bridge="$here/bridge.sh"
dry="${BRIDGE_WATCH_DRY:-}"
log() { echo "$(date '+%Y-%m-%d %H:%M:%S') watch $*" >> "$root/bridge.log"; [[ -n "${BRIDGE_WATCH_ONCE:-}" ]] && echo "$*"; }

# One copy at a time: a live pid in the lock file means another is running.
lock="$state/pid"
if [[ -z "${BRIDGE_WATCH_ONCE:-}" ]]; then
  old="$(cat "$lock" 2>/dev/null || true)"
  if [[ -n "$old" ]] && kill -0 "$old" 2>/dev/null; then echo "bridge-watch already running (pid $old)."; exit 0; fi
  echo $$ > "$lock"
fi

# Accounts: name, config folder, its role, its worker VMs, the other account's role.
declare -A home=([pro]="$HOME/.claude-pro" [max]="$HOME/.claude")
for kv in ${BRIDGE_WATCH_HOMES:-}; do home[${kv%%=*}]="${kv#*=}"; done
declare -A role=([pro]=coordinator [max]=pm)
declare -A vms=([pro]="w1 w2 u1" [max]="w3 w4")
declare -A other=([pro]=pm [max]=coordinator)
declare -A of_role=([coordinator]=pro [pm]=max)

mail() { # mail <to> <subject> <body>
  printf '%s\n' "$3" | BRIDGE_FROM=watch bash "$bridge" send "$1" "$2" >/dev/null && log "mailed $1: $2"
}
running_vms() { # the given VMs that are running, by Hyper-V
  powershell -NoProfile -Command "Get-VM -ErrorAction SilentlyContinue | Where-Object { \$_.State -eq 'Running' -and ('$*' -split ' ') -contains \$_.Name } | ForEach-Object Name" 2>/dev/null | tr -d '\r'
}
stop_vm() {
  [[ -n "$dry" ]] && { log "dry: would stop $1"; return 0; }
  log "stopping $1 (stop.cmd pushes its work first)"
  cmd //c "$(cygpath -w "$here/stop.cmd")" "$1" 2>&1 | tr -d '\r' | grep -E "pushed|nothing|shut down|Not stopping" | while read -r l; do log "  $1: $l"; done
}
reset_epoch() { # "8:20am" or "Nov 3, 8pm": the next such time, else 5 h from now
  local t; t="$(date -d "$1" +%s 2>/dev/null)" || { echo $(( $(date +%s) + 18000 )); return; }
  (( t > $(date +%s) - 3600 )) || t=$(( t + 86400 ))
  echo "$t"
}
out_until() { cat "$state/$1.out" 2>/dev/null || true; }

check_usage() { # $1 = account
  local a="$1" dir="${home[$1]}/projects" last line ts msg when until stopped=""
  [[ -d "$dir" ]] || return 0
  last="$(cat "$state/$a.seen" 2>/dev/null || echo 0)"
  # Only transcripts written in the last 15 min, and only limit lines newer than the last one handled.
  # The newest limit line, parsed as JSON: "<epoch seconds>\t<message>".
  line="$(find "$dir" -name '*.jsonl' -mmin -15 -print0 2>/dev/null | xargs -0 -r grep -h "hit your" 2>/dev/null | node -e '
    let best = null;
    for (const l of require("fs").readFileSync(0, "utf8").split("\n")) {
      let o; try { o = JSON.parse(l); } catch { continue; }
      // Only the limit notice Claude Code writes itself, not a session quoting one.
      if (!o.isApiErrorMessage || o.error !== "rate_limit") continue;
      const c = o.message?.content, parts = Array.isArray(c) ? c.map(x => x.text ?? "") : [String(c ?? "")];
      const m = parts.join(" ").match(/You.ve hit your [^\n]*?limit[^\n]*/);
      const t = Date.parse(o.timestamp) / 1000;
      // A transcript written to now can hold an old notice: only the last 15 min counts.
      if (!t || Date.now() / 1000 - t > 900) continue;
      if (m && (!best || t > best[0])) best = [Math.floor(t), m[0].trim()];
    }
    if (best) console.log(best.join("\t"));' 2>/dev/null)"
  [[ -n "$line" ]] || return 0
  ts="${line%%$'\t'*}"; msg="${line#*$'\t'}"
  (( ts > last )) || return 0
  echo "$ts" > "$state/$a.seen"
  when="$(sed -n 's/.*resets \([^(]*\).*/\1/p' <<<"$msg" | sed 's/ *$//')"
  until="$(reset_epoch "${when:-}")"
  echo "$until" > "$state/$a.out"
  log "$a is out: \"$msg\" (back after $(date -d "@$until" '+%H:%M'))"
  for vm in $(running_vms ${vms[$a]}); do stop_vm "$vm"; stopped+="$vm "; done
  mail "${other[$a]}" "$a out until $(date -d "@$until" '+%H-%M')" \
"The $a account hit its usage limit (\"$msg\").
Stopped: ${stopped:-no workers were running}. stop.cmd pushed any work in progress as wip/<worker>-<branch>.
Pick up what's urgent on your account; I'll mail again when $a is back."
}

check_back() { # $1 = account
  local a="$1" until; until="$(out_until "$a")"
  [[ -n "$until" ]] || return 0
  (( $(date +%s) >= until + 120 )) || return 0
  rm -f "$state/$a.out"
  log "$a is back"
  mail "${other[$a]}" "$a is back" "The $a account's usage limit has reset."
  mail "${role[$a]}" "your usage is back" "The $a account's usage limit has reset. Read your inbox and carry on with your queue."
  wake "${role[$a]}"
}

wake() { # wake <role>, at most once every 30 min
  local r="$1" lastw
  bash "$bridge" alive "$r" && return 0
  lastw="$(cat "$state/$r.woke" 2>/dev/null || echo 0)"
  (( $(date +%s) - lastw >= 1800 )) || { log "$r not listening; woke it under 30 min ago, waiting"; return 0; }
  [[ -n "$dry" ]] && { log "dry: would wake $r"; date +%s > "$state/$r.woke"; return 0; }
  date +%s > "$state/$r.woke"
  bash "$bridge" wake "$r" 2>&1 | while read -r l; do log "  wake $r: $l"; done
}

check_delivery() { # $1 = role
  local r="$1" a="${of_role[$1]}" oldest age
  shopt -s nullglob; local mailbox=("$root/$r"/*.md); shopt -u nullglob
  (( ${#mailbox[@]} )) || return 0
  oldest="$(ls -tr "${mailbox[@]}" | head -1)"
  age=$(( $(date +%s) - $(date -r "$oldest" +%s) ))
  (( age >= 300 )) || return 0
  bash "$bridge" alive "$r" && return 0
  if [[ -n "$(out_until "$a")" ]]; then return 0; fi   # out of usage: waking it can't help
  if [[ "$r" == pm ]]; then
    # No wake recipe: say so once every 30 min.
    (( $(date +%s) - $(cat "$state/pm.nagged" 2>/dev/null || echo 0) >= 1800 )) || return 0
    date +%s > "$state/pm.nagged"; log "pm has ${#mailbox[@]} unread ($(( age / 60 )) min) and isn't listening"; return 0
  fi
  log "$r has ${#mailbox[@]} unread ($(( age / 60 )) min) and isn't listening: waking it"
  wake "$r"
}

log "started (pid $$${dry:+, dry run})"
while :; do
  for a in pro max; do check_usage "$a"; check_back "$a"; done
  for r in coordinator pm; do check_delivery "$r"; done
  touch "$state/alive"
  [[ -n "${BRIDGE_WATCH_ONCE:-}" ]] && exit 0
  sleep 30
done
