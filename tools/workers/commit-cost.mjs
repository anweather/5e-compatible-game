// commit-cost.mjs <message file>: appends what this commit's work cost to its message, as trailers:
//   Worker: w1
//   Worker-Time: 8 min
//   Worker-Tokens: 154,567 new + 1,080,000 cache read (input 3,210, cache write 120,000, output 31,357; 85 replies; claude-opus-5-5)
// It reads the token counts Claude Code already logs (~/.claude/projects/**/*.jsonl), so measuring costs
// the agent nothing. "This commit's work" is every reply since the last commit on this VM, or since
// boot for the first one. start.cmd installs it with the prepare-commit-msg hook in each worker.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const msgFile = process.argv[2];
const home = os.homedir();
const stateFile = path.join(home, '.worker', 'last-commit.txt');
const bootMs = Date.now() - os.uptime() * 1000;
let since = Number(process.env.COMMIT_COST_SINCE) || bootMs; // the override is for testing
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

// One reply can span several lines with the same message id and usage: count each id once.
const replies = new Map();
for (const file of transcripts(path.join(home, '.claude', 'projects'))) {
  for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
    if (!line.includes('"usage"')) continue;
    let o;
    try { o = JSON.parse(line); } catch { continue; }
    const t = Date.parse(o.timestamp);
    const m = o.message;
    if (o.type !== 'assistant' || !m?.usage || !(t > since)) continue;
    replies.set(m.id ?? `${file}:${t}`, { t, model: m.model, u: m.usage });
  }
}

const sum = { input: 0, write: 0, read: 0, output: 0 };
const models = new Set();
let first = Date.now();
for (const { t, model, u } of replies.values()) {
  sum.input += u.input_tokens ?? 0;
  sum.write += u.cache_creation_input_tokens ?? 0;
  sum.read += u.cache_read_input_tokens ?? 0;
  sum.output += u.output_tokens ?? 0;
  if (model && !model.startsWith('<')) models.add(model);
  first = Math.min(first, t);
}

const n = (x) => x.toLocaleString('en-US');
// Cache reads are the context re-read on every reply: cheap per token but huge, so they're kept apart.
const fresh = sum.input + sum.write + sum.output;
let name = os.hostname();
try { name = fs.readFileSync('C:/ProgramData/worker/name.txt', 'utf8').trim().split(' ')[0] || name; } catch { }
const lines = [
  `Worker: ${name}`,
  `Worker-Time: ${replies.size ? Math.max(1, Math.round((Date.now() - first) / 60000)) : 0} min`,
  `Worker-Tokens: ${n(fresh)} new + ${n(sum.read)} cache read (input ${n(sum.input)}, cache write ${n(sum.write)}, output ${n(sum.output)}; ${replies.size} replies; ${[...models].join(', ') || 'no model'})`,
];

const msg = fs.readFileSync(msgFile, 'utf8').replace(/\s+$/, '');
// Trailers go in the last paragraph, beside Co-Authored-By if it's there.
const lastPara = msg.split(/\n\s*\n/).pop();
const sep = /^[\w-]+: /m.test(lastPara) && msg.includes('\n\n') ? '\n' : '\n\n';
fs.writeFileSync(msgFile, msg + sep + lines.join('\n') + '\n');
fs.mkdirSync(path.dirname(stateFile), { recursive: true });
fs.writeFileSync(stateFile, String(Date.now()));
