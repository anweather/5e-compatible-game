# The speech check, and where Whisper plugs in (#58)

Status, 2026-11-03: Whisper is the default recogniser, on branch
`whisper-spike`. `WhisperTranscriber` runs whisper.cpp (the MIT `whisper.unity`
package, pinned at 1.4.0 in `Packages/manifest.json`) on this machine with
`ggml-base.en.bin`. Windows dictation stays as `-speech windows`. The stub
stays for tests.

## What's there

- **The seam** is the one from J-061: `ISpeech`, picked in `SpeechChoice`.
  A transcriber is a second kind of adapter, `TranscribedSpeech`, which
  records **speech audio** (`ISpeechAudio`: `MicrophoneAudio` or
  `WavAudio`, both handing over 16 kHz mono floats), and on release gives the
  samples to an `ITranscriber` on a worker thread. Its words come out as one
  `Final`, then `Done`, through `Take`, so `GameVoice` puts them in the chat
  box and sends them exactly as it does Windows' words.
- **The switch:** `-speech <name>` on the command line picks the adapter for
  that run and isn't saved; otherwise the `speech.adapter` setting; otherwise
  `whisper`. Names: `whisper`, `windows`, `stub-transcriber`, and the probe's
  `stand-in` and `recorded`. More flags for a transcriber:
  `-speech-model <file>` (another model), `-speech-gpu` (Whisper on the GPU;
  the default is the CPU, see below),
  `-speech-wav <file>` (hear a WAV file in place of the microphone),
  `-speech-timeout <s>` (default 15), `-speech-stub-seconds <s>` (how long
  the stub works, default 0.5).
- **The timeout is ours.** Nothing on the main thread waits on the
  transcriber. Past the timeout its token is cancelled, the work is left to
  end by itself, and the hold ends with "Making out the words took longer
  than N s ... Try again, or type." The next press starts fresh at once.
- **The stub** keeps its worker thread busy for the set time (real work, not
  a sleep), then says the WAV file's name with dashes as spaces, or "stub
  heard N seconds" from the microphone, or nothing for silence. Only an
  Editor or a development build can pick it: a release gets a line saying
  it's for testing.
- **Logs:** every new trace line goes through `VoiceTrace.Words`, so a
  release logs how much was said, never what (`VoiceTraceTests` reads the
  new files too). The check shows the words on screen.

## Trying it (Andrew, two minutes)

With `-speech whisper` (or nothing: it's the default) any player will do. The
stub needs a **development build** (a release says the stub is for testing). On the PC, through the
Editor (`script eval`, or a menu item if you add one):
`PrototypeBuild.Build(BuildTarget.StandaloneWindows64, "Builds/SpeechCheck/SpellChemistryValley.exe", BuildOptions.Development)`;
on the Mac, Prototype > Build > macOS (profiling) is a development build.
From the repo, with the player's path in front:

```sh
# The Mac's sample phrases (spoken by `say`), once:
tools/voice-samples/make.sh

# From a file: no key, no microphone
SpellChemistryValley -speech-check -speech stub-transcriber -speech-wav tools/voice-samples/out/make-the-deer-three-times-bigger.wav -speech-stub-seconds 1.5

# From the default microphone (Windows, or the Editor): press Space (or the pad's bottom button), talk for 4 s
SpellChemistryValley -speech-check 4 -speech stub-transcriber
```

A Mac player has the microphone too: Player Settings' Microphone Usage
Description is "Hold to talk to Claude in the game", which macOS shows when
it asks the player to allow it. The first hold is when it asks.

On Windows it's `SpellChemistryValley.exe` with the same flags, and a WAV
from `tools/voice-samples/make.ps1`. The check waits a second to measure
idle frames, records, transcribes, shows the words on screen for 4 s and
quits. Read the result from the player log:

```
SPEECH CHECK the stub transcriber (no model) from make-the-deer-three-times-bigger.wav: done, "make the deer three times bigger" (32), 2.0 s of sound; open 0 ms, record 0 ms, transcribe 1507 ms, total 1509 ms; frames idle mean 14.1 ms, worst 23.2 ms over 72, transcribing mean 13.1 ms, worst 20.8 ms over 115
```

A development Mac player, 2026-11-03, from `make-the-deer-three-times-bigger.wav`
with the stub working 1.5 s: transcribe 1504 ms, total 1509 ms; frames while
transcribing mean 4.5 ms, worst 13.3 ms over 336. The idle second right after
the scene loads had a 455 ms frame, which is loading, not the check. A
release player with the same flags logs `SPEECH CHECK no transcriber here`.

`open` is how long the microphone took to start, `record` the hold,
`transcribe` the model's time, `total` from press to words. A worst frame
while transcribing close to the idle one means no hitch. In the game itself,
`-speech stub-transcriber` makes hold to talk (H) go through the stub.

In the Editor: `tools/probe.sh transcribe` runs the same check from WAV
files (and `--only microphone` the microphone, see below).

## Whisper

- **The model isn't in git.** `StreamingAssets` is ignored. Every player build
  runs `WhisperModelIntoBuild`, which fetches `ggml-base.en.bin` (148 MB) from
  whisper.cpp's model page into `StreamingAssets/Whisper` once and checks its
  SHA-256; a build that can't get it fails. Prototype > Fetch Whisper Model
  does the same for an Editor. A player with no model gets a line saying so,
  and typing still works. The player grows by the model plus the native
  libraries (Windows about 22 MB, of which `ggml-vulkan.dll` is 20).
- **Why base.en.** On a Mac (M2 Pro, CPU, 4 threads), ten phrases spoken by
  `say`: base.en 10 of 10, about 385 ms a phrase; tiny.en 10 of 10, about
  200 ms, 78 MB. Both are fast enough, and `say` is far cleaner than a headset
  in a room, so the bigger model is the safer pick. Real voices haven't been
  measured.
- **The first press** loads the model on the worker thread (about 1.1 s here),
  so it takes about 1.6 s; the next take about 0.4 s.
- **CPU by default.** A native GPU failure would take the game down, and the
  CPU is already fast enough. `-speech-gpu` is there to compare.
- **A cancel can't stop whisper once it's going.** The package hides
  `abort_callback`; set by reflection it only took effect after the encoder,
  nearly all of the work (cancelled at 30 ms, a 384 ms call ran 359 ms, then
  whisper returned -6), so that was taken out. A cancelled call runs to its
  end and its words are dropped, and a press behind it waits on the worker
  thread. The main thread never waits, so a slow PC gets the timeout's line,
  not a freeze.
- **Logs:** the package prints what it heard at its default level, so
  `WhisperTranscriber` turns it down.
- **Load-time needs on Windows:** `libwhisper.dll` links `ggml.dll`, which
  links `ggml-vulkan.dll`, which links `vulkan-1.dll`; they also need
  `MSVCP140`, `VCRUNTIME140`, `VCRUNTIME140_1` and `VCOMP140`. A PC missing any
  fails at the first press with the failed line (and the DLL's name in the
  trace), and typing still works.
- **Linux (Steam Deck):** the package has Linux x86_64 libraries and the path
  has nothing Windows-only, but nothing has run there. They link
  `libvulkan.so.1` and `libgomp.so.1`.

In the Editor, `tools/probe.sh transcribe --only whisper` runs the real model:
words come back, the next press works, a timeout and the press after it, a
cancelled call, and every model it finds (the build's, plus any in
`~/.cache/spell-chemistry-valley/whisper`) over `tools/voice-samples`.

## Known problem: opening the microphone on the Mac

On this Mac, in the hidden Editor, `Microphone.Start` blocked the main thread
inside CoreAudio twice (2026-11-03): for 450 s, then it failed with "Error
initializing output device (60)"; the second time for 391 s, then it opened.
Closing it (the clip's cleanup leaving Play) blocked for minutes too. Unity's `Microphone` is main-thread
only, so the open can't simply move off it. Not yet known: whether a
microphone permission prompt was waiting (none was seen), whether it's this
Mac's sound server, or whether a player behaves the same. A player on each
system still has to show the open and close are quick: Whisper is the default
now, and this is the one main-thread wait left in a hold. That's why the probe's microphone step only runs with
`--only microphone`.
