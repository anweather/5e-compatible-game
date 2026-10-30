#!/usr/bin/env bash
# Logs, once a minute, which local Claude workers are active and what Unity is
# doing, for report.sh. Appends one CSV row per sample.
#
#   sample.sh [out.csv] [seconds]
#
# Configure: workers.conf beside this script (see workers.conf.example), and
# GAME_EXE, your built game's process name (without .exe), so test players count.
# Works on Windows (Git Bash, using inline PowerShell commands; no .ps1 files,
# so no execution policy is involved), macOS and Linux.
DIR=$(cd "$(dirname "$0")" && pwd)
OUT=${1:-$DIR/activity.csv}
EVERY=${2:-60}
GAME_EXE=${GAME_EXE:-MyGame}
CONF=${CONF:-$DIR/workers.conf}
[ -f "$CONF" ] || { echo "No $CONF: copy workers.conf.example and edit it." >&2; exit 1; }

names=(); transcripts=(); projects=()
while IFS='|' read -r n t p; do
  case "$n" in ''|\#*) continue;; esac
  names+=("$n"); transcripts+=("${t/#\~/$HOME}"); projects+=("$(printf '%s' "$p" | tr 'A-Z' 'a-z' | sed 's#\\#/#g; s#/$##')")
done < "$CONF"

if [ ! -f "$OUT" ]; then
  h="time"; for n in "${names[@]}"; do h="$h,${n}_active"; done
  echo "$h,editors,players,batch_jobs,free_mem_gb,cpu_pct,editor_projects" > "$OUT"
fi

recent() { [ -f "$1" ] && [ $(( $(date +%s) - $(stat -c %Y "$1" 2>/dev/null || stat -f %m "$1") )) -le 180 ]; }

snapshot() {   # prints: MEM <gb>, CPU <pct>, PLAYERS <n>, then "U <unity command line>" per Unity process
  if command -v powershell >/dev/null 2>&1; then
    powershell -NoProfile -Command '
      $os = Get-CimInstance Win32_OperatingSystem
      "MEM " + [math]::Round($os.FreePhysicalMemory / 1MB, 1)
      "CPU " + [int](Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average
      "PLAYERS " + @(Get-Process '"$GAME_EXE"' -ErrorAction SilentlyContinue).Count
      Get-CimInstance Win32_Process | Where-Object { $_.Name -eq "Unity.exe" } | ForEach-Object { "U " + $_.CommandLine }' 2>/dev/null | tr -d '\r'
  else
    if [ -r /proc/meminfo ]; then echo "MEM $(awk '/MemAvailable/ {printf "%.1f", $2/1048576}' /proc/meminfo)"
    else echo "MEM $(vm_stat | awk '/free|inactive/ {gsub(/\./,"",$NF); s+=$NF} END {printf "%.1f", s*4096/1073741824}')"; fi
    echo "CPU $(ps -A -o %cpu= | awk '{s+=$1} END {printf "%d", s/'"$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1)"'}')"
    echo "PLAYERS $(pgrep -x "$GAME_EXE" | wc -l | tr -d ' ')"
    ps -A -o command= | grep -i '[u]nity' | sed 's/^/U /'
  fi
}

while true; do
  snap=$(snapshot)
  mem=$(sed -n 's/^MEM //p' <<<"$snap"); cpu=$(sed -n 's/^CPU //p' <<<"$snap"); players=$(sed -n 's/^PLAYERS //p' <<<"$snap")
  procs=$(sed -n 's/^U //p' <<<"$snap" | tr 'A-Z' 'a-z' | sed 's#\\#/#g' | grep -v assetimportworker)
  # An Editor: -projectpath without -batchmode. A batch job: -batchmode, or the unity CLI driving an Editor.
  editors=$(grep -- '-projectpath' <<<"$procs" | grep -v -- '-batchmode' | sed -E 's/.*-projectpath "?([^" ]+).*/\1/' | sed 's#/$##' | sort -u)
  batch=$( { grep -- '-batchmode' <<<"$procs" | grep -- '-projectpath' | sed -E 's/.*-projectpath "?([^" ]+).*/\1/'
             grep -E ' command .*--project-path' <<<"$procs" | sed -E 's/.*--project-path "?([^" ]+).*/\1/'; } | sed 's#/$##; /^$/d')
  row=$(date +%Y-%m-%dT%H:%M:%S)
  for i in "${!names[@]}"; do
    a=0; { recent "${transcripts[$i]}" || grep -qxF "${projects[$i]}" <<<"$batch"; } && a=1
    row="$row,$a"
  done
  n_ed=$(sed '/^$/d' <<<"$editors" | wc -l | tr -d ' '); n_b=$(sed '/^$/d' <<<"$batch" | wc -l | tr -d ' ')
  echo "$row,$n_ed,${players:-0},$n_b,${mem:-},${cpu:-},\"$(tr '\n' ';' <<<"$editors" | sed 's/;$//')\"" >> "$OUT"
  sleep "$EVERY"
done
