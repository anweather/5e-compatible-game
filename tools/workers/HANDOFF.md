# Handoff: Workers session (Hyper-V worker VMs)

For the Project Manager session, to start the next Workers session.

## State (2026-10-28)

- Branch `workers-vms` (pushed, NOT merged) has everything in `tools/workers/`:
  scripts, README, REPORT. Lane: `tools/workers/**` only.
- Andrew ran `grant-hyperv-access.cmd`: he is in Hyper-V Administrators
  (checked with `Get-LocalGroupMember -SID S-1-5-32-578`).
- No VMs exist yet. The old session's token predates the group, so `Get-VM`
  was still denied there.

## Before the new session starts

1. Andrew signs out of Windows and back in (or restarts). This ends every
   Claude session and Unity Editor on the PC, so schedule it.
2. Optional but helps: merge `workers-vms` into `dev`, so the in-VM install
   URLs in README (`.../dev/tools/workers/...`) work as written. Otherwise the
   new session swaps `dev` for `workers-vms` in those URLs.

## Prompt for the new session

> You are the Workers session. Run `git fetch origin && git switch workers-vms`,
> then read tools/workers/VM_BRIEF.md, HANDOFF.md, README.md and REPORT.md.
> Andrew has signed out and in since joining Hyper-V Administrators, so
> `tools\workers\status.cmd` should now work without elevation. Continue:
> U1 (`create-ubuntu-template.cmd`) and W1 (`create-windows-template.cmd`),
> then hand Andrew the steps that need him (Ubuntu installer U2, UAC in the
> Windows VM W2, sign-ins U3/W3). Once both templates are provisioned and
> sealed, clone one worker from each, measure idle and busy RAM with
> `status.cmd`, fill in README's Memory table, and update REPORT.md (under
> 10 lines). Don't merge; the coordinator does.

## Notes for the new session

- Run scripts as `cmd /c .\name.cmd args` (bare `name.cmd` isn't found from
  this shell). `.ps1` files are blocked; the `.cmd` files carry PowerShell
  below a `#PS` line.
- J-017: `start.cmd` refuses to start a VM if less than 10 GB would stay
  free. W1's export copies the whole Quick Create disk (tens of GB) to
  `C:\HyperV\Workers` (C: has about 960 GB free).
- Untested so far: every Hyper-V step, both provision scripts, and Unity Hub
  headless on Linux (`xvfb-run -a unityhub -- --headless ...`).
- Unity CLI install command is unconfirmed; see docs.unity.com/en-us/unity-cli.
