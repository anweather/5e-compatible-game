// attention.mjs: Claude Code's Notification hook in a worker VM. When the worker's Claude stops
// to ask for permission, it writes ~/.worker/attention.json ({time, message, cwd, session}), which
// the host's bridge-watch.sh reads every 30 s and mails to the PM, so a worker blocked on a
// prompt nobody is watching is seen in a minute, not hours. Idle notices ("waiting for your
// input") are ignored: a worker idles after every task. Installed by worker-settings.mjs.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

let input = {};
try { input = JSON.parse(fs.readFileSync(0, 'utf8')); } catch { }
const message = String(input.message ?? '');
const kind = String(input.notification_type ?? '');
if (kind ? kind === 'permission_prompt' : /permission/i.test(message)) {
  const dir = path.join(os.homedir(), '.worker');
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'attention.json'), JSON.stringify({
    time: Math.floor(Date.now() / 1000), message, cwd: input.cwd ?? '', session: input.session_id ?? '',
  }) + '\n');
}
