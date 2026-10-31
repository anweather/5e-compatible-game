#!/usr/bin/env node
// runner.mjs: the worker's side of the mailbox, inside the VM. The host drops briefs into
// ~/.worker/inbox (start.cmd <vm> <brief>, brief.cmd <vm> <file>); this runs each one as a headless
// Claude session (claude -p) in the repo, one at a time, and writes what it said into
// ~/.worker/outbox, which the host's bridge-watch.sh collects into the PM's mailbox. No session on
// the host sits in between (Andrew, 2026-10-30).
//   inbox/<id>.md               a brief: a fresh session
//   inbox/<id>.followup-<n>.md  a follow-up: resumes <id>'s session, so it keeps that context
// A usage limit ends the run with the limit's text in the report; the brief is not retried.
// Started at logon (Windows: the "Worker Runner" task; Linux: start.cmd over SSH). One copy at a time.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import crypto from 'node:crypto';

const home = os.homedir();
const base = path.join(home, '.worker');
const [inbox, taken, outbox] = ['inbox', 'taken', 'outbox'].map(d => path.join(base, d));
for (const d of [inbox, taken, outbox]) fs.mkdirSync(d, { recursive: true });
const stateFile = path.join(base, 'runner.json');
const lock = path.join(base, 'runner.pid');
const mode = process.env.WORKER_PERMISSION_MODE || 'auto';
const repo = path.join(home, '5e-compatible-game');
const claude = [path.join(home, '.local', 'bin', process.platform === 'win32' ? 'claude.exe' : 'claude')].find(fs.existsSync) || 'claude';
const name = (() => { // start.cmd writes "<name> <time>" (Windows path first, then Linux)
  for (const f of ['C:/ProgramData/worker/name.txt', '/var/lib/worker/name.txt']) { try { return fs.readFileSync(f, 'utf8').trim().split(' ')[0]; } catch { } }
  return os.hostname();
})();
const log = m => fs.appendFileSync(path.join(base, 'runner.log'), `${new Date().toISOString()} ${m}\n`);

try { const old = Number(fs.readFileSync(lock, 'utf8')); if (old && old !== process.pid) { process.kill(old, 0); console.log(`runner already running (pid ${old})`); process.exit(0); } } catch { }
fs.writeFileSync(lock, String(process.pid));
let state = {}; try { state = JSON.parse(fs.readFileSync(stateFile, 'utf8')); } catch { }
state.sessions ??= {};

function run(args) {
  return new Promise(resolve => {
    const p = spawn(claude, args, { cwd: fs.existsSync(repo) ? repo : home, env: process.env, windowsHide: true, detached: process.platform !== 'win32' });
    let out = '', err = '', ended = false;
    p.stdout.on('data', d => out += d); p.stderr.on('data', d => err += d);
    // A headless run waits for its background tasks (a Monitor's tail -f, a dev server) before it
    // exits, so a finished brief can hang for ever. Once its final answer is in the transcript and
    // nothing has been written for SETTLE minutes, end the run, children and all (2026-10-30, w1).
    const settle = Number(process.env.WORKER_SETTLE_MIN || 3) * 60000;
    const watch = setInterval(() => {
      const t = transcript(args.sid); if (!t) return;
      const idle = Date.now() - fs.statSync(t).mtimeMs;
      if (idle < settle || !finalAnswer(t)) return;
      ended = true; clearInterval(watch);
      log(`settled: answered and idle ${Math.round(idle / 60000)} min, ending the run`);
      if (process.platform === 'win32') spawn('taskkill', ['/PID', String(p.pid), '/T', '/F'], { windowsHide: true });
      else try { process.kill(-p.pid, 'SIGTERM'); } catch { }
    }, 30000);
    p.on('close', code => { clearInterval(watch); resolve({ code, out, err, ended }); });
    p.on('error', e => { clearInterval(watch); resolve({ code: -1, out, err: String(e), ended }); });
  });
}
// The run's transcript (--session-id names it), and whether its last word is a final answer.
function transcript(sid) {
  const root = path.join(home, '.claude', 'projects');
  try { for (const d of fs.readdirSync(root)) { const f = path.join(root, d, `${sid}.jsonl`); if (fs.existsSync(f)) return f; } } catch { }
  return null;
}
function lastAssistant(t) {
  const lines = fs.readFileSync(t, 'utf8').trim().split('\n');
  for (let i = lines.length - 1; i >= 0; i--) {
    let o; try { o = JSON.parse(lines[i]); } catch { continue; }
    if (o.type === 'assistant' && Array.isArray(o.message?.content)) return o.message;
    if (o.type === 'user' && !o.isMeta) return null; // a tool result or prompt after it: not finished
  }
  return null;
}
const finalAnswer = t => { const m = lastAssistant(t); return m && m.stop_reason === 'end_turn' && m.content.some(c => c.type === 'text' && c.text.trim()); };
// Usage summed from the transcript, for a run that was ended (no JSON result then).
function usageOf(t) {
  const u = { input_tokens: 0, cache_creation_input_tokens: 0, cache_read_input_tokens: 0, output_tokens: 0 }, seen = new Set();
  for (const l of fs.readFileSync(t, 'utf8').split('\n')) {
    let o; try { o = JSON.parse(l); } catch { continue; }
    if (o.type !== 'assistant' || !o.message?.usage || seen.has(o.message.id)) continue;
    seen.add(o.message.id); for (const k in u) u[k] += o.message.usage[k] ?? 0;
  }
  return u;
}

async function take(file) {
  const id = path.basename(file, '.md');
  const follow = id.match(/^(.+)\.followup-\d+$/);
  const text = fs.readFileSync(file, 'utf8');
  fs.renameSync(file, path.join(taken, path.basename(file)));
  const args = ['-p', text, '--permission-mode', mode, '--output-format', 'json'];
  const resumed = follow && state.sessions[follow[1]];
  // A follow-up continues its brief's transcript; a brief gets a new one under a name we know.
  args.sid = resumed || crypto.randomUUID();
  args.push(...(resumed ? ['--resume', resumed] : ['--session-id', args.sid]));
  state.sessions[follow ? follow[1] : id] = args.sid;
  fs.writeFileSync(stateFile, JSON.stringify(state, null, 1));
  log(`start ${id}${resumed ? ` (resuming ${resumed})` : ` (session ${args.sid})`}`);
  const t0 = Date.now();
  const { code, out, err, ended } = await run(args);
  let r = {}; try { r = JSON.parse(out.trim().split('\n').pop()); } catch { }
  const t = transcript(args.sid);
  if (ended && t) { const m = lastAssistant(t); r = { result: m?.content.filter(c => c.type === 'text').map(c => c.text).join('\n'), session_id: args.sid, num_turns: '?' }; }
  if (t) r.usage = usageOf(t); // the JSON result's usage is the last turn's only: sum the whole run
  // A background task finishing after the answer wakes the session for a short last word ("the
  // report stands"): keep the real report, the longest of the last three answers, and the last word.
  if (t) {
    const answers = fs.readFileSync(t, 'utf8').split('\n').map(l => { try { return JSON.parse(l); } catch { return null; } })
      .filter(o => o?.type === 'assistant' && o.message?.stop_reason === 'end_turn')
      .map(o => o.message.content.filter(c => c.type === 'text').map(c => c.text).join('\n').trim()).filter(Boolean);
    const best = answers.slice(-3).reduce((a, b) => (b.length > a.length ? b : a), '');
    const lastWord = answers.at(-1) ?? '';
    if (best && best !== lastWord) r.result = `${best}\n\n(Last word: ${lastWord})`;
  }
  const said = r.result ?? (out || err || '(no output)');
  const limit = /hit your (\w+ )?limit/i.test(said + err) ? '\nLimit: this account hit its usage limit; the brief stopped there.' : '';
  const u = r.usage ?? {};
  const report = [`# ${name} reports: ${id}`, '', said.trim(), '', '---',
    `Exit ${ended ? 'ended by the runner (answered, then idle: a background task kept it open)' : code}${r.is_error ? ' (error)' : ''}; ${Math.round((Date.now() - t0) / 60000)} min; ${r.num_turns ?? '?'} turns; session ${r.session_id ?? '?'}.`,
    `Tokens: input ${u.input_tokens ?? '?'}, cache write ${u.cache_creation_input_tokens ?? '?'}, cache read ${u.cache_read_input_tokens ?? '?'}, output ${u.output_tokens ?? '?'}.${limit}`].join('\n');
  // Written whole, then renamed, so the host never reads half a report.
  const tmp = path.join(outbox, `.${id}.tmp`);
  fs.writeFileSync(tmp, report + '\n');
  fs.renameSync(tmp, path.join(outbox, `${id}.md`));
  log(`done ${id}: exit ${code}${limit ? ', usage limit' : ''}`);
}

log(`started (pid ${process.pid}, mode ${mode})`);
for (;;) {
  // A file still being copied in (Copy-VMFile, scp) is left until it's been still for 3 s.
  const next = fs.readdirSync(inbox).filter(f => f.endsWith('.md') && Date.now() - fs.statSync(path.join(inbox, f)).mtimeMs > 3000).sort()[0];
  if (next) { try { await take(path.join(inbox, next)); } catch (e) { log(`failed ${next}: ${e.stack ?? e}`); } }
  else await new Promise(r => setTimeout(r, 10000));
}
