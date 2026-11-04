Brief for the PC: one real mismatched two-peer run of maps phase 2 (`maps-identity`, `docs/design/maps-as-data.md` section 2). The Mac can't run this: it has no second graphics peer, and its Editor is stuck. All on one machine, over the same-network Host and Join, no sign-in.

**Build.** On branch `maps-identity`, a Windows player as usual: `Unity -batchmode -projectPath unity -executeMethod PrototypeBuild.BuildWindowsFromCommandLine`, which exits 0 and logs `HEADLESS BUILD: OK`. Copy the build folder three times: `A`, `B`, `C`. Below, `<data>` is `<Name>_Data/StreamingAssets/world/maps` in a copy. Each copy's log is its `Player.log`; read it after each step, as they share one path, or start each with `-logFile <own file>`.

**1. Same maps.** Start A and B with no arguments. A: Esc, Host on the same network. B: Join `127.0.0.1`. **Must show:** B joins, no desync line in either, a cast by one shows on the other. Leave.

**2. A different folder.** In B, add a space to the end of `<data>/valley/map.json`. A hosts, B joins. **Must show:** B goes back to solo with `Couldn't join: The host is on The Valley, and this copy has a different The Valley.`, A's log has `NET: turned client ... away:` with the same words, and A plays on. Then put B's file back.

**3. A map the peer doesn't have.** In A and C, copy `<data>/valley` to `<data>/test-isle` and in its `map.json` set `"id": "test-isle"` and `"name": "Test Isle"`. Start A with `-map test-isle` and host. B joins. **Must show:** `Couldn't join: The host is on Test Isle, and this copy doesn't have it.`

**4. The host's map reaches the joiner.** A still hosting on `test-isle`, start C with no arguments and join. **Must show:** C joins, C's log has `NET: on the host's map, test-isle`, no desync after a cast each. Then C leaves: C is back in solo, and a second join from C works. Test Isle's land is the valley's, so this shows the id and hash travel, not a second land: the terrain is still the scene's until phases 5 and 6.

**Report:** what each step showed, the exact refusal texts, and any desync. Not this run's job: a map in the menu, drawn land that differs.
