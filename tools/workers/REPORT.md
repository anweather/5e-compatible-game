# Workers report (for the coordinator)

- Works: `windows-template` (git, lfs, gh, claude, dotnet 8, node 22, Unity 6000.3.27f1 + Windows/Mac Mono; Unity, GitHub, Claude signed in), sealed. Clones sign in by themselves and start `claude remote-control` named after the VM (`w1` shows in ListAgents); start.cmd copies the name in, the task resets the template's RC registration. remove-worker.cmd and seal-template -Replace: no manual deletes.
- RAM (`w1`, 4 vCPU, 4-12 GB): idle 1.5-2.4 GB demand; `dotnet test core` 3.1 GB (386 pass); Unity batch import + compile 6.7 GB demand / 7.8 GB assigned.
- Unity batch needs Hub running for the licence (startup task now starts it; template needs that re-run). Project doesn't compile in workers yet: Asset Store packs missing (Andrew: Restore Asset Packs once in the template, re-seal).
- OPEN: a fresh clone from the re-sealed template (remove-worker, new-worker, start: all worked) did not come online in 7 min, while the hand-updated `w1` did in ~75 s. Cause unknown (no read access into the guest); next: on the fresh `w1`, read %TEMP%\claude-rc.log, %LOCALAPPDATA%\worker\rc-name.txt and the task's last result.
- `ubuntu-template`: provisioned, gh signed in, shut down, not sealed. Blocked on Unity licence (Personal can't activate offline; needs Hub sign-in on the VM console).
- Fixes found by running: winget msstore cert, claude PATH, gh, headless Hub never exits, KVP daemon, workspace trust, spawn-mode prompt, task needs elevation.
- No GPU in VMs: probes, shots, clips, perf, MPPM stay on the host. J-017 guard works (refused at 17.9 GB free).
- Merge: branch `workers`, tools/workers/** only.
