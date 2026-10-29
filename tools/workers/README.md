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
  `worker`, a password of your choice, **tick "Install OpenSSH server"**, no
  snaps. Reboot when it says so (press Enter if it asks to remove the media).
  Then, in the VM (console or `ssh worker@<ip>` from `start.cmd`'s output):

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
new-worker.cmd ubuntu-template  u1     # full copy, new id and MAC; left off
start.cmd u1                           # refuses if <10 GB would stay free (J-017); prints the IP
sleep.cmd u1                           # saves state to disk: frees all its RAM, resumes in seconds
stop.cmd u1                            # clean guest shutdown
status.cmd                             # state, CPU, assigned and demanded RAM, host free RAM
```

Inside a worker, `git fetch && git switch <branch>` before work: the clone is
as old as the template. Delete a worker in Hyper-V Manager (Andrew: it deletes
files on the host). To refresh a template: start it, update, shut down, and
seal again after Andrew removes `C:\HyperV\Workers\templates\<name>`.

## Memory

Measured with `status.cmd` (DemandGB). Not yet measured; fill in once the
templates exist.

| VM | Idle | Busy (what) |
|---|---|---|
| Ubuntu worker | – | – (dotnet test core / Unity batch build) |
| Windows worker | – | – (Unity batch build) |
