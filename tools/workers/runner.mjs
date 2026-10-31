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
    const p = spawn(claude, args, { cwd: fs.existsSync(repo) ? repo : home, env: process.env, windowsHide: true });
    let out = '', err = '';
    p.stdout.on('data', d => out += d); p.stderr.on('data', d => err += d);
    p.on('close', code => resolve({ code, out, err }));
    p.on('error', e => resolve({ code: -1, out, err: String(e) }));
  });
}

async function take(file) {
  const id = path.basename(file, '.md');
  const follow = id.match(/^(.+)\.followup-\d+$/);
  const text = fs.readFileSync(file, 'utf8');
  fs.renameSync(file, path.join(taken, path.basename(file)));
  const args = ['-p', text, '--permission-mode', mode, '--output-format', 'json'];
  const resumed = follow && state.sessions[follow[1]];
  if (resumed) args.push('--resume', resumed);
  log(`start ${id}${resumed ? ` (resuming ${resumed})` : ''}`);
  const t0 = Date.now();
  const { code, out, err } = await run(args);
  let r = {}; try { r = JSON.parse(out.trim().split('\n').pop()); } catch { }
  if (r.session_id) state.sessions[follow ? follow[1] : id] = r.session_id;
  fs.writeFileSync(stateFile, JSON.stringify(state, null, 1));
  const said = r.result ?? (out || err || '(no output)');
  const limit = /hit your (\w+ )?limit/i.test(said + err) ? '\nLimit: this account hit its usage limit; the brief stopped there.' : '';
  const u = r.usage ?? {};
  const report = [`# ${name} reports: ${id}`, '', said.trim(), '', '---',
    `Exit ${code}${r.is_error ? ' (error)' : ''}; ${Math.round((Date.now() - t0) / 60000)} min; ${r.num_turns ?? '?'} turns; session ${r.session_id ?? '?'}.`,
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
