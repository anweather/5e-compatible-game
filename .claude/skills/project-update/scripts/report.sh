#!/usr/bin/env bash
# Summarises sample.sh's log: active % per worker, how much of the window Unity
# ran, and the longest stretches with no Unity at all.
#
#   report.sh [hours]   # default 4
#   report.sh --idle    # minutes since any Unity last ran (0 when there are no samples yet)
DIR=$(cd "$(dirname "$0")" && pwd)
LOG=${LOG:-$DIR/activity.csv}
[ -s "$LOG" ] || { [ "$1" = "--idle" ] && echo 0 || echo "No samples yet."; exit 0; }

# The fixed columns come after the per-worker "<name>_active" columns.
awk_common='
  NR==1 { for (i=1; i<=NF; i++) { if ($i ~ /_active$/) { w[++nw]=i; wn[nw]=substr($i,1,length($i)-7) }
          if ($i=="editors") ce=i; if ($i=="players") cp=i; if ($i=="batch_jobs") cb=i
          if ($i=="free_mem_gb") cm=i; if ($i=="cpu_pct") cc=i }; next }'

if [ "$1" = "--idle" ]; then
  awk -F, -v now="$(date +%s)" "$awk_common"'
    { cmd = "date -d " $1 " +%s 2>/dev/null || date -j -f %Y-%m-%dT%H:%M:%S " $1 " +%s"; cmd | getline t; close(cmd)
      if (!first) first=t; if ($ce+$cp+$cb>0) last=t }
    END { print (first ? int((now - (last ? last : first)) / 60) : 0) }' "$LOG"
  exit 0
fi

since=$(date -d "-${1:-4} hours" +%Y-%m-%dT%H:%M:%S 2>/dev/null || date -v-"${1:-4}"H +%Y-%m-%dT%H:%M:%S)
awk -F, -v since="$since" "$awk_common"'
  $1 >= since {
    n++; if (n==1) first=substr($1,12,5); lastt=substr($1,12,5)
    for (k=1; k<=nw; k++) act[k]+=$(w[k])
    u = ($ce+$cp+$cb>0); if (u) un++; if ($cp+$cb>0) work++; if ($ce>=2) two++
    if ($cm!="" && (minm=="" || $cm+0<minm+0)) minm=$cm; cpu+=$cc
    if (!u) { if (gs=="") gs=substr($1,12,5); gl++ } else if (gs!="") { gap[gs]=gl; gs=""; gl=0 }
  }
  END {
    if (!n) { print "No samples in the window."; exit }
    if (gs!="") gap[gs]=gl
    printf "Window %s-%s, %d samples\n", first, lastt, n
    for (k=1; k<=nw; k++) printf "  %-10s active %3d%%  idle %3d min\n", wn[k], 100*act[k]/n, n-act[k]
    printf "  Unity running %d%%; working (build, probe, player) %d%%; two Editors at once %d%%\n", 100*un/n, 100*work/n, 100*two/n
    printf "  Free memory min %s GB; CPU mean %d%%\n", minm, cpu/n
    s=""; for (k in gap) if (gap[k]>=5) s = s sprintf("%s for %d min; ", k, gap[k])
    print "  No-Unity gaps of 5 min or more: " (s=="" ? "none" : s)
  }' "$LOG"
