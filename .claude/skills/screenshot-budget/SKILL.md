---
name: screenshot-budget
description: Run visual review across several Claude sessions without burning tokens on images. Sets who looks at which screenshots and at what size, adds an unreviewed full-resolution gallery for the human, a shot checklist that prevents retakes, and a written-check report format so a supervisor can relay results without opening images. Use this whenever you supervise or coordinate other agents that produce screenshots (game, UI, rendering, data-viz work), when the user worries about token or context cost from images, asks for "cheaper reviews", "fewer screenshots", "lower-res shots", a gallery, or when a long session keeps hitting context limits because of images. Pairs well with agent-coordinator, demo-reel and project-update.
---

# Screenshot budget

Images are the most expensive thing most supervising sessions read. This skill sets up a review pipeline where
every image is looked at by at most one model, at the smallest size that can answer the question, and the human
still gets a rich gallery for free.

## Why it matters (the numbers)

- An image costs roughly `width × height / 750` tokens once it is opened (after the model's own downscale).
  - 1600×900 ≈ 1,900 tokens. 1075×1182 ≈ 1,700. 800×450 ≈ 480. 480×270 ≈ 170.
- Capturing, saving and uploading a file costs **zero** tokens. Only *viewing* it costs.
- Viewed images stay in the conversation. Every later turn carries them again (cheaper when cached, but not
  free), and they are usually what forces a long session to be summarised.
- The common waste: a worker views its shot, then the supervisor views the same shot at full size. That is double
  cost for one judgement.

Typical result of adopting these rules: image tokens down ~90% for the supervisor and ~85–90% for workers;
overall supervisor usage down a third to a half.

## The rules

Write these into the team's coordination file (template below) and tell every worker.

1. **Workers are the visual reviewers.** They check their own shots at about **480 px** on the long edge while
   iterating. That is enough for pose, placement, contact and framing. They should be as critical as a reviewer:
   does it read as the thing that was asked for, any gaps, floating, clipping, does it read at a distance.
2. **The supervisor opens one image per finished visible feature**, and only when it is going somewhere the
   supervisor must vouch for it (a milestone or showcase page). 800 px on the long edge; a contact sheet counts as
   one image and may be up to 1200 px. Do the review and the page entry from the same viewing.
3. **Everything else is relayed, not re-viewed.** For fixes, follow-ups, interim progress and anything not visible
   (performance, audio, networking, culling), the worker sends a written check and the supervisor passes it on,
   saying "the session reports …" so the human knows it was not independently checked.
4. **Detail comes from framing, not resolution.** If a small thing matters (a seam, a texture, a contact point),
   frame tighter or add a crop. Do not raise the resolution of the whole shot.
5. **A free gallery for the human.** Besides the reviewed shots, workers save a fuller set in
   `<shots root>/<lane>/<feature>/gallery/`: full resolution, more angles, before/after sets, short sequences.
   Workers never open these and never send them to the supervisor; the report just names the folder. Cap around
   40 images per feature so the folder stays browsable.
6. **No images for invisible changes.** Give numbers instead (fps, ms, counts, a log line).

## Shot checklist (prevents most retakes)

Retakes are the hidden cost. Most come from the same handful of mistakes:

- **True aspect ratio.** Render at the aspect you save at (set the camera's aspect explicitly). An editor game
  view with an odd shape squeezed into 16:9 stretches everything: round things go oval and nobody notices for
  days.
- **Fixed conditions:** same time of day, weather, seed and random appearance (coats, colours) for before and
  after. Default to noon and clear weather unless the feature needs otherwise.
- **Same camera** for before and after.
- **Keep the player or avatar out of frame and out of the line of sight** (see-through or occlusion effects cut
  holes in walls between camera and player).
- **Light dark scenes** (caves, night) enough that the feature is visible, or say why it is dark.
- **Frame the feature:** 3–8 m away, whole subject in frame, feature in the middle, not the player.
- **Wait for streaming** (things to finish loading) before capturing wide or overhead shots.
- **At most 3 reviewed images per report.**

## Report format (worker → supervisor)

```
DONE: <feature>, <commit hashes>
What changed: <2–6 bullets>
Written check: looked for <x>, saw <y>; caveats <z>
Final (page features only): <path to one 800 px image or sheet>
Gallery (not reviewed): <folder>
NEXT: <next task>
```

## Coordination file template

Paste into the team's coordination doc (adjust paths):

```
- Shots: true aspect, fixed conditions, same camera for before/after, avatar out of frame, dark scenes lit,
  feature framed 3–8 m. Max 3 reviewed images per report, 800 px long edge (sheets ≤1200 px). Crop for detail.
  Numbers, not images, for changes you can't see.
- Who looks: check your own shots at ~480 px. Send the supervisor one 800 px final only for a finished visible
  feature going on the milestone page. For fixes and interim steps send a written check; the supervisor relays it
  without opening images.
- Gallery: save a full-res set per feature in <shots root>/<lane>/<feature>/gallery/ for the human. Never open it
  yourself and don't send it to the supervisor; name the folder in the report. ≤40 images.
```

## Supervisor habits

- Before opening any image, ask: is this going on a page I publish? If not, relay the written check.
- When you do open one, open the final only, never the iteration shots.
- Keep updates to the human short (3–5 lines plus a link); the page or the gallery holds the detail.
- If a worker's written checks keep missing problems the human later finds, tighten that worker's checklist
  rather than going back to reviewing everything yourself.
- Trade-off to tell the human: a few taste issues the supervisor would have caught now reach the human first.
  They act as the final reviewer when they play or browse the gallery.
