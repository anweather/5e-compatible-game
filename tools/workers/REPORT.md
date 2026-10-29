# Workers report (for the coordinator)

- Built: scripts and README in tools/workers/ (branch `workers-vms`, pushed, not merged): grant access, create Ubuntu and Windows templates, provision each (in-VM), seal (checkpoint + export named `template`), new-worker, start/sleep/stop/status. All parse-checked; wrapper and access check tested; ISO, Node and winget lookups resolve.
- NOT built: no VM exists yet. This session isn't elevated or in Hyper-V Administrators, so Get-VM is denied.
- Waiting on Andrew: (1) run grant-hyperv-access.cmd elevated once, sign out/in; (2) Ubuntu installer in the VM window (user, password, OpenSSH); (3) UAC in the Windows VM for provision-windows.cmd; (4) Unity licence, GitHub, Claude sign-ins per VM; (5) unity CLI install per Unity's docs.
- 2026-10-28: step 1 ran: Andrew is now in Hyper-V Administrators, but sessions started before a sign-out/in still lack it in their token (Get-VM still denied here).
- After (1) plus a sign-out/in, any session can run U1/W1/seal/new-worker unattended.
- RAM idle/busy: not measured (no VMs). Caps set: Ubuntu 2-8 GB, Windows 4-12 GB; start.cmd refuses to start if <10 GB would stay free (J-017).
- No GPU in VMs: probes, shots, clips, perf, MPPM stay on the host.
- Merge: tools/workers/** only; please merge `workers-vms` into dev so the raw.githubusercontent URLs in README work.
