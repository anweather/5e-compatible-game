// worker-settings.mjs: run in a worker VM by start.cmd (Windows) or linux.sh (Linux), at every
// start. It adds the Notification hook (attention.mjs) to the worker's Claude settings
// (~/.claude/settings.json), leaving everything else there as it is, so a permission prompt in
// the worker is reported to the host within a minute. It changes no permissions.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const file = path.join(os.homedir(), '.claude', 'settings.json');
let s = {};
try { s = JSON.parse(fs.readFileSync(file, 'utf8')); } catch { }

const hook = `node "${path.join(os.homedir(), '.worker', 'attention.mjs').replace(/\\/g, '/')}"`;
s.hooks ??= {};
const notes = (s.hooks.Notification ??= []);
if (!notes.some((g) => (g.hooks ?? []).some((h) => h.command === hook))) notes.push({ hooks: [{ type: 'command', command: hook }] });

fs.mkdirSync(path.dirname(file), { recursive: true });
fs.writeFileSync(file, JSON.stringify(s, null, 2) + '\n');
console.log(`Worker settings: the attention hook is in ${file}.`);
