#!/usr/bin/env bash
# The Mac's voice test set: game asks as 16 kHz mono 16-bit WAVs, spoken by macOS `say`, into
# tools/voice-samples/out (git-ignored). The file's name is its words with dashes, which is what
# the stub transcriber says for it, and what a real transcriber should hear. make.ps1 is the Windows
# set, with word timings for RecordedSpeech; this one writes WAVs only.
#
# It writes files only: nothing plays aloud, and no device or setting changes.
#
#   tools/voice-samples/make.sh
#   -speech-check -speech stub-transcriber -speech-wav tools/voice-samples/out/fireball.wav
set -euo pipefail
out="$(cd "$(dirname "$0")" && pwd)/out"
mkdir -p "$out"

asks=(
  "make the fire near me green"
  "make the deer three times bigger"
  "cast fireball at the barn"
  "turn the rain off"
  "start a storm with lightning"
  "freeze the river"
  "give me resistance to fire"
  "how much damage does magic missile do"
  "make all fire blue"
  "undo the last change"
)

for ask in "${asks[@]}"; do
  file="$out/${ask// /-}.wav"
  say -o "$file" --data-format=LEI16@16000 "$ask"
  echo "$file"
done
