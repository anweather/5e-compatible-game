---
name: demo-reel
description: Build and keep growing a shareable demo reel, a claude.ai Artifact page of short video clips and screenshots of each feature as it lands, newest first, with before/after pairs for fixes. The page itself never changes: clips live in a reel.json list published beside it, so adding a feature only appends files. Also sets the team rule that every visible feature ships with a clip. Use this whenever the user wants a showcase, demo page, changelog with videos, feature gallery, "show my friends what we built", progress reel, before/after screenshots, or asks for an artifact that keeps getting new clips or screenshots added over time, even if they don't say "reel".
---

# Demo reel

A reel that grows with the project is the best way to show friends,
playtesters or yourself what changed. The trick is to keep the page fixed
and only ever append data. Then adding a feature is cheap and can't break
the page.

## How it's built

- `assets/reel.html` is the page. It fetches `reel.json` (published beside
  it) and draws the drops, newest first. Each drop is a dated group of clips,
  sub-grouped by "area", with lane filters. It handles video clips with
  posters, single screenshots, and before/after pairs. It works in light and
  dark themes and at phone width.
- `assets/reel.json` is the clip list. A clip has `title`, `lane`, `area`,
  `length`, `commit`, `summary`, plus `video` + `poster`, `image`, or
  `before` + `after`. Media names must be plain file names published beside
  the page. The page refuses paths and URLs.

Publish with the Artifact tool. Load the artifact-design guidance first, as
the Artifact tool requires. Start from these assets and restyle the page for
the project: its name, palette and type. Keep the data contract.

**Why published files, not the database or asset store:** a reel is usually
shared by public link. Published files load for signed-out viewers; the db
and asset-store capabilities need a sign-in. If the reel is only for
signed-in teammates, the db and assets route also works.

## Adding a feature

1. Get the media from the agent that merged the feature (the rule below).
2. Copy the files into the folder you publish from. The Artifact tool only
   takes files under the working directory or the scratchpad.
3. Add an entry to `reel.json`: new clips go in the newest drop, and a new
   drop starts per day or per release. Check that the JSON parses.
4. Republish the same page `file_path` (or `url`) with `files` listing only
   the new media plus `reel.json`. Files you leave out are kept, so you never
   re-upload old clips.
5. Keep `reel.html` and `reel.json` in the repo too (for example
   `showcase/`), so the reel survives the session that made it.

## The team rule

Add this to COORDINATION.md (or CLAUDE.md):

> Every feature you can see ships with a clip: MP4 (H.264), 720p, 30 s at
> most, 5 MB at most, plus a poster JPG. Use a screenshot if a clip doesn't
> suit the feature. Fixes get a before and an after. Film it while the
> feature's automated test or probe runs. Save the files in a git-ignored
> folder (for example `Builds/demo-videos/`) with short, unique, plain names,
> then send the coordinator: the file names, a title, the lane, an area, the
> length, the merge commit, and one or two plain sentences.

Things you can't see (a crash fix, a settings change) don't need a clip.
Say so instead of filming something meaningless.

## Gotchas

- **Space:** one artifact version holds at most 256 MB and 511 files. About
  50 clips of 3–5 MB fit. Near 200 MB, start "Volume 2" and link the two.
- **Side-by-side images get cropped:** the cards are 16:9 and
  `object-fit: cover`. Split a side-by-side comparison into separate before
  and after images (System.Drawing on Windows, or ImageMagick or PIL
  elsewhere).
- **Long paths on Windows:** scratchpad paths can exceed 260 characters, so
  PowerShell 5.1's `Get-Content` fails on them. Validate the JSON from the
  repo copy instead.
- **Media from another machine:** have that agent commit or upload the files
  somewhere you can read. You can't publish a file you can't reach.
- **Read files you didn't make before publishing them,** as the Artifact tool
  asks. Don't put anything private (tokens, personal data in screenshots) on
  a public reel.
