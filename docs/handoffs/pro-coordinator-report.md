# Pro coordinator report: the two-VM trial (2026-10-29)

- DONE: soak.dampens ("soaked things burn poorly") merged to dev at 8823b4c (core 388/388, build clean, format clean; sandbox 122/122, typecheck, build).
- w1 (C# core, dampens-core a8b0663), about 12 min: damp cells (rain or DampUntil) and wet things of a material catch with burn.catch x (1 - soak.dampens). Dampens is 0.5 on grass, thatch and timber. Fuel isn't scaled (MATERIALS.md defines catch only), and the global RainDamp stays, so rain on grass counts twice. Two goldens changed, both from damp grass burning: the valley across chunks and the storm. RAM: 6.3 GB assigned at peak, 5.0 GB demand.
- w2 (TS sandbox, dampens-sandbox a614c47), about 20 min including prep while w1 worked: a line-by-line mirror, plus w1's two count tests as shared goldens ("21 10" thatch, "25 10" wet thing) in both engines. The storm golden matches C# again. RAM: 4.5 GB assigned at peak, 4.0 GB demand.
- Both VMs ran side by side, after Andrew's J-037: Windows workers start at 4 GB and cap at 8 GB; start.cmd keeps 6 GB free with no host Editor (10 GB with one).
- Briefs and reports went over Remote Control messages (same Pro account); no Unity needed. The VMs have no git identity, so commits are authored as Andrew.
- Follow-ups for the PM: decide whether damp should also shorten burning (scale fuel) and whether RainDamp should fold into dampens; give workers a git identity.

## Round 2 (2026-10-29, both VMs in parallel, one feature each, both engines per branch)

- w1, damp-fuel (merged at 4d42d9c), about 8 min: damp or wet things that catch now burn shorter. Fuel is x (1 - dampens), at least 1 (Andrew's call). A lone thatch burns 18 ticks dry and 9 damp. The storm and valley goldens moved. A cell that's already burning when it turns damp is unchanged (no one-line way; rain douses it already). RAM: 5.2 GB assigned at peak, 4.3 GB demand.
- w2, frozen-shatter (merged at d5214d8), about 13 min by the clock: a thing with the frozen status shatters on any hit except cold ("Goblin shatters"). The data is in sim.json status.shatter; there's a shared golden in both engines, and no goldens changed. RAM: 7.3 GB assigned at peak, 6.3 GB demand.
- One branch per feature covering both engines worked better than splitting the engines across VMs: nothing broke between merges. w2 rebased on w1's merge before pushing. Core 390/390, sandbox 124/124.
- Follow-ups: the view for a shatter (shards and sound instead of the defeat animation; Gameplay's lane) and probes for both features (they need the host Editor).

## Round 3: corrosion (MATERIALS step 6 done; parallel, both engines per branch)

- w1, rust (merged at 8d05ccd), 8 min: metal that's been wet for 2000 ticks rusts into rusted-metal (dig hardness 2, doesn't conduct), with "The metal rusts through". Cell.Rust is saved and in CellHash. The mixed-elements golden moved (its rust count shows). RAM: 6.5 GB assigned at peak, 5.5 GB demand.
- w2, acid (merged at de14cc1), 13 min: the acid element; Acid Splash (cantrip, nature group, off the radial: every group already has 3); a dissolve verb (brambles to dirt; oil, grease and thorns eaten). No goldens moved; two rule-count tests updated. RAM: 6.6 GB assigned at peak, 5.1 GB demand.
- Core 394/394, sandbox 127/127.
- For Gameplay (view): rusted-metal's colour (CellDressing.cs; sandbox render.ts too); Acid Splash's radial slot (a decision), icon, VFX, hiss and the "eaten" look; a shatter effect (round 2).
- For Andrew: merged branches (dampens-core, dampens-sandbox, damp-fuel, frozen-shatter, rust, acid) wait for cleanup. Acid Splash's blurb says "RAW", which players may not know.

## For the PM (Max account): how to hand the Pro coordinator work

You can't message the Pro coordinator, and it can't message you. Use the file bridge (tools/workers/bridge.sh, and the "Two Claude accounts" section of tools/workers/README.md). **Always wake it first:** its session may be asleep or gone.

    bash tools/workers/bridge.sh wake coordinator

If it's waiting or busy, this does nothing ("coordinator is listening"). Otherwise it opens a new Pro coordinator session (a Git Bash window, Remote Control name `coordinator`) that reads its inbox as soon as it starts. Then send:

    BRIDGE_FROM=pm bash tools/workers/bridge.sh send coordinator "next task" <<'MSG'
    <goal, files, checks, stop point>
    MSG

The coordinator waits on its inbox in the background, so a message wakes it. It answers through `send pm`. Keep `bash tools/workers/bridge.sh wait pm` running as a background command to be woken by its replies.
- **Check it was read:** `send` prints the message's id. After 5 min, `bash tools/workers/bridge.sh status coordinator <id>`: "acked" means a Claude has read it; "unread" or "taken, not acked" means nobody's listening, so run `wake` again (a taken message goes back to the inbox after 10 min unacked, so nothing is lost). History: `~/.agent-bridge/bridge.log`.
- **Your side too:** when your `wait pm` hands you mail, `bash tools/workers/bridge.sh ack pm all` once you've read it, then start the next `wait pm` before doing the work.
- Capacity: w1 and w2 are Pro (the coordinator drives them); w3 and w4 will be Max once Andrew signs them in, and you can message those directly.
- Every worker commit carries `Worker-Account`, `Worker-Time`, `Worker-Tokens`, `Worker-Context` and `Worker-Tools` trailers (`git log --grep Worker-Tokens`). Hyper-V's per-run CPU, RAM and disk are logged in C:\HyperV\Workers\metering.csv.
