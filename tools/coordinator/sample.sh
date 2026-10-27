#!/usr/bin/env bash
# Samples, once a minute, what the PC's workers and Unity are doing, for the
# coordinator's idle-time report (report.sh). Appends one CSV row per minute.
#
#   tools/coordinator/sample.sh [out.csv] [seconds]
#
# A worker counts as active in a minute when its Claude transcript was written
# in the last 3 minutes, or a Unity batch job (a build, or a probe through the
# unity CLI) is running on its checkout. The Mac session is remote and reports
# its own time.
REPO=/c/Users/Andrew/Development/5e-compatible-game
OUT=${1:-$REPO/.claude/worktrees/_coordinator-logs/activity.csv}
EVERY=${2:-60}
T=$HOME/.claude/projects/C--Users-Andrew-Development-5e-compatible-game
# Each PC worker: its session's transcript and the checkout its Unity opens (lower case, forward slashes).
GAMEPLAY_T=$T/bfca4a08-7811-4cf1-949f-2c02c5070ba2.jsonl; GAMEPLAY_P='5e-compatible-game/unity'
WORLD_T=$T/0420374d-e608-4f77-87f0-c60c755c756c.jsonl;    WORLD_P='worktrees/village/unity'
BOT_P='worktrees/playtest-build/unity'

mkdir -p "$(dirname "$OUT")"
[ -f "$OUT" ] || echo "time,gameplay_active,world_active,editors,players,batch_jobs,bot_unity,free_mem_gb,cpu_pct,editor_projects" > "$OUT"

recent() { [ -f "$1" ] && [ $(( $(date +%s) - $(stat -c %Y "$1") )) -le 180 ]; }

while true; do
  snap=$(powershell -NoProfile -Command '
    $os = Get-CimInstance Win32_OperatingSystem
    "MEM " + [math]::Round($os.FreePhysicalMemory / 1MB, 1)
    "CPU " + [int](Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average
    "PLAYERS " + @(Get-Process SpellChemistryValley -ErrorAction SilentlyContinue).Count
    Get-CimInstance Win32_Process -Filter "Name=''Unity.exe''" | ForEach-Object { "U " + $_.CommandLine }' 2>/dev/null | tr -d '\r')
  mem=$(sed -n 's/^MEM //p' <<<"$snap"); cpu=$(sed -n 's/^CPU //p' <<<"$snap"); players=$(sed -n 's/^PLAYERS //p' <<<"$snap")
  procs=$(sed -n 's/^U //p' <<<"$snap" | tr 'A-Z' 'a-z' | sed 's#\\#/#g' | grep -v assetimportworker)
  # An Editor: -projectpath without -batchmode. A batch job: -batchmode, or the unity CLI's "command ... --project-path".
  editors=$(grep -- '-projectpath' <<<"$procs" | grep -v -- '-batchmode' | sed -E 's/.*-projectpath "?([^" ]+).*/\1/' | sort -u)
  batch=$( { grep -- '-batchmode' <<<"$procs" | grep -- '-projectpath' | sed -E 's/.*-projectpath "?([^" ]+).*/\1/'
             grep -E ' command .*--project-path' <<<"$procs" | sed -E 's/.*--project-path "?([^" ]+).*/\1/'; } | sed '/^$/d')
  n_ed=$(sed '/^$/d' <<<"$editors" | wc -l); n_b=$(sed '/^$/d' <<<"$batch" | wc -l)
  ga=0; { recent "$GAMEPLAY_T" || grep -q "$GAMEPLAY_P\$" <<<"$batch"; } && ga=1
  wa=0; { recent "$WORLD_T" || grep -q "$WORLD_P\$" <<<"$batch"; } && wa=1
  bot=0; grep -q "$BOT_P" <<<"$editors$batch" && bot=1
  echo "$(date +%Y-%m-%dT%H:%M:%S),$ga,$wa,$n_ed,${players:-0},$n_b,$bot,${mem:-},${cpu:-},\"$(tr '\n' ';' <<<"$editors" | sed 's/;$//')\"" >> "$OUT"
  sleep "$EVERY"
done
