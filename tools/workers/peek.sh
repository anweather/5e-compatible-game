#!/usr/bin/env bash
# peek.sh: what the workers are doing, read from their own Claude transcripts inside the VMs
# (Windows over PowerShell Direct, Linux over SSH), so it costs the workers nothing.
#   peek.sh                 one line per running worker: its brief, minutes, tokens, last action
#   peek.sh <vm> [N]        that worker's last N steps (default 15): what it said, each tool call,
#                           failures, compactions, a usage limit; then the runner's log
#   peek.sh <vm> --follow   the same, again every 30 s (Ctrl-C to stop)
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
pass='password' # the scripts' guest account (start.cmd, wip.cmd), never a person's

running() { powershell -NoProfile -Command "Get-VM | Where-Object { \$_.State -eq 'Running' -and \$_.Name -match '^[wu]\d' } | ForEach-Object Name" 2>/dev/null | tr -d '\r'; }

# The newest transcript's last 600 lines, a marker, then the runner's last 5 log lines.
fetch() {
  local vm="$1"
  if [[ "$vm" == u* ]]; then
    local ip; ip="$(powershell -NoProfile -Command "(Get-VMNetworkAdapter -VMName $vm).IPAddresses | Where-Object { \$_ -match '^\d+\.' } | Select-Object -First 1" 2>/dev/null | tr -d '\r')"
    ssh -o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR -o ConnectTimeout=5 "${WORKER_USER:-anweather}@$ip" \
      'f=$(ls -t $(find ~/.claude/projects -name "*.jsonl" 2>/dev/null) 2>/dev/null | head -1); [ -n "$f" ] && tail -n 600 "$f"; echo "@@RUNNER@@"; tail -n 5 ~/.worker/runner.log 2>/dev/null'
  else
    powershell -NoProfile -Command "\$c = New-Object PSCredential('User', (ConvertTo-SecureString '$pass' -AsPlainText -Force)); Invoke-Command -VMName $vm -Credential \$c -ErrorAction SilentlyContinue -ScriptBlock { \$f = Get-ChildItem \"\$env:USERPROFILE\\.claude\\projects\" -Recurse -Filter *.jsonl -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if (\$f) { Get-Content \$f.FullName -Tail 600 }; '@@RUNNER@@'; Get-Content \"\$env:USERPROFILE\\.worker\\runner.log\" -Tail 5 -ErrorAction SilentlyContinue }" 2>/dev/null | tr -d '\r'
  fi
}

summarize() { # $1 = vm, $2 = steps (0: one line), stdin = fetch output
  node -e '
const [vm, steps] = [process.argv[1], Number(process.argv[2])];
const [tx, runner = ""] = require("fs").readFileSync(0, "utf8").split("@@RUNNER@@");
const ev = [], seen = new Set(); let tok = { in: 0, cw: 0, cr: 0, out: 0 }, first = 0, last = 0, compact = 0, limit = "", cwd = "", fails = 0;
const short = (s, n = 110) => String(s ?? "").replace(/\s+/g, " ").trim().slice(0, n);
for (const l of tx.split("\n")) {
  let o; try { o = JSON.parse(l); } catch { continue; }
  const t = Date.parse(o.timestamp) || 0; if (t) { first ||= t; last = t; }
  if (o.cwd) cwd = o.cwd;
  if (o.isCompactSummary || o.subtype === "compact_boundary") { compact++; ev.push([t, "· compacted"]); }
  if (o.isApiErrorMessage) { const m = JSON.stringify(o.message ?? "").match(/hit your[^"\\]*/); if (m) limit = m[0]; ev.push([t, "! " + short(m?.[0] ?? o.error)]); }
  const m = o.message; if (!Array.isArray(m?.content)) continue;
  if (o.type === "assistant" && m.usage && !seen.has(m.id)) { seen.add(m.id); const u = m.usage; tok.in += u.input_tokens ?? 0; tok.cw += u.cache_creation_input_tokens ?? 0; tok.cr += u.cache_read_input_tokens ?? 0; tok.out += u.output_tokens ?? 0; }
  for (const c of m.content) {
    if (o.type === "assistant" && c.type === "text" && c.text.trim()) ev.push([t, "» " + short(c.text)]);
    if (c.type === "tool_use") { const i = c.input ?? {}; ev.push([t, `$ ${c.name} ${short(i.command ?? i.file_path ?? i.pattern ?? i.description ?? JSON.stringify(i), 90)}`]); }
    if (c.type === "tool_result" && c.is_error) { fails++; ev.push([t, "✗ " + short(typeof c.content === "string" ? c.content : JSON.stringify(c.content), 90)]); }
  }
}
const K = n => n >= 1e6 ? (n / 1e6).toFixed(1) + "M" : n >= 1e3 ? Math.round(n / 1e3) + "k" : n;
const ite = tok.in + 1.25 * tok.cw + 0.1 * tok.cr + 5 * tok.out;
const ago = last ? Math.round((Date.now() - last) / 60000) : "?";
const brief = (runner.match(/start (\S+)(?![\s\S]*start )/) ?? [])[1] ?? "-";
const busy = /start \S+[^\n]*\n?(?![\s\S]*done)/.test(runner.split("\n").slice(-3).join("\n")) || /start [^\n]*$/.test(runner.trim().split("\n").pop() ?? "");
const head = `${vm.padEnd(3)} ${busy ? "working" : "idle   "} ${brief.slice(0, 40).padEnd(40)} ${String(first ? Math.round((last - first) / 60000) : 0).padStart(4)} min  ${String(K(ite)).padStart(6)} ite  last ${ago} min ago${compact ? `  ${compact} compaction(s)` : ""}${limit ? "  LIMIT: " + limit : ""}`;
if (!steps) { console.log(head + "\n    " + (ev.at(-1)?.[1] ?? "(no transcript)")); process.exit(); }
console.log(head);
console.log(`    in ${cwd || "?"}; tokens: input ${K(tok.in)}, cache write ${K(tok.cw)}, cache read ${K(tok.cr)}, output ${K(tok.out)}; ${seen.size} replies, ${fails} failed tool calls`);
for (const [t, s] of ev.slice(-steps)) console.log(`    ${t ? new Date(t).toTimeString().slice(0, 8) : "        "} ${s}`);
console.log("  runner:"); for (const l of runner.trim().split("\n").filter(Boolean)) console.log("    " + l);
' "$1" "$2"
}

case "${1:-}" in
  "") vms="$(running)"; [[ -n "$vms" ]] || { echo "No workers running."; exit 0; }
      for vm in $vms; do fetch "$vm" | summarize "$vm" 0; done ;;
  -h|--help) sed -n 2,7p "$0" ;;
  *)  vm="$1"; n="${2:-15}"
      if [[ "$n" == --follow ]]; then while :; do clear; fetch "$vm" | summarize "$vm" 25; sleep 30; done
      else fetch "$vm" | summarize "$vm" "$n"; fi ;;
esac
