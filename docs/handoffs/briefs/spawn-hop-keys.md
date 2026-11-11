Needs: mac
Area: unity/Assets/Prototype/Scripts/PlaytestTools.cs (and its test)
Branch: spawn-hop-keys
After: playtest-tools has landed (this builds on it; branch off origin/dev after that).

# N and Shift+N: hop between the map's spawn points (#220)

Andrew wants to move around the nine quickly. Spawn points already exist: one in every world square of the nine
(`GeneratedMap.SpawnPlaces()`, kind "spawn", `play-nine` 487f537e), read by `SpawnPoints` (the host's "Start in:"
button in `Net/NetSession.cs`:413 picks the starting one). What is missing is a key in play.

Do:
1. In `PlaytestTools` (F4 fly, F6 where, F8 marks live there): **N** teleports the wizard to the next spawn point in
   `SpawnPoints`' order, **Shift+N** to the previous one (wrapping). Land standing on the ground at the spawn's
   cell, facing as a fresh start does. Show the spawn's name in the HUD for a moment.
2. Offline only, like fly: refuse online (a peer's place is shared state, D4) with a short HUD line. A hop is a
   view-side move of the wizard, not a core command; check how fly moves the wizard and do the same.
3. Make sure the destination chunks are active and drawn before the wizard stands there (the reach follows the
   wizard; a hop of hundreds of metres must not drop him into an undrawn chunk or under the ground). Reuse what a
   fresh start does.
4. A map with one spawn: N does nothing but say so.
5. An EditMode test in PlaytestToolsTests (the hop order and wrap, from a list of spawn points). One line in the
   play log when a hop happens (position before and after), so a lag report can tell.

Don't: change shared state, the core, or the spawn points themselves. Not for the PC's playtest-looks files.
Done when: EditMode passes on the Mac, a probe or player run on the nine hops through every spawn and reports where
it stood (no fall, no void). Self-land: `land.sh auto spawn-hop-keys --compiled`. Report the commit and the nine's
spawn names in order.
