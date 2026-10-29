# Workers report (for the coordinator)

- Works: Hyper-V access without elevation; `windows-template` provisioned (git, lfs, gh, claude, dotnet 8, node 22, Unity 6000.3.27f1 + Windows/Mac Mono, repo on dev; Unity, GitHub, Claude signed in) and sealed; worker `w1` cloned and started (J-017 guard refused it correctly at 17.9 GB free, started at 21 GB).
- RAM: `w1` idle 1.5 GB demand, trimmed to its 4 GB minimum. Busy not measured (no way into the guest yet; see next line).
- New: rc-autostart-windows.cmd / rc-autostart-ubuntu.sh start `claude remote-control` at boot, named after the VM, so workers are reachable without a sign-in. Untested.
- `ubuntu-template`: provisioned (same tools, gh signed in), shut down, NOT sealed. Blocked on the Unity licence: Personal can't activate offline; needs Hub sign-in on a VM console (startx) or batch-mode -username/-password.
- Fixes found by running: winget `--source winget` (msstore cert fails in the image), claude PATH, gh added, headless Hub never exits (script now stops it), Ubuntu KVP daemon package + enable.
- Waiting on Andrew: Ubuntu Unity sign-in; rc-autostart in each template (Windows needs the account password; then re-seal after removing templates\windows-template).
- Merge: branch `workers`, tools/workers/** only.
