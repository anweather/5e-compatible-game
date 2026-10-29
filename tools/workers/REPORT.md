# Workers report (for the coordinator)

- Works: `windows-template` (git, lfs, gh, claude, dotnet 8, node 22, Unity 6000.3.27f1 + Windows/Mac Mono; Unity, GitHub signed in; Claude signed out, see slots below), sealed. Clones sign in by themselves and start `claude remote-control` named after the VM (`w1` shows in ListAgents); start.cmd copies the name in, the task resets the template's RC registration. remove-worker.cmd and seal-template -Replace: no manual deletes.
- RAM (`w1`, 4 vCPU, 4-12 GB): idle 1.5-2.4 GB demand; `dotnet test core` 3.1 GB (386 pass); Unity batch import + compile 6.7 GB demand / 7.8 GB assigned.
- Unity batch needs Hub running for the licence (startup task now starts it; template needs that re-run). Project doesn't compile in workers yet: Asset Store packs missing (Andrew: Restore Asset Packs once in the template, re-seal).
- FOUND: clones can't share a `claude auth login` sign-in. Its refresh token rotates: the first VM to refresh (w1, 07:44) voided everyone else's copy, and the template wiped its tokens at 07:47 ("You must be logged in to use Remote Control"). Remote Control refuses `claude setup-token` tokens. Fix: signins.cmd keeps one sign-in per worker name (slots w1-w4) on the host, pushed in by start.cmd and saved back by stop/sleep/remove and a 5-minute host task; Andrew signs in once per slot. VERIFIED: remove-worker w1 + new-worker + start w1 came up signed in, RC "Connected" as w1, no clicks. Watch Unity Hub's sign-in for the same problem.
- Host reaches workers over PowerShell Direct now (account `User`, password set by rc-autostart-windows -Password).
- `ubuntu-template`: provisioned, gh signed in, shut down, not sealed. Blocked on Unity licence (Personal can't activate offline; needs Hub sign-in on the VM console).
- Fixes found by running: winget msstore cert, claude PATH, gh, headless Hub never exits, KVP daemon, workspace trust, spawn-mode prompt, task needs elevation.
- No GPU in VMs: probes, shots, clips, perf, MPPM stay on the host. J-017 guard works (refused at 17.9 GB free).
- Merge: branch `workers`, tools/workers/** only.
