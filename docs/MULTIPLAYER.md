# Multiplayer and voice: inviting a friend into the sandbox

This doc covers how a friend joins your Chemistry Yard over the internet,
how you talk to each other (including proximity voice), and how spells
can change that voice. It was researched on 2026-10-24 for Unity 6.3 LTS
(6000.3.27f1, URP). Prices and versions come from the linked pages on
that date. **[V]** means checked on an official page, **[S]** a
secondary source, and **[U]** unverified, so test it in a spike.

Scope: co-op sandbox, PC first, 2–8 friends, not competitive. That
scope drives every choice below.

## Recommendations

| Question | Recommendation |
|---|---|
| Netcode | **Netcode for GameObjects (NGO) 2.x**, client-hosted (one player is the host), on Unity Transport |
| Grid chemistry | **Lockstep of intents**, the sandbox's design (`LIVE_LAB.md`) carried over NGO messages. Cells are never streamed |
| Player movement | **Owner-authoritative** `NetworkTransform` and `NetworkAnimator`, interpolated on other screens |
| PhysX props and items | **Host-authoritative** and synced as transforms. If one affects chemistry, the host quantises the result into an intent (D4). Built: §7 |
| Connectivity | **Unity Multiplayer Services sessions**: Relay plus a join code, signed in anonymously. $0 at friends scale |
| Voice | **Vivox** (`com.unity.services.vivox`). Free up to 5,000 peak concurrent users |
| Proximity | Start with Vivox's **positional channel**. For spells, move to **participant taps played through Unity `AudioSource`s**, so the game owns falloff and effects |
| Spell voice | Spell state lives in the shared deterministic world. **Each listener applies the effect locally** (gain, range, mute, filters, where the voice comes from). Private spells (Message, Sending, Telepathic Bond) use **extra Vivox channels** |
| Order | 1) Friend joins my yard. 2) Voice. 3) Spell voice. About 2 + 1 + 2 weeks of evenings |

## 1. Netcode architecture

### Three kinds of state, three kinds of authority

| State | Who decides | How it travels | Why |
|---|---|---|---|
| **Grid chemistry** (`Chem.World`: cells, RNG, tick, wind) | Nobody: every machine computes it | Ordered cast intents in tick batches, plus checksums and a snapshot on join | Already deterministic and golden-tested against the TS sandbox. A batch is bytes; the grid is kilobytes per tick |
| **Player movement and animation** | The owning player | `NetworkTransform` and `NetworkAnimator`, owner authority, interpolated | Co-op. Nobody gains from cheating, and your own wizard feels instant |
| **PhysX props and carried items** (§7) | The host | Host-simulated, synced as transforms; who holds an item is host-granted | PhysX isn't deterministic across machines (`RESEARCH.md` §9). D4: physics never becomes shared truth. If a barrel landing matters to chemistry, the host sends "barrel at cell (4,7)" as an intent |

This is D4 applied to a real-time co-op game. It also amends one line
in `LIVE_LAB.md` ("for real-time 3D it sends authoritative state"). That
line still holds for PhysX, but the grid can stay lockstep. Players
are not grid entities in Unity, so moving them never touches the grid.

### Which netcode library

| Option | Fit for this project | Cost | Verdict |
|---|---|---|---|
| **Netcode for GameObjects 2.x** | First-party. Works with GameObjects, the Starter Assets controller, UGS Relay and sessions, and Vivox. Has owner-authoritative transforms. No prediction, which co-op doesn't need. Unity 6.3+ supports only NGO 2.x [V] <https://discussions.unity.com/t/netcode-for-gameobjects-1-x-deprecation-in-6000-3-editor/1674997>. The registry shows 2.11.2, with Unity Transport 2.6.0 [V] <https://packages.unity.com/com.unity.netcode.gameobjects> | Free | **Pick this** |
| Netcode for Entities | Prediction and rollback for competitive play. DOTS/ECS only, so the scene and controller would have to be rewritten | Free | Overkill. D4's reopen condition doesn't apply |
| Mirror / FishNet | Mature and free. Similar model to NGO. You'd wire Relay, Lobby and voice yourself, with fewer first-party tutorials | Free | Fine, but more glue and a less learnable path in Unity's own docs |
| Photon Fusion 2 / PUN 2 | Excellent prediction, hosted rooms. The 100 CCU plan is free for commercial use; 20 CCU is dev-only [S] <https://blog.photonengine.com/?p=5466> | Free, then paid | A second vendor and account for features we don't need |
| Steamworks P2P | Free relays (Steam Datagram Relay), invites through the Steam overlay. Needs an app ID: $100 Steam Direct fee per product, recoupable [V] <https://partner.steamgames.com/doc/gettingstarted/appfee>. Every friend needs Steam | $100 | **Later**, as a transport swap under NGO when shipping on Steam |
| Epic Online Services | Free, including P2P with NAT relay and voice [V] <https://www.epicgames.com/site/news/epic-online-services-launches-free-in-game-voice-and-easy-anti-cheat>. Unity plugin is PlayEveryWare's MIT plugin [V] <https://github.com/EOS-Contrib/eos_plugin_for_unity> | Free | A good free fallback if UGS terms change. More setup: Epic dev portal, product IDs, login flows |

**Why NGO:** learning Unity is a goal (D1). NGO is what Unity's
tutorials, the Multiplayer Center package (already in `manifest.json`)
and the sessions SDK assume. Its missing feature, prediction, is one
co-op doesn't need. Everything above the transport (lockstep, intents,
snapshots) is our own code anyway, so a later move to Steam or EOS
means swapping the transport, not the design.

**Topology:** client-hosted. The player who presses "Host" runs the NGO
host and is the lockstep **host** (orders batches). No dedicated server.
Distributed Authority (NGO 2.x, 6,000 free connectivity hours a month
[V] <https://unity.com/products/gaming-services/pricing>) lets the session survive
the host leaving, but it adds a service and new concepts. Revisit it
only if host migration matters. The sandbox picks a new host when the
old one leaves; Unity's version should simply end the session at first.

## 2. Connectivity: "invite a friend"

### Options

| Option | NAT traversal | Discovery | Cost at 2–8 players | What a friend needs |
|---|---|---|---|---|
| **UGS Multiplayer Services sessions (Relay + Lobby)** | Relay servers, always works | Join code, or a session list | **$0.** Relay: first 50 average monthly CCU free, then $0.16 per CCU; 3 GiB per CCU free, up to 150 GiB a month. Lobby: 10 GiB a month per region group free. Authentication free [V] <https://unity.com/products/gaming-services/pricing> | Your build. No account (anonymous sign-in) |
| Steam | Steam Datagram Relay | Steam friends, overlay invites | $100 once per app [V] | Steam, and owning or being keyed the app |
| EOS | P2P with relay | EOS lobbies | Free [V] | Your build. An Epic account or device ID login [U] |
| Direct IP | Port forwarding on the host's router | Type the IP | Free | Your IP. Fails behind CGNAT, and many friends can't forward ports |

**Recommendation: UGS sessions with Relay.** The unified package
`com.unity.services.multiplayer` replaces the standalone Relay and
Lobby packages, which are deprecated in Unity 6 [V]
<https://docs.unity.com/relay/relay-and-ngo.html>. A *session* wraps
Lobby, Relay and Matchmaker behind one API, and has join codes [V]
<https://docs.unity.com/en-us/mps-sdk/sessions-overview.md>. Pick the
package version that the Package Manager offers for 6000.3 [U: I found
no 2.x changelog].

Keep **direct IP** as a debug path: LAN tests without the services.

### Join-by-code flow

1. Both players start the game. It signs in anonymously (Authentication).
2. Host clicks **Host**. The game creates a session (max 8 players,
   Relay connection, NGO host) and shows a code such as `K7QX2M`.
3. Host sends the code by Discord or text.
4. Friend clicks **Join**, types the code. The sessions SDK joins the
   Relay allocation and starts the NGO client.
5. The friend's wizard spawns. The host sends a chemistry snapshot.
   The friend is now "synced" (the sandbox's word for it).
6. Session properties carry `build` and `rulesHash`. A mismatched
   client is refused with "update your build", as the sandbox does.

### What the user must set up (only they can)

- A Unity Cloud project linked in **Edit → Project Settings → Services**.
- Relay, Lobby and (later) Vivox enabled on the Unity Cloud dashboard.
  Whether a payment card is needed to enable them is [U]: check
  in the dashboard. Set a usage alert either way.
- Ship builds to friends. A macOS build that isn't signed and notarized
  is blocked by Gatekeeper (right-click → Open works). Windows
  SmartScreen warns on unsigned EXEs. For friends, zip files on a shared
  drive or itch.io (restricted page) are enough.

## 3. Reusing the sandbox's lockstep

Yes: the same design works, and cells never need syncing. It maps like
this.

| Sandbox (`sandbox/src/net.ts`) | Unity |
|---|---|
| `Transport` over the artifact `room` | NGO custom named messages (`CustomMessagingManager`), reliable and sequenced, over Relay |
| Intent JSON, `sanitizeIntent` | A small `struct` per intent, `INetworkSerializable`. Validate on the host the same way (ids, bounds) |
| Host batches `{n, e, s, i[], h?}` six times a second | Same. `sim.json` already says `ticksPerSecond: 6`, so one batch per tick |
| Checksum every 20 batches | `World.CellHash()` already exists (FNV-1a, matches the TS goldens). Hash it on every machine and compare |
| Snapshot to `rooms/<code>` on join, miss or bad hash | Host sends a snapshot message straight to that client |
| Solo is a room of one | Same: solo runs the lockstep loop as host with no peers |

### What has to change

1. **Step on batches, not on frame time.** `ChemGrid.Update` adds
   `Time.deltaTime` to a timer and calls `World.Step()`. Online, only
   the host's clock may decide when a tick happens. The host emits
   batch *n* each 1/6 s. Every machine (host included) calls `Step()`
   only when it holds batch *n*. Render interpolates between ticks
   as now.
2. **Casts become intents applied at a tick.** `SpellCaster.Cast`
   calls `World.Cast` directly. For Fire Bolt it does so in a callback
   when the projectile lands, which is frame-timed and different on
   each machine. Instead:
   - the caster sends `Cast(spellId, x, y, dir)` to the host;
   - the host puts it in batch *n* with an apply tick (*n* plus the
     spell's flight time in ticks, from the rules);
   - every machine calls `World.Cast` at that tick;
   - the projectile is pure presentation, timed to land on that tick.

   The demo's `DemoDirector` casts through the same path.
3. **World snapshot.** The C# port has no save/load yet. Add
   `World.Save()`/`World.Load()` covering cells, `rng`, `Tick`, `Wind`,
   and the per-tick marks. Use the sandbox's snapshot shape so that a
   golden test can load a TS snapshot. Compress it (a 64×64 yard is
   tens of KB raw), and NGO fragments large reliable messages.
4. **The world seed and layout come from the host.** Put them in the
   first snapshot, never `Random` or wall-clock time.
5. **Mods (later):** a `mods` intent switches rule patches on at a
   tick boundary, exactly as in the sandbox.

### Where determinism can break

| Risk | Status | Mitigation |
|---|---|---|
| Float maths in the view (`ChemGrid.Visuals`, lights, particles) | Harmless: the view never writes back to `World` | Keep it that way. Code review rule: nothing in `Scripts/` mutates `World` except the lockstep applier |
| RNG | Fine: mulberry32 lives in world state | Never call `UnityEngine.Random` or `System.Random` in `Chem` |
| Frame-based stepping | Breaks today (above) | Batch-driven stepping |
| `double` in rules (`Flammability`, spread chances, `Rand()`) | Basic IEEE `+ − × ÷` and compares give the same bits on x64 and ARM64, which is why the TS goldens pass. The risk is the compiler fusing `a*b+c` into one FMA instruction on Apple Silicon under IL2CPP or Mono [U] | Checksums detect it and a snapshot heals it. The real fix is D3's rule: integer or fixed-point. For example, chances as per-mille integers and `Rand()` returning `uint`. Test a Mac (ARM) host with a Windows (x64) client early |
| Iteration order (`HashSet`, `Dictionary` enumeration) | Ordered loops today | No enumeration of hash collections inside `Step()`. The golden tests catch drift |
| A client stalls (alt-tab, loading) | Batches queue up | Apply the backlog, up to N ticks per frame. Past that, ask for a snapshot |
| Latency | Cast takes effect after a round trip plus up to 167 ms (one tick) | Fine for a sandbox (`LIVE_LAB.md` already accepts 200–300 ms). Play the cast animation and sound at once, so it feels instant |

Player position isn't in the grid. When chemistry needs to know where a
player stands (fire burns you, ice slides you), each owner computes it
locally from their own position and the shared grid. A cross-player
effect (Thunderwave pushes a friend) goes as an event to the owner,
who applies it. This is client-authoritative and fine for co-op.

## 4. Voice chat

### Options

| Option | Proximity | Raw audio for effects | Cost | Notes |
|---|---|---|---|---|
| **Vivox** (UGS) | Positional channels: audible distance, conversational distance, fade model; position sent 2–4 times a second [V] <https://docs.unity.com/en-us/vivox-unity/developer-guide/channels/positional-channel-configuration> | **Audio taps** (Unity package 16.x): Capture Source tap (own mic), Participant tap (one remote player), Channel tap. Each plays through a Unity `AudioSource`, so Unity Audio components and Audio Mixer groups apply [V] <https://docs.unity.com/en-us/vivox-unity/developer-guide/audio-taps/use-audio-taps>, <https://docs.unity.com/en-us/vivox-unity/developer-guide/audio-taps/audio-taps-in-c-sharp> | **Free up to 5,000 PCU**, then $2,000 per 5,000 PCU [V] <https://unity.com/products/gaming-services/pricing> | Same sign-in as sessions. Latest version seen 16.10.0 [V] <https://docs.unity.com/en-us/vivox-unity/release-notes> |
| Steam Voice | Do-it-yourself (you get compressed buffers and play them) | Yes, after decoding | Free with an app ID ($100) | Steam-only |
| EOS RTC voice | Lobby or room voice, relayed through Epic | Some (audio callbacks) [U] | Free [V] | Pairs with an EOS netcode choice |
| Photon Voice 2 | Yes, via `AudioSource` | Yes | Free to 20 CCU [S] <https://marketplace.unity.com/packages/tools/audio/photon-voice-2-130518> | A second vendor |
| Dissonance | Grid or collider-trigger proximity [V] <https://dissonance.readthedocs.io/en/latest/Tutorials/Proximity-Chat.html>; NGO integration [V] <https://dissonance.readthedocs.io/en/latest/Basics/Choosing-A-Network.html> | Plays through `AudioSource`; custom DSP hooks [U] | About $100–110 once [S] <https://assetstore.unity.com/packages/tools/audio/dissonance-voice-chat-70078> | Voice rides your own netcode, through Relay. No extra service |
| Discord | None in-game | None | Free | What friends will do until in-game voice exists |

**Recommendation: Vivox.** It's free at our scale, first-party, uses
the UGS sign-in we already need, and the taps give the raw access spell
effects need. **Dissonance** is the fallback if the tap spike (below)
fails. It sends voice over NGO, so it gets the same proximity and
`AudioSource` control with no extra service.

### Two ways to do proximity with Vivox

| | A. Vivox positional channel | B. Participant taps + Unity 3D audio |
|---|---|---|
| How | Join one 3D channel; call `Set3DPosition` 3 times a second | Join a non-positional yard channel. For each remote player, create a participant tap on their avatar's head, set "Silence In Channel Audio Mix", and use the `AudioSource`'s 3D spatial blend and rolloff |
| Who decides loudness | Vivox's server and SDK, from one `Channel3DProperties` set for the channel | Our code, per speaker and per listener, every frame |
| Spells | Can't change one person's range | Any change: gain, rolloff, mute, filters, moving the voice |
| Bandwidth | Only nearby voices arrive | Every voice arrives (fine for ≤8) |
| Risk | Low; the documented path | Tap latency and "silence in mix" behaviour need a spike [U] |

**Recommendation:** ship **A** in phase 2 (an hour of work, and it
proves voice works). Spike **B** at the start of phase 3. If B
behaves, switch to it, since every spell idea below needs it.

Vivox limits that matter: up to 10 channels at once, of which at most
1 positional; 200 people per channel [V]
<https://support.unity.com/hc/en-us/articles/29282864148372>. So a
yard channel plus a few private spell channels fits easily.

### Voice basics either way

- **Push-to-talk by default**, with an open-mic option, a per-player
  mute and volume, and a speaking indicator over each head.
- **macOS microphone:** set **Microphone Usage Description** in Player
  Settings, or the app is killed when it opens the mic. Notarized builds
  outside the App Store also need the `com.apple.security.device.audio-input`
  entitlement [V] <https://support.unity.com/hc/en-us/articles/4431473872020>,
  <https://docs.unity.com/en-us/vivox-core/developer-guide/macos/macos-requirements>.
- **Privacy:** voice passes through Vivox's servers. Sessions are
  private (code only), and nothing is recorded, except Magic Mouth below
  (local, opt-in). Write a one-paragraph notice in the build's README.
  Moderation is "mute and leave" at friends scale. Vivox Safe Voice
  exists if this ever goes public.

## 5. Spell-augmented voice

### The principle

Spells are already shared state: they're cast through lockstep, so every
machine knows "Thaumaturgy on player 2 until tick 3,610" and "Silence
sphere at cell (12,8), radius 4". Voice effects are then **pure
functions of that state, evaluated by each listener**:

```
for each remote speaker S, every frame:
  gain, maxRange, filters, sourcePos = VoiceRules(S, me, world, spellEffects)
  apply to S's participant-tap AudioSource
```

No voice audio is altered in transit, so nothing needs to agree except
the spell state, which already does. The exceptions are private
channels, where who receives the audio matters for privacy.

Rules data: add a `voice` block to spell entries in `rules/spells.json`
(for example `{"rangeMul": 3}`). The sandbox can ignore it, mods can tune
it, and both versions keep one vocabulary (`LIVE_LAB.md`).

### Spells (all names verified in SRD 5.2.1)

Names and text were checked against the SRD 5.2.1 markdown
(<https://github.com/downfallx/dnd-5e-srd-markdown>, `spells.md`).
**Ventriloquism and Telepathy (as a spell) are not in the SRD.
Disguise Self changes looks only, not voice.**

| Spell (SRD 5.2.1) | What the SRD says | Voice effect | How, technically | With Vivox |
|---|---|---|---|---|
| **Thaumaturgy** (cantrip): *Booming Voice* | "Your voice booms up to three times as loud as normal for 1 minute" | Your range ×3, +6 dB, a touch of low-shelf boost and reverb | Rolloff max distance ×3 and gain up on your `AudioSource`, on every listener | B. In A, impossible per person |
| **Silence** (level 2) | "No sound can be created within or pass through" a 20-ft sphere; creatures inside are Deafened; no Verbal spells | Speakers inside are mute to everyone. Listeners inside hear no one. A voice whose line to you crosses the sphere is cut | Gain 0 when speaker or listener is inside, or the segment crosses the sphere (a ray-sphere test). The sphere is a world effect, so every machine agrees. Also blocks casting V spells inside, a free gameplay rule | B |
| **Message** (cantrip) | Whisper to one target within 120 ft; only they hear it, and they can whisper back. Blocked by magical silence or 1 ft of stone, metal or wood | A private 1-round whisper (about 6 s, or hold the cast key) to one player, and their reply | Create Vivox channel `msg-<a>-<b>`. Both join it; transmission switches to it while the key is held. 2D, close, slightly breathy (high-pass filter). Line-of-sight check against Silence and thick walls before it opens | Yes (channels). Real privacy: others never receive it |
| **Sending** (level 3) | A message of 25 words or fewer to a known creature at any distance; the target can answer at once | A 10-second push-to-talk to one player anywhere, then one reply window | Same as Message, plus a "voice in your head" effect: 2D, reverb, slight chorus, and a chime | Yes (channels and a participant tap for the effect) |
| **Telepathic Bond** (level 5) | Links up to 8 willing creatures, any distance, 1 hour | A party channel that ignores distance | Non-positional channel `bond-<id>`; members transmit to yard and bond. Effect as for Sending, quieter | Yes |
| **Minor Illusion** (cantrip): *Sound* | A sound from a whisper to a scream, which "can be your voice", within 30 ft, for 1 minute | **Throw your voice** to a point for a minute | Each listener moves your `AudioSource` from your head to the illusion point (a proxy source). Your real position goes silent. Optional: "Study" check makes it faint for that listener | B |
| **Alter Self**: *Change Appearance* (level 2) | You decide your look, "including … sound of your voice" | Pick a voice: deep, high, gravelly, echoing | Route your participant tap to a mixer group with a Pitch Shifter, EQ or distortion. A handful of preset groups (Deep, High, Raspy, Hollow) | B (taps → mixer) |
| **Polymorph** (level 4) | The target becomes a Beast | Speech becomes animal noises | Heavy distortion and low-pass, or replace speech with animal sounds keyed to your mic level (a level detector on the tap) | B |
| **Magic Mouth** (level 2) | Implant a message of 25 words or fewer in an object; it is spoken in your voice when a trigger occurs | Record up to 10 s; it plays from an object when someone walks near | Record from the Capture Source tap locally, compress it, send it once as a reliable NGO message. The trigger is a chemistry or cell event, so everyone plays it at the same tick | Taps plus NGO. The heaviest feature |
| **Enthrall** (level 2) | Targets get −10 to Wisdom (Perception) | Affected listeners hear other speakers ducked, so they hear only the bard | −10 dB on all speakers but the caster, for affected listeners | B |
| **Gust of Wind** (level 2) | A 60-ft line of strong wind | *Not in the RAW text, our invention:* voices carry downwind and are cut upwind | Per pair, scale range by the angle between the speaker→listener direction and `World.Wind`, which is already in state | B |
| **Tongues** (level 3) | Understand and be understood in any language | None. The game has no languages yet | Skip. Revisit if NPC languages or speech-to-text exist | — |

**Feasibility:** with **A** (positional channel) only Message, Sending
and Telepathic Bond work, because they're just channel membership.
Everything else needs **B**, because it changes one speaker's range,
location or sound for one listener. That's why B is the phase 3 spike.

**Cheating is a non-issue in co-op.** In B everyone receives every
voice and muting is local, so a modified client could hear through
Silence. Only the private spells (Message, Sending, Bond) get true
privacy, via separate channels.

## 6. Phased plan

Estimates assume evenings, someone learning Unity, with Claude Code
writing most of the C#. A "week" is about 4–6 evenings.

### Phase 1: "Friend joins my yard" (about 2 weeks)

Done when: two machines, on different networks, see the same fire
spread from each other's casts, and see each other's wizard run around.

1. **Determinism groundwork, no networking** (2–3 evenings, all in
   `Chem`, testable with `dotnet test`): `World.Save/Load`; a
   `Lockstep` class ported from `net.ts` against an abstract transport;
   a test with two worlds, a late join and dropped batches (a port of
   `net.test.ts`).
2. **Batch-driven ChemGrid** (1–2 evenings): step only on batches.
   Solo runs as a host of one. `SpellCaster` sends intents; projectiles
   become presentation only.
3. **NGO and sessions** (3–4 evenings): add
   `com.unity.netcode.gameobjects` and `com.unity.services.multiplayer`;
   a Host/Join UI with the code; anonymous sign-in; an NGO transport
   for `Lockstep` (named messages).
4. **Networked player** (2–3 evenings): player prefab with
   `NetworkObject`, `NetworkTransform` (owner authority, interpolated),
   `NetworkAnimator` (owner). On non-owners, disable
   `ThirdPersonController`, `StarterAssetsInputs`, `PlayerInput` and the
   Cinemachine camera, and spawn the camera only for the owner. Name tags.
5. **Cross-machine test** (1 evening): Mac host and Windows client, with
   hash checks logged. Fix any desync before moving on.

### Phase 2: Voice (about 1 week)

1. Add `com.unity.services.vivox`, enable Vivox on the dashboard.
2. Join the positional channel named after the session; send position 3
   times a second; push-to-talk; mute and volume; speaking indicator.
3. macOS mic permission and entitlement; a test on both platforms.

### Phase 3: Spell voice (about 2 weeks)

1. **Spike B** (2 evenings): participant taps on avatars, "silence in
   mix", Unity rolloff. Measure latency. Decide B or fall back to
   Dissonance.
2. `VoiceRules` from world spell effects, plus `voice` blocks in
   `rules/spells.json`.
3. Order: Thaumaturgy → Silence → Minor Illusion → Message → Alter
   Self → Sending/Telepathic Bond → Enthrall/Gust → Polymorph → Magic
   Mouth. Each one is a small, playable win.

### Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Desync between Mac (ARM) and Windows (x64), from float/FMA | Medium | Grids drift | Checksums plus snapshots heal it. Move rule chances to integers. Test cross-arch in phase 1 |
| Something frame-timed sneaks into `World` | Medium | Desync | One applier writes to `World`. Code rule plus the hash check |
| Starter Assets controller on non-owners (input and camera fight) | High at first | Jitter, wrong camera | Disable input, controller and camera on non-owners. Owner-authoritative transform. Known NGO pattern |
| NAT and firewalls | Low with Relay | Can't join | Relay always relays. Direct IP for LAN debugging |
| Unsigned builds blocked (Gatekeeper, SmartScreen) | High | Friend can't launch | Right-click → Open; notarize later |
| macOS microphone permission | High if forgotten | Crash or silent mic | Player Settings string plus entitlement |
| Vivox tap behaviour differs from docs | Medium | Phase 3 blocked | Spike first; Dissonance fallback |
| UGS pricing or terms change (Multiplay was wound down in 2026 [S] <https://crux.supercraft.host/blog/unity-gaming-services-alternatives/>) | Low | Cost or rework | Usage alerts; lockstep sits above the transport, so EOS or Steam can replace it |
| Voice privacy | Low (friends) | Trust | Private sessions, push-to-talk default, mute, no recording except opt-in Magic Mouth |
| Account setup only the user can do | Certain | Blocks phase 1 step 3 | Do it first: Unity Cloud project, link it, enable Relay/Lobby |
| Version mismatch between friends | Medium | Desync or crash | `build` and `rulesHash` in session properties; refuse a mismatch |

## 7. Physics props and items online (built 2026-10-25)

What the Unity yard does today, with what was measured in Multiplayer Play
Mode (one Mac, 2 and then 4 players, same-network transport).

**Design.**
- Every crate, barrel, boulder and thing to carry is an in-scene
  `NetworkObject` (built by `PropBuilder`; never edit the scene by hand,
  rebuild it so each object gets its id). The host simulates its
  Rigidbody; clients get a kinematic copy moved by a server-authority
  `NetworkTransform` (interpolated). `NetProp` replicates only burning,
  levitating and hit points, for the flames and the log.
- What a prop does to cells (a barrel's blast, embers, a burning crate's
  fire, a smashed lantern's spill) is a `Chem.EffectCommand`. Only the
  host's physics produces them; `NetChem` stamps them into the next tick
  batch next to the casts, every machine applies them at that tick
  (`TickBatch.RunOn`), and a late joiner replays them from the host's
  history. Clients never apply effects themselves.
- Spell shoves follow the cast's tick batch: the host shoves the props,
  each player shoves their own wizard (owner-authoritative movement), from
  the caster's position carried in `CastIntent.From`. A shove the host's
  physics gives another player's wizard (a barrel blast) is sent to that
  player's machine (`NetWizard.PushOwner`).
- Items: `NetItem` holds the holder wizard's object id. Picking up asks
  the host (checks the item is free and within 4 m) and every machine puts
  the item in that wizard's hand; dropping or throwing sends the hand's
  position and the velocity, and the host's physics flies it. A lantern's
  or ball's light being uncovered is shared; the Dancing Lights mote's
  position is sent by its holder 15 times a second; a sending stone's
  message goes only to the machine of whoever holds the other stone. The
  spyglass is local.
- A broken prop is despawned without being destroyed and hidden on every
  machine (Netcode's way for in-scene objects), so late joiners hide it too.
- `AnnounceCastRpc` finds the caster's wizard by its object id (a client
  can't look up other players by client id), so every player sees every
  cast's show.

**Measured.**

| Test | Result |
|---|---|
| Host Thunderwave beside a crate | Crate thrown 2.78 m on the host; the client's copy trails by about 120 ms (interpolation): 0.77 m apart at the same instant mid-flight, mean 0.01 m once that delay is allowed for, 5 mm apart at rest |
| Client Thunderwave at the barrels | Barrel thrown 3.32 m by the host's physics; client within 3 mm at rest |
| Client lights the oil (chain) | KABOOM ×3 at tick 306 and embers at 375 and 390 on both; the same 6 props hidden on both; all 29 checksums (every 30 ticks) equal |
| Client knocked back by the host's Thunderwave | Moved 2.42 m on its own machine; the host's view within 5 mm |
| Client picks up a ball, walks, throws | Held in player 2's hand on the host; flew 10.85 m on the host after the throw; both at rest within 4 mm |
| Client hurls the lantern | Smashed (12.3 m/s on the host) and the spill stamped at tick 1263 on both |
| Sending stones | Host to client and back: message and ping only on the receiver; sender's log says who heard it |
| Dancing Lights mote | Client's mote at (24.67, 0.50, 0.06), host's copy at (24.64, 0.50, 0.04); back in hand on both |
| Late join after explosions (player 2 rejoins; 31 effects in the history) | Same props hidden, positions within 1 cm, a held stone in the holder's hand; checksums equal after catch-up |
| 4 players, player 4 joining late | Host's Fire Bolt shown on both clients; KABOOM at tick 195 on all; player 3's Thunderwave moved a crate 2.78 m (same on all) and player 2 by 2.34 m; player 4 replayed the history from tick 0 (casts and effects) and its 50 checksums matched the host's |

**Known limits.** A client's wizard can't push a crate by walking into it
(its copy is kinematic); the Starter Assets push works only for the host.
A held item rides in a hand pose that differs a little between machines
(0.27 m measured for a held stone). With four Editors on one Mac a late
joiner's transport still logged "Receive queue is full (512)" once while
catching up; the receive queue is 512 packets (`ChemYardBuilder`), and
reliable messages are resent, so nothing desynced.

## 8. Online on the chunked valley (checked 2026-10-26)

The first online check since the valley became 9×9 chunk terrains
(`feat/chunked-valley`, slices 1 and 2). It is now a probe scenario,
`tools/probe.sh online` (`docs/FAST_LOOP.md`): this Editor hosts, the
Multiplayer Play Mode virtual player Player 2 joins on 127.0.0.1, and
both cast through the network on one schedule of sim ticks.

| Test | Result |
|---|---|
| By hand: host and Player 2 each cast Fire Bolt, Earthen Trench and Wall of Stone; Player 2's Thunderwave | Every 30-tick checksum both machines logged was equal (ticks 0 to 420); no desync warning. Trench cells: the same heights and water on both, the terrain drawn under them the same (-0.998 m at the host's trench on both), and the drawn water surface on the same cells. Crate 6 thrown 3.30 m by the host's physics; the client within 5 mm |
| By hand: Player 2 leaves and rejoins at tick 924 (29 prop effects in the history) | Checksums equal from 930 to 1020 after catch-up; the same props left; trenches, walls and water as on the host |
| `probe online`, Player 2 started by the probe (54 s) | 2 players; 7/7 checksums equal (ticks 30 to 210) and the same world at the sample tick (217); terrain under the four earth spells drawn identically (0.00 m apart, the host's trench 5.48 m lower in sum, the client's 7.98 m); the same wet and drawn-wet cells (3 in the host's trench, from the pond); the same raised walls; crate 6 thrown 3.28 m, the client's copy 0.7 cm from it; the host's wizard thrown 2.17 m by the client's Thunderwave, the client's view 0.7 cm from it; no console errors on either |

**Found and fixed.** No desync. One error: another player's wizard
landing from a fall (8 m or more) threw a `NullReferenceException` on
every other screen, in the Starter Assets `ThirdPersonController.OnLand`.
A puppet's controller is switched off, so its `Start`, which looks up the
`CharacterController`, never runs, but the Animator still sends it the
landing and footstep events. `NetWizard` now hands it the
`CharacterController` (`WizardRig.PrepareForAnimationEvents`), so other
players' landings and footsteps play. The probe's host wizard falls 10 m
to cover it: without the fix `client_no_console_errors` fails.

**Not affected by chunking.** The chemistry's active chunks are never
switched off at runtime (nothing calls `World.SetActive`), so host and
clients always step the same chunks. The terrain a client draws comes from
the same cells through `CellHeightfield`; `OnlineValleyTests` checks in the
core that a client and a late joiner on a valley-sized world keep the
host's checksums and draw the same heightfield.

## Proposed decision entries (not yet in `DECISIONS.md`)

```markdown
## D7. Netcode: NGO, client-hosted, lockstep chemistry (2026-10-24, proposed)

- **Decision:**
  - Netcode for GameObjects 2.x on Unity Transport, client-hosted
    (one player hosts). Unity Multiplayer Services sessions with
    Relay and a join code for connecting; anonymous sign-in.
  - The grid chemistry stays lockstep, as in the sandbox (D6): the
    host orders cast intents into one batch per tick, every machine
    steps on batches, `CellHash` checksums, snapshot on join or
    desync. Cells are never streamed.
  - Players are owner-authoritative (NetworkTransform/Animator,
    interpolated). PhysX props are host-authoritative; any result
    that matters to chemistry is quantised into an intent (D4).
- **Why:** first-party and the most learnable path (D1); co-op needs
  no prediction; the deterministic core already exists and is tested;
  $0 at friends scale.
- **Reopen if:** competitive play, more than ~8 players, shipping on
  Steam (swap the transport for Steam sockets), or a desync rate that
  integer rules can't fix.

## D8. Voice: Vivox, with spell effects applied by each listener (2026-10-24, proposed)

- **Decision:** Vivox for voice. Start with a positional channel; move
  to participant taps played through Unity AudioSources so the game
  owns falloff and effects. Spell voice effects are a pure function of
  the shared spell state, evaluated by each listener. Private spells
  (Message, Sending, Telepathic Bond) use separate Vivox channels.
- **Why:** free to 5,000 PCU, same UGS sign-in, and taps give the raw
  audio access that spell effects need.
- **Reopen if:** the tap spike fails (then Dissonance over NGO), or
  the game moves to Steam or EOS for netcode.
```
