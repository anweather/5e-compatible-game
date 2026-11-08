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
#   3. Delivery: mail taken but not acked in 10 min goes back to the inbox; mail unread for 5 min while its role's listener isn't running and its account
#      isn't out wakes the role (at most once every 30 min); the PM has no wake recipe, so its
#      mail is only logged as waiting.
#   4. Workers (once a minute): each running worker VM's attention prompt and finished reports, read
#      over SSH (Linux) or PowerShell Direct (Windows). Each call is cut off after
#      BRIDGE_WATCH_GUEST_SECS (default 60): one guest that never answers must not stop the loop
#      for the rest (2026-11-06: u2's reports sat on it for a day while the watcher still had a pid).
#      What it sees running, and a worker it can't reach, are logged when they change.
# Everything is logged in bridge.log as "watch". State: $BRIDGE/watch/; "alive" is touched each pass.
#   BRIDGE_WATCH_DRY=1    log what it would do, but stop no VM and wake nobody (testing)
#   BRIDGE_WATCH_ONCE=1   one pass, then exit (testing)
#   BRIDGE_WATCH_HOMES    override the accounts' config folders: "pro=<dir> max=<dir>" (testing)
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="${BRIDGE:-$HOME/.agent-bridge}"
state="$root/watch"; mkdir -p "$state"
bridge="$here/bridge.sh"
dry="${BRIDGE_WATCH_DRY:-}"
# Always true: callers chain on it (mail's "&& log" is mail's status), and outside a test pass the
# "[[ ONCE ]] && echo" was false, so every mail looked failed and no report ever left a worker (2026-11-06).
log() { echo "$(date '+%Y-%m-%d %H:%M:%S') watch $*" >> "$root/bridge.log"; [[ -z "${BRIDGE_WATCH_ONCE:-}" ]] || echo "$*"; }

# One copy at a time: a live pid in the lock file means another is running, unless it hasn't finished
# a pass in 10 min (stuck: a live pid alone said "running" while nothing was watched), so this one replaces it.
lock="$state/pid"
if [[ -z "${BRIDGE_WATCH_ONCE:-}" || -n "${BRIDGE_WATCH_LOCK:-}" ]]; then
  old="$(cat "$lock" 2>/dev/null || true)"
  if [[ -n "$old" && "$old" != $$ ]] && kill -0 "$old" 2>/dev/null; then
    if [[ -e "$state/alive" ]] && (( $(date +%s) - $(date -r "$state/alive" +%s) < 600 )); then echo "bridge-watch already running (pid $old)."; exit 0; fi
    kill "$old" 2>/dev/null; log "replaced pid $old: no pass finished in 10 min"
  fi
  echo $$ > "$lock"
fi

# Accounts: name, config folder, its role, its worker VMs, the other account's role.
declare -A home=([pro]="$HOME/.claude-pro" [max]="$HOME/.claude")
for kv in ${BRIDGE_WATCH_HOMES:-}; do home[${kv%%=*}]="${kv#*=}"; done
declare -A role=([pro]=coordinator [max]=pm)
declare -A vms=([pro]="w1 w2 u1" [max]="w3 w4 u2")
declare -A other=([pro]=pm [max]=coordinator)
declare -A of_role=([coordinator]=pro [pm]=max)

mail() { # mail <to> <subject> <body>
  printf '%s\n' "$3" | BRIDGE_FROM=watch bash "$bridge" send "$1" "$2" >/dev/null && log "mailed $1: $2"
}
# A guest or Hyper-V call, cut off if it hangs. Without coreutils' timeout (a Mac running the tests) it runs as is.
bounded() { if command -v timeout >/dev/null; then timeout "${BRIDGE_WATCH_GUEST_SECS:-60}" "$@"; else "$@"; fi; }
running_vms() { # the given VMs that are running, by Hyper-V
  bounded powershell -NoProfile -Command "Get-VM -ErrorAction SilentlyContinue | Where-Object { \$_.State -eq 'Running' -and ('$*' -split ' ') -contains \$_.Name } | ForEach-Object Name" 2>/dev/null | tr -d '\r'
}
stop_vm() {
  [[ -n "$dry" ]] && { log "dry: would stop $1"; return 0; }
  log "stopping $1 (stop.cmd pushes its work first)"
  cmd //c "$(cygpath -w "$here/stop.cmd")" "$1" 2>&1 | tr -d '\r' | grep -E "pushed|nothing|shut down|Not stopping" | while read -r l; do log "  $1: $l"; done
}
reset_epoch() { # "8:20am (America/Phoenix)", "6:30am (UTC)" or "Nov 3, 8pm": the next such time, else 5 h from now
  # The zone in brackets matters: a VM reports in UTC, the host in local time (2026-10-30: "6:30am (UTC)"
  # was read as local and kept Pro marked out for 7 h after it was back).
  # Node, not date: Git Bash's date has no zone database and reads any named zone as UTC.
  local t; t="$(node -e '
    const s = process.argv[1], m = s.match(/(\d{1,2})(?::(\d\d))?\s*(am|pm)/i); if (!m) process.exit(1);
    const tz = (s.match(/\(([^)]+)\)/) ?? [])[1] ?? Intl.DateTimeFormat().resolvedOptions().timeZone;
    const h = (+m[1] % 12) + (/pm/i.test(m[3]) ? 12 : 0), min = +(m[2] ?? 0), now = Date.now();
    // The zone offset at an instant, from how the zone writes it.
    const parts = at => Object.fromEntries(new Intl.DateTimeFormat("en-US", { timeZone: tz, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }).formatToParts(at).map(x => [x.type, x.value]));
    const offset = at => { const p = parts(at); return Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour, +p.minute, +p.second) - Math.floor(at / 1000) * 1000; };
    const p = parts(now), dated = s.match(/\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(\d{1,2})\b/i);
    const month = dated ? "janfebmaraprmayjunjulaugsepoctnovdec".indexOf(dated[1].toLowerCase()) / 3 : +p.month - 1, day = dated ? +dated[2] : +p.day;
    const at = year => { const wall = Date.UTC(year, month, day, h, min); return wall - offset(wall); };
    let t = at(+p.year);
    // A date names one day: only a date months back means next year (a December notice for January). A stale date stays past, so it never holds an account out.
    if (dated) { if (t < now - 182 * 86400e3) t = at(+p.year + 1); } else if (t < now - 3600e3) t += 86400e3;
    console.log(Math.floor(t / 1000));' "$1" 2>/dev/null)" || { echo $(( $(date +%s) + 18000 )); return; }
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
  when="$(sed -n 's/.*resets \([^(]*([^)]*)\).*/\1/p; t; s/.*resets \([^(]*\).*/\1/p' <<<"$msg" | head -1 | sed 's/ *$//')" # keeps "(zone)"
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
  # The role is told only if it has work waiting: a session closed on purpose (the Pro
  # coordinator, out of the worker loop since 2026-10-30) isn't woken just to hear this.
  if compgen -G "$root/${role[$a]}/*.md" >/dev/null; then
    mail "${role[$a]}" "your usage is back" "The $a account's usage limit has reset. Read your inbox and carry on with your queue."
    wake "${role[$a]}"
  fi
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
  local r="$1" a="${of_role[$1]}" oldest age t
  # Taken but not acked in 10 min: back to the inbox. `wait` does this too, but only while a
  # listener runs; a listener whose session never reacts leaves the mail here (2026-10-30).
  shopt -s nullglob
  for t in "$root/$r"/taken/*.md; do
    (( $(date +%s) - $(date -r "$t" +%s) >= 600 )) || continue
    mv "$t" "$root/$r/" && log "returned $(basename "$t") to $r (taken, not acked in 10 min)"
  done
  local mailbox=("$root/$r"/*.md); shopt -u nullglob
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

guest_ip() { # a Linux worker's IPv4, as Hyper-V reports it
  bounded powershell -NoProfile -Command "(Get-VMNetworkAdapter -VMName $1).IPAddresses | Where-Object { \$_ -match '^\d+\.' } | Select-Object -First 1" 2>/dev/null | tr -d '\r'
}
reached() { # reached <vm> <what> <problem, empty when it worked>: logs a worker the watcher can't reach, and when it can again
  # Removing mailed reports keeps its own record: a read that works between two failed removals mustn't log both every pass.
  local f="$state/$1.reach" doing="read $1's $2" again="reading $1 again" was
  [[ "$2" == outbox-done ]] && { f="$state/$1.done.reach"; doing="remove $1's mailed reports"; again="removing $1's mailed reports again"; }
  was="$(cat "$f" 2>/dev/null || true)"
  [[ "$3" == "$was" ]] && return 0
  if [[ -n "$3" ]]; then echo "$3" > "$f"; log "can't $doing: $3"; else rm -f "$f"; log "$again"; fi
}
linux() { # linux <what> <vm> [args]: linux.sh over SSH, bounded; its errors go to the log, not to /dev/null
  local what="$1" vm="$2" ip out err; shift 2
  ip="$(guest_ip "$vm")"
  [[ -n "$ip" ]] || { reached "$vm" "$what" "Hyper-V reports no IPv4 for it"; return 1; }
  err="$(mktemp)"
  if out="$(bounded bash "${BRIDGE_WATCH_LINUX:-$here/linux.sh}" "$what" "$vm" "$ip" "$@" 2>"$err")"; then
    reached "$vm" "$what" ""; rm -f "$err"; printf '%s\n' "$out"
  else
    local code=$?; reached "$vm" "$what" "linux.sh $what at $ip exited $code$( (( code == 124 )) && echo " (cut off)"): $(tr '\n' ' ' < "$err" | cut -c1-200) $(tail -c 200 <<<"$out" | tr '\n' ' ')"
    rm -f "$err"; return 1
  fi
}

guest_attention() { # the worker's ~/.worker/attention.json (attention.mjs writes it), or nothing
  local vm="$1"
  if [[ "$vm" == u* ]]; then   # Linux workers over SSH
    linux attention "$vm"
  else                          # Windows workers over PowerShell Direct (the scripts' guest account)
    bounded powershell -NoProfile -Command "\$c = New-Object PSCredential('User', (ConvertTo-SecureString 'password' -AsPlainText -Force)); Invoke-Command -VMName $vm -Credential \$c -ErrorAction SilentlyContinue -ScriptBlock { Get-Content \"\$env:USERPROFILE\\.worker\\attention.json\" -ErrorAction SilentlyContinue }" 2>/dev/null | tr -d '\r'
  fi
}

check_attention() { # a running worker stopped at a permission prompt: tell the PM, who tells Andrew
  local vm json t msg cwd seen
  for vm in $(running_vms ${vms[pro]} ${vms[max]}); do
    json="$(guest_attention "$vm")"; [[ -n "$json" ]] || continue
    t="$(node -e 'try { const o = JSON.parse(process.argv[1]); console.log(o.time | 0); } catch { console.log(0); }' "$json")"
    seen="$(cat "$state/$vm.attention" 2>/dev/null || echo 0)"
    (( t > seen )) || continue
    echo "$t" > "$state/$vm.attention"
    msg="$(node -e 'try { const o = JSON.parse(process.argv[1]); console.log(o.message); } catch { }' "$json")"
    cwd="$(node -e 'try { const o = JSON.parse(process.argv[1]); console.log(o.cwd); } catch { }' "$json")"
    log "$vm is waiting at a prompt: $msg"
    mail pm "$vm is waiting for an approval" \
"$vm stopped at a permission prompt $(( ($(date +%s) - t) / 60 )) min ago: \"$msg\" (in $cwd).
Only Andrew can answer it: in the Claude app, open $vm under Remote Control and approve or deny."
  done
}

guest_outbox() { # the worker's finished reports ("=== <file>" then its text each); a Linux worker keeps them until outbox-done
  local vm="$1"
  if [[ "$vm" == u* ]]; then
    linux outbox "$vm"
  else
    bounded powershell -NoProfile -Command "\$c = New-Object PSCredential('User', (ConvertTo-SecureString 'password' -AsPlainText -Force)); Invoke-Command -VMName $vm -Credential \$c -ErrorAction SilentlyContinue -ScriptBlock { Get-ChildItem \"\$env:USERPROFILE\\.worker\\outbox\\*.md\" -ErrorAction SilentlyContinue | ForEach-Object { '=== ' + \$_.Name; Get-Content -Raw \$_.FullName; Remove-Item \$_.FullName } }" 2>/dev/null | tr -d '\r'
  fi
}

check_outbox() { # a worker's report (runner.mjs) goes to the PM's mailbox; a usage limit in it marks the account out
  local vm a raw report file subject when until mailed
  for vm in $(running_vms ${vms[pro]} ${vms[max]}); do
    raw="$(guest_outbox "$vm")"; [[ -n "$raw" ]] || continue
    a=pro; [[ " ${vms[max]} " == *" $vm "* ]] && a=max
    mailed=()
    while IFS= read -r -d $'\x1e' report; do
      file="${report%%$'\n'*}"; report="${report#*$'\n'}"
      [[ -n "${report//[$'\n ']/}" ]] || { mailed+=("$file"); continue; }
      subject="$(sed -n 's/^# //p' <<<"$report" | head -1)"
      if grep -q '^Limit:' <<<"$report"; then
        subject="$subject (usage limit)"
        if [[ -z "$(out_until "$a")" ]]; then
          when="$(grep -o 'resets [^"]*([^)]*)' <<<"$report" | head -1 | sed 's/resets //')"
          until="$(reset_epoch "${when:-}")"; echo "$until" > "$state/$a.out"
          log "$a is out (seen in $vm's report), back after $(date -d "@$until" '+%H:%M')"
        fi
      fi
      mail pm "${subject:-$vm reports}" "$report" && mailed+=("$file")
    done < <(awk 'BEGIN{n=0} /^=== /{if(n)printf "\x1e"; n=1; print substr($0, 5); next} {print} END{if(n)printf "\x1e"}' <<<"$raw")
    # Only what reached the PM's mailbox leaves the worker; the rest is read again next pass.
    # A removal that fails is logged by linux (reached), once until it changes; those reports are mailed again.
    if [[ "$vm" == u* && ${#mailed[@]} -gt 0 ]]; then linux outbox-done "$vm" "${mailed[@]}" >/dev/null || true; fi
  done
}

check_memory() { # free memory under the floor: pause the newest worker VM; resume it when there's room again
  # start.cmd checks memory only at a VM's start; VMs grow after it (Linux to 6 GB, Windows to 8) and so does
  # a host Editor building a player (13.7 GB on 2026-10-31, with 0.7 GB left).
  # Pausing (Save-VM) keeps the guest's session, its work and its address, where stopping lost all three
  # (three stops and an hour lost to a new IP on 2026-11-08). Resume waits for resume_gb so it doesn't flap.
  local floor="${BRIDGE_WATCH_MIN_FREE_GB:-1.5}" resume_gb="${BRIDGE_WATCH_RESUME_GB:-6}" free vm
  free="$(bounded powershell -NoProfile -Command "[math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB,2)" 2>/dev/null | tr -d '\r')"
  [[ -n "$free" ]] || return 0
  if awk -v f="$free" -v m="$floor" 'BEGIN { exit !(f < m) }'; then
    vm="$(bounded powershell -NoProfile -Command "Get-VM | Where-Object { \$_.State -eq 'Running' -and \$_.Name -match '^[wu]\d' } | Sort-Object Uptime | Select-Object -First 1 -ExpandProperty Name" 2>/dev/null | tr -d '\r')"
    [[ -n "$vm" ]] || { log "only $free GB free, and no worker VM to pause"; return 0; }
    log "only $free GB free (floor $floor): pausing $vm, the newest worker"
    if [[ -z "$dry" ]] && bounded powershell -NoProfile -Command "Save-VM -Name '$vm' -ErrorAction Stop" >/dev/null 2>&1; then
      echo "$vm" >> "$state/paused"
      mail pm "$vm paused: memory low" "Only $free GB was free (floor $floor GB), so the watcher paused $vm (Save-VM), the most recently started worker. Its session, work and address are kept; the watcher resumes it once $resume_gb GB are free."
    else
      log "  pausing $vm failed: stopping it instead"; stop_vm "$vm"
      mail pm "$vm stopped: memory low" "Only $free GB was free (floor $floor GB) and pausing $vm failed, so the watcher stopped it. stop.cmd pushed its work in progress as wip/$vm-<branch>. Restart it when there's room (start.cmd $vm), with a brief to resume."
    fi
    return 0
  fi
  [[ -s "$state/paused" ]] || return 0
  awk -v f="$free" -v m="$resume_gb" 'BEGIN { exit !(f >= m) }' || return 0
  vm="$(head -1 "$state/paused")"
  log "$free GB free: resuming $vm"
  if [[ -n "$dry" ]] || bounded powershell -NoProfile -Command "Start-VM -Name '$vm' -ErrorAction Stop" >/dev/null 2>&1; then
    sed -i '1d' "$state/paused"; mail pm "$vm resumed" "$free GB free, so the watcher resumed $vm where it was paused."
  else log "  resuming $vm failed; will try again"; fi
}

log "started (pid $$${dry:+, dry run})"
n=0
while :; do
  check_memory
  for a in pro max; do check_usage "$a"; check_back "$a"; done
  for r in coordinator pm; do check_delivery "$r"; done
  # The guests answer slowly (PowerShell Direct takes seconds): every other pass, once a minute.
  if (( n++ % 2 == 0 )); then
    seen="$(running_vms ${vms[pro]} ${vms[max]} | sort | xargs)"
    [[ "$seen" == "$(cat "$state/running" 2>/dev/null)" ]] || { echo "$seen" > "$state/running"; log "workers running: ${seen:-none}"; }
    check_attention; check_outbox
  fi
  touch "$state/alive"
  [[ -n "${BRIDGE_WATCH_ONCE:-}" ]] && exit 0
  sleep 30
done
