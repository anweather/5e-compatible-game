# F-0007: The robe stretches into a long flat tail, and leaves the legs bare

- **Category:** Visual
- **Severity:** Medium (the wizard is always on screen). A known issue: the 0.1.8 release notes list it, and 0.1.7 did it too.
- **Build:** alpha 0.1.8 (4378764), the release candidate; runs rc018-812 and rc018-813, seeds 812-813 (2026-10-30)
- **Reproduced:** Seen in 2 of 3 runs. Not replayed.
- **GitHub:** [#4](https://github.com/anweather/5e-compatible-game/issues/4)

**Steps:** From the yard, cast three spells in place, then walk to the ball
court and on toward the waterfall (run 813). Or walk from the yard to the
village and sit on a bench, then stand (run 812).

**What happens:** In run 813 at frame 544, about 18 s into the run, the robe's
skirt trails behind the wizard as a long, flat black sheet, as far as the
wizard is tall. In run 812 at frame 1610, after the walk to the village and a
sit, the robe ends at the chest. The wizard's bare jointed legs show below it
(`bare-legs.jpg`). The release notes say "after a long walk". Run 813 shows it
after a short one.

**Replay:** `playtest run --replay playtest/runs/rc018-813/run.json --until-frame 560 --player %LOCALAPPDATA%/SpellChemistryValley/current`

![before](before.jpg)
![bare legs](bare-legs.jpg)

**Cause (2026-10-30):** `RobeCloth` stopped while not visible and resumed from where it was left; it now snaps to rest when seen again. Bot runs on a locked PC make this far worse: the robe was visible on 14 of 2880 frames, so the player was hardly rendering outside the bot's shots. See #4.
