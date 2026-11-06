# Brief: Hyper-V worker VMs (from the coordinator, approved by Andrew)

You're the Workers session, on Andrew's second (Pro) Claude account, with small
usage limits: plan before you run anything, and keep reports short. The repo is
C:\Users\Andrew\Development\5e-compatible-game. Read COORDINATION.md (rules,
lanes, J-017 RAM rules) and JUDGMENT_CALLS.md first. Your lane is
tools/workers/** only. Since D15, workers land their own branches (below);
a change to landing itself still goes to the PM, which lands it.

## Why
On the PC (Windows 11 Pro, 31 GB RAM, one GPU), two Unity Editors starve each
other, and Editors steal window focus. Agent work should run in Hyper-V VMs
with hard memory and CPU limits, started for a job and slept or stopped after.

Hyper-V is already on, and Andrew has made a VM with Quick Create named
"Windows 11 dev environment". Start the Windows template from that VM rather
than a fresh install.

## Goal: two templates
1. **Ubuntu 24.04 LTS** (for headless work: core and sandbox tests, compiling,
   batch builds of the Windows player; the project uses Mono, so Linux can
   build it).
2. **Windows 11** (for Windows-only work, matching the real platform).

Each template has:
- git and the repo cloned (github.com/anweather/5e-compatible-game, branch dev);
- dotnet SDK (the version core/ needs) and Node (for sandbox/);
- Unity Hub and Unity Editor 6000.3.27f1, with Windows Build Support (Mono),
  plus Mac Build Support (Mono) if it installs cleanly;
- the unity CLI, as in the repo's docs;
- Claude Code (the official installer), not signed in;
- a checkpoint or exported image named `template`, so a worker can be cloned
  from it.

Suggested sizes: Ubuntu 2 vCPU, 4–8 GB dynamic memory; Windows 4 vCPU, 8–12 GB
dynamic memory. No GPU is expected; note what that rules out (probes, shots,
performance runs stay on the host).

## Approved
- Installing from official sources only: ubuntu.com, microsoft.com, unity.com,
  claude.ai or Anthropic's installer, dotnet.microsoft.com, nodejs.org,
  git-scm.com.
- Creating VMs, virtual switches and checkpoints in Hyper-V.

## Andrew's steps (stop and list them; never do them yourself)
- Anything needing a password, licence, key or sign-in: Unity sign-in and
  licence activation in each VM, GitHub auth inside the VMs, Claude sign-in
  inside the VMs, Windows activation, and admin prompts (UAC).
- Turning on Hyper-V, if it isn't on yet (it needs a restart).
- Anything that deletes or overwrites what's already on the host.

## Notes
- PowerShell .ps1 files are blocked by policy on this PC. Use .cmd files that
  call `powershell -NoProfile -Command`, or bash with inline PowerShell (see
  tools/coordinator/sample.sh). Hyper-V cmdlets need an elevated shell; if
  yours isn't, write the commands down for Andrew to run.
- Never use --force past the preflight. Don't touch other lanes' files.

## Deliver
- tools/workers/README.md: what was built, every manual step, and how to clone
  a worker from each template and start, sleep and stop it.
- tools/workers/*.cmd scripts for create, start, sleep and stop.
- A report to the coordinator in under 10 lines: what works, what's waiting on
  Andrew, and the RAM each VM uses idle and busy.

## Landing your work (D15, every worker)
1. Work on the brief's branch off `dev`. If the brief came from the queue
   (`docs/handoffs/briefs/queue/taken/<f>`), `git rm` that file on your
   branch: the claim leaves with your landing, and `land.sh` refuses a branch
   that leaves it behind.
2. Run the gates: `bash tools/workers/gates.sh <log>`. All must pass.
3. Land: `bash tools/workers/land.sh here < message` (a line or two: what
   landed). Add `--compiled` only if this machine has Unity and you compiled
   the Unity C# in it; without Unity, a Unity C# branch stops and goes to the
   PC through the mailbox. `--deletes` only for deletions you meant and read.
4. Report what `land.sh` said. `STOP: ...` is a refusal, not a success: say
   so first in your report, with the reason (`land.sh` has already sent it to
   the PM). Don't work around a refusal (no `--deletes` you didn't read, no
   `--compiled` you didn't do); fix the cause or leave it for the PM.
