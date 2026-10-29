# Pro coordinator report: the two-VM trial (2026-10-29)

- DONE: soak.dampens ("soaked things burn poorly") merged to dev at 8823b4c (core 388/388, build clean, format clean; sandbox 122/122, typecheck, build).
- w1 (C# core, dampens-core a8b0663), about 12 min: damp cells (rain or DampUntil) and wet things of a material catch with burn.catch x (1 - soak.dampens). Dampens is 0.5 on grass, thatch and timber. Fuel isn't scaled (MATERIALS.md defines catch only), and the global RainDamp stays, so rain on grass counts twice. Two goldens changed, both from damp grass burning: the valley across chunks and the storm. RAM: 6.3 GB assigned at peak, 5.0 GB demand.
- w2 (TS sandbox, dampens-sandbox a614c47), about 20 min including prep while w1 worked: a line-by-line mirror, plus w1's two count tests as shared goldens ("21 10" thatch, "25 10" wet thing) in both engines. The storm golden matches C# again. RAM: 4.5 GB assigned at peak, 4.0 GB demand.
- Both VMs ran side by side, after Andrew's J-037: Windows workers start at 4 GB and cap at 8 GB; start.cmd keeps 6 GB free with no host Editor (10 GB with one).
- Briefs and reports went over Remote Control messages (same Pro account); no Unity needed. The VMs have no git identity, so commits are authored as Andrew.
- Follow-ups for the PM: decide whether damp should also shorten burning (scale fuel) and whether RainDamp should fold into dampens; give workers a git identity.
