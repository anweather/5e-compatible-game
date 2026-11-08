Needs: unity
Area: the pad's spell menu (radial and its UI under unity/Assets/Prototype/Scripts), on-screen help text, input
bindings for the pad; rules/ only if the hands' elements live there
Branch: pad-hands-menu

# One simple pad menu: two hands, an element each (Andrew, 2026-11-08; given to the Mac)

From `docs/playtests/2026-11-08-human-results.md`, M1 and M2. Controller first: design and judge it on the pad.

Andrew: the d-pad-down menu is "completely bonkers": it opens "a million different things", and its directions are
swapped (down moves left/right, left/right move up/down). **Oversimplify it:** d-pad down shows two hands; the left
stick picks the left hand's element, the right stick the right hand's. Release (or a button) confirms. Nothing else in
that menu. Whatever the old menu also did that is still needed goes somewhere else or goes away: list what you moved
or removed in the report.

Also **cut the text** on screen (M2): the game explains too much. In the menu, show the hands and the element icons
and colours, and at most one short word each. Go through the HUD and help prompts and cut what a player learns by
playing; keep anything they can't find out otherwise. List what you cut.

**No big element icons on screen (Andrew, 2026-11-08, after the brief):** the animated wizard hands already show each
hand's element, so the giant element icons that stay on screen all the time are "just weird". Take them off the HUD.
Show a hand's element on screen only while that hand's trigger is held (casting or charging), and keep even that small;
the hands themselves do the job. The new menu is the only other place icons appear.

Keep the Hold's casting (an element per hand, the direction modifier, charging) working with the new menu. Free roam
and the Hold use the same menu.

Before landing: merge origin/dev, `dotnet test core`, EditMode N/N in full, the cast and Hold probes, and a ≤30 s
720p clip with a poster (`pad-hands-menu.mp4`), driven by the pad: open the menu, pick an element per hand, cast.
Send it to Andrew with SendUserFile. Push the branch only, remove this claim on the branch in the last commit. The
lead lands it.
