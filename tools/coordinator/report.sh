#!/usr/bin/env bash
# Summarises sample.sh's log for the coordinator's project update: how much of
# the window each PC worker was active, how much of it at least one Unity was
# running, and the longest stretches with no Unity at all.
#
#   tools/coordinator/report.sh [hours]      # default 4
#   tools/coordinator/report.sh --idle       # minutes since any Unity last ran
LOG=${LOG:-/c/Users/Andrew/Development/5e-compatible-game/.claude/worktrees/_coordinator-logs/activity.csv}
[ -s "$LOG" ] || { echo "No samples yet."; exit 0; }
if [ "$1" = "--idle" ]; then
  awk -F, -v now="$(date +%s)" 'NR>1 { "date -d " $1 " +%s" | getline t; close("date -d " $1 " +%s"); if (NR==2) first=t; if ($4+$5+$6>0) last=t }
    END { print int((now - (last ? last : first)) / 60) }' "$LOG"
  exit 0
fi
since=$(date -d "-${1:-4} hours" +%Y-%m-%dT%H:%M:%S)
awk -F, -v since="$since" '
  NR>1 && $1 >= since {
    n++; if (n==1) first=substr($1,12,5); lastt=substr($1,12,5)
    g+=$2; w+=$3
    u = ($4+$5+$6>0); if (u) un++; if ($5+$6>0) work++; if ($4>=2) two++
    if ($8!="" && (minm=="" || $8<minm)) minm=$8; cpu+=$9
    if (!u) { if (gs=="") gs=substr($1,12,5); gl++ } else if (gs!="") { gap[gs]=gl; gs=""; gl=0 }
  }
  END {
    if (!n) { print "No samples in the window."; exit }
    if (gs!="") gap[gs]=gl
    printf "Window %s-%s, %d samples (1 a minute)\n", first, lastt, n
    printf "  gameplay  active %3d%%  idle %3d min\n", 100*g/n, n-g
    printf "  world     active %3d%%  idle %3d min\n", 100*w/n, n-w
    printf "  Unity running %d%% of the time; working (build, probe, player) %d%%; two Editors at once %d%%\n", 100*un/n, 100*work/n, 100*two/n
    printf "  Free memory min %s GB; CPU mean %d%%\n", minm, cpu/n
    s=""; for (k in gap) if (gap[k]>=5) s = s sprintf("%s for %d min; ", k, gap[k])
    print "  No-Unity gaps of 5 min or more: " (s=="" ? "none" : s)
  }' "$LOG"
