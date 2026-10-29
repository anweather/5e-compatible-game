# Worker VMs (Hyper-V)

Agent work that needs its own Unity or a hard RAM cap runs in Hyper-V VMs on
the PC, started for a job and slept or stopped after. There are two
templates, cloned into workers:

| Template | For | vCPU | Memory (dynamic) | Disk |
|---|---|---|---|---|
| `ubuntu-template` (Ubuntu 24.04 LTS server) | Headless work: core and sandbox tests, compiling, batch builds of the Windows player (Mono) | 2 | 4 GB start, 2–8 GB | 128 GB, grows as used |
| `windows-template` (copy of Quick Create's "Windows 11 dev environment") | Windows-only work | 4 | 8 GB start, 4–12 GB | as Quick Create made it |

Each has git + git-lfs, the .NET 8 SDK, Node 22, Unity Hub and Editor
6000.3.27f1 (Windows Build Support, Mono; Mac Build Support, Mono if it
installed), Claude Code (not signed in), and the repo on `dev`. Files live
under `C:\HyperV\Workers` (the 980 PRO SSD); every script takes `-Root` to
change that. Networking is Hyper-V's `Default Switch` (NAT, outbound
internet, a new IP on each boot; `start.cmd` prints it).

**No GPU in the VMs.** Unity runs there with software rendering at best, so
probes (`tools/probe.sh`), screenshots and clips, perf numbers, MPPM and
anything that looks at the picture stay on the host. Batch-mode builds,
EditMode tests, `dotnet test core` and sandbox tests are fine.

The scripts are `.cmd` files with PowerShell below a `#PS` line (`.ps1` is
blocked on this PC). Run them from `cmd` or PowerShell as `.\name.cmd args`.

## One-time: Hyper-V access (Andrew)

1. Open an **elevated** prompt (UAC) and run `tools\workers\grant-hyperv-access.cmd`.
   It adds your account to *Hyper-V Administrators*, so every later script
   (and agent sessions) works without elevation.
2. Sign out and back in.
3. Check: `status.cmd` lists your VMs instead of a permission error.

## Ubuntu template

- **U1.** `create-ubuntu-template.cmd`: downloads the newest 24.04 server ISO
  from releases.ubuntu.com (checks its SHA256), makes the VM and opens its window.
- **U2. (Andrew)** In the VM window run the installer: defaults, user name
  `anweather`, a password of your choice, **tick "Install OpenSSH server"**, no
  snaps. Reboot when it says so (press Enter if it asks to remove the media).
  Then, in the VM (console or `ssh anweather@<ip>` from `start.cmd`'s output):

  ```
  curl -fsSL https://raw.githubusercontent.com/anweather/5e-compatible-game/dev/tools/workers/provision-ubuntu.sh | bash
  ```

  It asks for the sudo password once. About 30–60 min, mostly the Unity download.
  (Until this folder is merged to `dev`, swap `dev` in the URL for the branch it's on.)
- **U3. (Andrew)** Sign-ins, if workers will need them (or do them per worker
  instead, so the template carries no secrets):
  - Unity licence: `xvfb-run -a unityhub` has no screen on a server, so use
    `unity auth login` from the unity CLI (install per
    docs.unity.com/en-us/unity-cli; not tested on Linux here) or Unity's manual
    activation (`Unity -batchmode -createManualActivationFile`, then
    upload it at license.unity3d.com).
  - The Asset Store packs come with the Unity account (Prototype → Restore
    Asset Packs needs a sign-in; until then builds lack the packs).
  - GitHub push access (`gh auth login` or an SSH key) and `claude` sign-in.
- **U4.** Make clones get their own DHCP lease, then seal:

  ```
  sudo truncate -s 0 /etc/machine-id && sudo poweroff      # in the VM
  seal-template.cmd ubuntu-template                           # on the host
  ```

## Windows template

- **W1.** `create-windows-template.cmd`: exports "Windows 11 dev environment"
  (read only; the original is untouched), imports it as a copy called
  `windows-template`, sets 4 vCPU and 4–12 GB, and opens it.
- **W2. (Andrew)** Sign in to the VM (the dev environment's own account), open
  an **elevated** PowerShell (UAC) and run:

  ```
  curl.exe -LO https://raw.githubusercontent.com/anweather/5e-compatible-game/dev/tools/workers/provision-windows.cmd
  .\provision-windows.cmd
  ```

  winget fetches each vendor's installer; some may raise their own UAC prompt.
- **W3. (Andrew)** Unity Hub: sign in and activate the licence; install the
  unity CLI per docs.unity.com/en-us/unity-cli (`unity --help` to check);
  GitHub and `claude` sign-in as for Ubuntu. Windows activation: the dev
  environment image is an evaluation build with its own expiry; note the date
  on the desktop.
- **W4.** Shut the VM down from inside, then `seal-template.cmd windows-template`.

Clones of the Windows template share its computer name and SID (no sysprep).
That's fine on the NAT switch; rename one inside the guest
(`Rename-Computer`) if two must talk to each other.

## Workers

```
new-worker.cmd windows-template w1     # full copy, new id, same MAC per name; left off
start.cmd u1                           # refuses if <10 GB would stay free (J-017); prints the IP
sleep.cmd u1                           # saves state to disk: frees all its RAM, resumes in seconds
stop.cmd u1                            # clean guest shutdown
status.cmd                             # state, CPU, assigned and demanded RAM, host free RAM
```

Inside a worker, `git fetch && git switch <branch>` before work: the clone is
as old as the template. `remove-worker.cmd w1` deletes a worker (VM and disk;
it refuses templates).

### Refreshing the template (new asset packs)

Clones get the Asset Store packs, Unity's imported `Library/` and the built
yard from the template, so their first Unity job runs in about 2 minutes
instead of importing 3 GB. When an agent needs a new pack it adds it to
`unity/AssetPacks.json` on its branch and says so; it can't download one (that
needs the Unity account that owns it). Then:

1. Andrew, on the PC: Prototype > Restore Asset Packs in the main checkout.
2. `refresh-template.cmd` (stop running workers first for RAM): starts the
   template, updates its repo to `dev`, copies the packs in from the main
   checkout, builds the yard, runs EditMode tests (all must pass), checks git
   is clean, shuts down and re-seals.
3. `remove-worker` + `new-worker` + `start` for each worker: they come back
   signed in from their slots, on the new template.

Run it too when `dev` has moved a long way. The template also holds Andrew's
Google accounts, signed in in Edge, so any sign-in a worker needs is one
click; Google sessions don't rotate, so clones can share them.

## Remote Control at boot

So a started worker shows up in claude.ai/code (and `ListAgents`) with nobody
signed in to it, each template runs `claude remote-control` at boot, named
after the Hyper-V VM (`w1`, `u1`...; start.cmd copies the name in).
Set it up once per template, before sealing:

- **Sign-ins live in slots on the host, one per worker name.** Seal the
  template signed out of Claude, Unity Hub and GitHub. Claude's and Hub's
  refresh tokens are single-use, so clones sharing one void each other
  within minutes, and Remote Control refuses the long-lived
  `claude setup-token` token ("only available with claude.ai
  subscriptions"). So `signins.cmd` keeps each worker's sign-ins in
  `C:\HyperV\Workers\signins\<name>` (this account and SYSTEM only):
  - `claude`: `.claude\.credentials.json`.
  - `unity-hub`: Hub's tokens, in Windows Credential Manager, plus its
    `accounts.db`. Clones can read each other's Credential Manager entries
    because they share the account's encryption keys; **never reset
    `User`'s password** (`net user`): that orphans every entry.
  - `unity-licence`: the licence file. It's bound to the Windows product id,
    BIOS serial and MAC; clones share the first two, and `new-worker` gives
    each worker name the same MAC every time (`C:\HyperV\Workers\macs`).
  - `github`: `gh auth login --insecure-storage` (a file, not Credential
    Manager) and `.gitconfig` with `gh auth setup-git`. Its token doesn't
    rotate, so one sign-in can be copied into every slot.

  `start.cmd` copies the slot in before Remote Control starts (stopping Hub
  first); `stop`, `sleep` and `remove-worker` copy newer sign-ins back
  (Claude: the later-expiring token; the rest: the later file). Filling a
  slot, once per worker name: start the worker, sign in to Claude in the
  window it opens, to Unity Hub, and run
  `gh auth login --insecure-storage -h github.com -p https -w; gh auth setup-git`.
  After that, `remove-worker w1` + `new-worker` + `start w1` comes up with
  everything signed in and licensed, no clicks (verified on `w1`). Four slots
  (`w1`–`w4`) cover 2–3 running workers with headroom. Andrew, once, from an
  admin prompt: `signins.cmd install` registers a host task that saves
  running workers' sign-ins every 5 minutes, so a worker that crashes or is
  turned off hard doesn't leave its slot stale. An unused Claude slot lapses
  after about 4 weeks (its refresh token expires). Windows workers only
  (PowerShell Direct).
- **Windows:** in the VM, elevated PowerShell,
  `.\rc-autostart-windows.cmd -Password password` (or the host runs it over
  PowerShell Direct once the account has a password). It turns on automatic
  sign-in for `User` and starts Remote Control at that sign-in (a signed-out
  startup task would need a password). The password lets the host reach the
  guest over PowerShell Direct (`Invoke-Command -VMName w1 -Credential User`);
  fine on the NAT switch, not on a real network. Log: `%TEMP%\claude-rc.log`.
- **Ubuntu:** in the VM, `bash ~/5e-compatible-game/tools/workers/rc-autostart-ubuntu.sh`
  (sudo password once). It also turns on the KVP daemon. Log:
  `journalctl --user -u claude-rc`.

First, in each template, run `claude` once in `~/5e-compatible-game` and
accept the workspace trust dialog: Remote Control refuses an untrusted folder
("Error: Workspace not trusted"). Trust is per user, so clones inherit it.

## Memory

Measured with Hyper-V's memory demand (`status.cmd`, DemandGB).

| VM | Idle | Busy (what) |
|---|---|---|
| Ubuntu worker | – (template, no Unity licence yet: 0.8 GB used of 1.8 GB assigned) | – (dotnet test core / Unity batch build) |
| Windows worker `w1` | 1.5–2.4 GB demand; Hyper-V trims it to the 4 GB minimum | 3.1 GB: `dotnet test core` (386 pass, ~1 min). 6.7 GB demand, 7.8 GB assigned: Unity batch first import + compile without packs (~4 min). With the refreshed template (packs, Library, yard): Unity EditMode 441/441 pass in 2.1 min |

Unity batch mode needs Unity Hub running (it holds the signed-in licence);
the startup task starts it.
