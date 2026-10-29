# Workers report (for the coordinator)

- 2026-10-28: Hyper-V access works without elevation since Andrew's restart (`status.cmd` lists VMs).
- U1 done: `ubuntu-template` made from ubuntu-24.04.5-live-server (SHA256 checked), booted into the installer.
- W1 done: `windows-template` exported/imported from "Windows 11 dev environment" (original untouched, still Off), 4 vCPU, 4-12 GB, running.
- W2 done (after fixes: winget `--source winget`, msstore cert fails in the image; claude PATH added by hand): git 2.56, lfs 3.8.1, claude 2.1.312, dotnet 8.0.427, node 22.23, Unity 6000.3.27f1 + Windows and Mac Mono, repo on dev. Claude signed in.
- Waiting on Andrew: U2 (Ubuntu installer: user `worker`, tick OpenSSH, then the curl provision line), W2 (sign in, elevated PowerShell, provision-windows.cmd), U3/W3 (Unity licence, GitHub, Claude sign-ins). Install URLs on `dev` now work (merged).
- Then: U4/W4 seal, clone u1/w1, measure RAM. Not measured yet; at first boot Windows demanded 1.4 GB.
- No GPU in VMs: probes, shots, clips, perf, MPPM stay on the host.
