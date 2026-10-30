// commit-cost.mjs <message file>: appends what this commit's work cost to its message, as trailers:
//   Worker: w1
//   Worker-Account: pro (org 70e49c5a)
//   Worker-Time: 8 min
//   Worker-Tokens: 154,567 new + 1,080,000 cache read (input 3,210, cache write 120,000, output 31,357; 85 replies; claude-opus-5-5)
//   Worker-Context: peak 96,000
//   Worker-Tools: 61 calls, 4 failed; tests: dotnet test x3, npm test x2
//   Worker-Trouble: 1 compaction, 2 API errors          (only when there was any)
// It reads the token counts Claude Code already logs (~/.claude/projects/**/*.jsonl), so measuring costs
// the agent nothing. "This commit's work" is every reply since the last commit on this VM, or since
// boot for the first one. start.cmd installs it with the prepare-commit-msg hook in each worker.
// The coordinator runs it on its own merges (land.sh) with COMMIT_COST_ROLE=Coordinator: the lines
// are then Coordinator-*, from $CLAUDE_CONFIG_DIR's logs, which is every session of that account on
// the host since its last merge.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execSync } from 'node:child_process';

const msgFile = process.argv[2];
const home = os.homedir();
const role = process.env.COMMIT_COST_ROLE || 'Worker';
const config = process.env.CLAUDE_CONFIG_DIR || path.join(home, '.claude');
const stateFile = path.join(config, `.${role.toLowerCase()}-last-commit.txt`);
// A rebase or amend replays a message that already carries its cost: leave it as it is.
if (new RegExp(`^${role}-Tokens: `, 'm').test(fs.readFileSync(msgFile, 'utf8'))) process.exit(0);
let since = Number(process.env.COMMIT_COST_SINCE) || Date.now() - os.uptime() * 1000; // the override is for testing
try { since = Math.max(since, Number(fs.readFileSync(stateFile, 'utf8'))); } catch { }

function* transcripts(dir) {
  let entries = [];
  try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
  for (const e of entries) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) yield* transcripts(p);
    else if (e.name.endsWith('.jsonl') && fs.statSync(p).mtimeMs >= since) yield p;
  }
}

// What the test commands look like, by the name they're counted under.
const tests = [
  ['dotnet test', /\bdotnet test\b/], ['npm test', /\bnpm (run )?test\b/], ['Unity tests', /-runTests\b/],
  ['probe', /\bprobe\.sh\b/], ['jscpd', /\bjscpd\b/],
];
// One reply can span several lines with the same message id and usage: count each id once.
const replies = new Map();
const tools = new Map(); // tool_use id -> its command (Bash/PowerShell), else ''
const failed = new Set();
let compactions = 0, apiErrors = 0;
// On the host many sessions share one account: COMMIT_COST_PROJECT keeps only the transcript
// folder with exactly that name (Claude Code names it after the folder the session started in).
const only = process.env.COMMIT_COST_PROJECT;
const projectDirs = (() => {
  const root = path.join(config, 'projects');
  if (!only) return [root];
  try { return fs.readdirSync(root).filter(d => d === only).map(d => path.join(root, d)); } catch { return []; }
})();
for (const file of projectDirs.flatMap(d => [...transcripts(d)])) {
  for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
    let o;
    try { o = JSON.parse(line); } catch { continue; }
    const t = Date.parse(o.timestamp);
    if (!(t > since)) continue;
    const m = o.message;
    if (o.isCompactSummary || o.subtype === 'compact_boundary') compactions++;
    if (o.isApiErrorMessage || (o.type === 'system' && /api_error/i.test(o.subtype ?? ''))) apiErrors++;
    if (!Array.isArray(m?.content)) continue;
    for (const c of m.content) {
      if (c.type === 'tool_use') tools.set(c.id, typeof c.input?.command === 'string' ? c.input.command : '');
      if (c.type === 'tool_result' && c.is_error) failed.add(c.tool_use_id);
    }
    if (o.type === 'assistant' && m.usage) replies.set(m.id ?? `${file}:${t}`, { t, model: m.model, u: m.usage });
  }
}

const sum = { input: 0, write: 0, read: 0, output: 0 };
const models = new Set();
let first = Date.now(), peak = 0;
for (const { t, model, u } of replies.values()) {
  const input = u.input_tokens ?? 0, write = u.cache_creation_input_tokens ?? 0, read = u.cache_read_input_tokens ?? 0;
  sum.input += input; sum.write += write; sum.read += read; sum.output += u.output_tokens ?? 0;
  peak = Math.max(peak, input + write + read);
  if (model && !model.startsWith('<')) models.add(model);
  first = Math.min(first, t);
}
const runs = tests.map(([name, re]) => [name, [...tools.values()].filter((cmd) => re.test(cmd)).length]).filter(([, k]) => k);

// The account: its plan and org from `claude auth status` (local, no tokens).
let account = '?';
try {
  const s = JSON.parse(execSync('claude auth status', { encoding: 'utf8', timeout: 20000, stdio: ['ignore', 'pipe', 'ignore'] }));
  account = `${s.subscriptionType ?? '?'}${s.orgId ? ` (org ${s.orgId.slice(0, 8)})` : ''}`;
} catch { }

const n = (x) => x.toLocaleString('en-US');
// Cache reads are the context re-read on every reply: cheap per token but huge, so they're kept apart.
const fresh = sum.input + sum.write + sum.output;
let name = os.hostname();
try { name = fs.readFileSync('C:/ProgramData/worker/name.txt', 'utf8').trim().split(' ')[0] || name; } catch { }
if (role !== 'Worker') name = process.env.COMMIT_COST_NAME || name;
const plural = (k, word) => `${k} ${word}${k > 1 ? 's' : ''}`;
const trouble = [compactions && plural(compactions, 'compaction'), apiErrors && plural(apiErrors, 'API error')].filter(Boolean);
const lines = [
  `${role}: ${name}`,
  `${role}-Account: ${account}`,
  `${role}-Time: ${replies.size ? Math.max(1, Math.round((Date.now() - first) / 60000)) : 0} min`,
  `${role}-Tokens: ${n(fresh)} new + ${n(sum.read)} cache read (input ${n(sum.input)}, cache write ${n(sum.write)}, output ${n(sum.output)}; ${replies.size} replies; ${[...models].join(', ') || 'no model'})`,
  `${role}-Context: peak ${n(peak)}`,
  `${role}-Tools: ${tools.size} calls, ${failed.size} failed; tests: ${runs.map(([k, c]) => `${k} x${c}`).join(', ') || 'none'}`,
  ...(trouble.length ? [`${role}-Trouble: ${trouble.join(', ')}`] : []),
];

const msg = fs.readFileSync(msgFile, 'utf8').replace(/\s+$/, '');
// Trailers go in the last paragraph, beside Co-Authored-By if it's there.
const lastPara = msg.split(/\n\s*\n/).pop();
const sep = /^[\w-]+: /m.test(lastPara) && msg.includes('\n\n') ? '\n' : '\n\n';
fs.writeFileSync(msgFile, msg + sep + lines.join('\n') + '\n');
fs.mkdirSync(path.dirname(stateFile), { recursive: true });
fs.writeFileSync(stateFile, String(Date.now()));
