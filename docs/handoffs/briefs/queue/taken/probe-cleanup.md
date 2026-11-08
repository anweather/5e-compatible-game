Needs: unity
Area: PauseMenu.cs and the menu probe, Shapeshift (the change-back key), ProbeScenarios.cs (combo, aimlooks),
SpellCaster.LockOn.cs; no rules changes
Branch: probe-cleanup

# Fix what fails on plain dev (the lead, 2026-11-08 overnight; given to the PC)

These fail on dev itself, so every worker's report carries them as "known":

1. **`menu` probe, the pause menu's IMGUI error** "Getting control 11's position in a group with only 11 controls when
   doing repaint" (`PauseMenu.cs:459`, `PauseMenu.Button` from `DrawPage`). It also showed 6 times in Andrew's
   2026-11-08 Player.log. A control drawn in Layout but not Repaint (or the reverse): make both passes draw the same.
2. **`shapeshift` probe, `key_ends_shape`:** the change-back key after Z doesn't end the shape. pad-hands-menu removed
   the change-back key's on-screen text, but the key itself must still work. Find out whether the key broke or the
   probe's expectation is stale, and fix the right one.
3. **`combo` probe:** 6 failures, stale since the hands menu (`dpad_down_opens_grimoire` expects the old page) and the
   casting changes (element loading, walking speed). Rewrite it to today's casting and menu, or retire checks covered
   by `handsmenu`, `hands` and `castfeel`. Say which.
4. **#166 `aimlooks`:** no aim marker for a selected spell (`SpellCaster.LockOn.cs:70`), 9 of 65 failing. Fix and
   close the issue with a comment.

Before landing: `dotnet test core`, EditMode N/N in full, the four probes passing, and the other probes these touch
(`handsmenu`, `hands`, `castfeel`, `aimed`). No clip is needed. Push the branch only, remove this claim on the branch
in the last commit. The lead lands it.
