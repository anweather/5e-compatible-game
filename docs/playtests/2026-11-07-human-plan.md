# Human playtest plan, 2026-11-07

Build: `Builds/PcDev` from dev with `castle-seat` and `hold-progress` landed. Reply per step with `V1 PASS`, `V1 FAIL: what you saw`, or `SKIP`.

## Proximity voice (Vivox)

Before this, canned audio has already shown the fade with distance (`docs/handoffs/briefs/vivox-canned.md`). You judge how it sounds.

Voice is only in the spike build, `Builds/PcVoice` (branch `vivox-spike`, next to `PcDev` in the `host-ux` worktree); `PcDev` has no voice. From the `host-ux` folder, in PowerShell:

```
powershell -ExecutionPolicy Bypass -File tools\vivox\voice-in-the-woods.ps1
```

It starts the voice in the woods (a small minimised window: a recorded voice on a 20 s loop that plays nothing itself), then opens the game 20 s later with `-vivox-walker`, so you hear the channel where your wizard stands, turned the way the camera looks. Closing the game closes the voice. The voice stands at (75, −20), in the trees 60 m east-south-east of the yard spawn (19.5, 3): from the start, walk ahead and to the right. Vivox's settings: full volume within 4 m, fading to silence at 32 m. Logs land in `%TEMP%\voice-in-the-woods` (lines starting `VIVOX:`; the game's says every 5 s how far you are and whether the voice is listed).

| Id | Do this | Tell me |
|---|---|---|
| V1 | Start the voice in the woods, then the game. Walk from the yard toward it | Where you first hear it, and whether it grows smoothly as you get close. Does it sit in the right direction when you turn? |
| V2 | Walk away again, past where you first heard it | It fades and goes quiet, without clicks or a sudden cut |
| V3 | Near the voice, hold H and talk to Claude | Claude hears you, the voice keeps playing, nothing freezes |

## The Hold (verdicts the Mac waits on before tuning)

| Id | Do this | Tell me |
|---|---|---|
| T1 | Cinders: water hand, then fire hand | Does it punish the wrong hand enough? |
| T2 | Roots: earth, then air | Same |
| T3 | The first boss | Fair? Fun? |
| T4 | A full run from fresh (F7) | How long, did a first run feel right, is 1.8 hours to unlock everything too short a road? |
| T5 | Look while playing | Element size in hand, barricade fire, pale flyers, the stalker's body, the grainy patch at the gate, climbing raiders. Burning ground hitting flyers: fair? |
