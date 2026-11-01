# Sound credits

Every clip in this folder is CC0 (public domain:
<https://creativecommons.org/publicdomain/zero/1.0/>), so it can live in this
public repository and ship in the game with no attribution required. We credit
the authors anyway. Recordings stored with Git LFS (`.gitattributes`).

Only CC0 audio goes in git. Anything under another licence (the Asset Store
packs) is restored with Prototype > Restore Asset Packs and never committed.

| Folder | Author | Source | Used for |
|---|---|---|---|
| `rubberduck/` | rubberduck | [25 CC0 bang / firework SFX](https://opengameart.org/content/25-cc0-bang-firework-sfx) (`bang_*`, `cannon_*`); [100 CC0 SFX](https://opengameart.org/content/100-cc0-sfx) (`explosion`, `splash_*`, `plop_*`, `wooden_*`) | explosions, Thunderwave, Create Water, breaking crates; water gathering (backwards), a burst's release |
| `kenney/` | Kenney (www.kenney.nl) | [Impact Sounds 1.0](https://kenney.nl/assets/impact-sounds) (`impactWood_heavy_*`) | breaking crates, the slam's thump |
| `bart/` | bart | [Ice spells](https://opengameart.org/content/ice-spells) (`ice`, `coldsnap`; made from a public-domain pdsounds.org recording) | Ray of Frost |

`cast-deep-hum-pulse/` and `arcane-whoosh/` are ours: a cast's hums, slam and whoosh, and the
landing whoosh, rendered for this game from the presets of those names by the sound-design skill
(`.claude/skills/sound-design/`, `render_game.py`), with no outside source.

Stand-ins for a machine with no bank, and a cancel's fizzle, are made in code for this game:
`ProceduralCast.cs`, `ProceduralFire.cs`. Which sound comes from where is `CastSoundTable.cs`.

Downloaded 2026-10-25. Files are as published, unchanged; loudness is evened
out in the game (`SoundBank`), not by editing the files.
