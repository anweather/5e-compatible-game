# The speech check, and where Whisper plugs in (#58)

Status, 2026-11-03: scaffolding only, on branch `whisper-spike`. There is no
Whisper model or plugin in the repo yet: downloading one hasn't been cleared.
The **stub transcriber** stands in for it, so everything around the model
(recording, the worker thread, the timeout, the hand-off to the chat) is
built and tested now. Windows dictation is still the default and is
unchanged.

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
  `windows`. Names: `windows`, `stub-transcriber`, and the probe's
  `stand-in` and `recorded`. More flags for a transcriber:
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

The check needs a **development build** while the stub is the only
transcriber (a release says the stub is for testing). On the PC, through the
Editor (`script eval`, or a menu item if you add one):
`PrototypeBuild.Build(BuildTarget.StandaloneWindows64, "Builds/SpeechCheck/SpellChemistryValley.exe", BuildOptions.Development)`;
on the Mac, Prototype > Build > macOS (profiling) is a development build.
From the repo, with the player's path in front:

```sh
# The Mac's sample phrases (spoken by `say`), once:
tools/voice-samples/make.sh

# From a file: no key, no microphone
SpellChemistryValley -speech-check -speech stub-transcriber -speech-wav tools/voice-samples/out/make-the-deer-three-times-bigger.wav -speech-stub-seconds 1.5

# From the default microphone: press Space (or the pad's bottom button), talk for 4 s
SpellChemistryValley -speech-check 4 -speech stub-transcriber
```

On Windows it's `SpellChemistryValley.exe` with the same flags, and a WAV
from `tools/voice-samples/make.ps1`. The check waits a second to measure
idle frames, records, transcribes, shows the words on screen for 4 s and
quits. Read the result from the player log:

```
SPEECH CHECK the stub transcriber (no model) from make-the-deer-three-times-bigger.wav: done, "make the deer three times bigger" (32), 2.0 s of sound; open 0 ms, record 0 ms, transcribe 1507 ms, total 1509 ms; frames idle mean 14.1 ms, worst 23.2 ms over 72, transcribing mean 13.1 ms, worst 20.8 ms over 115
```

`open` is how long the microphone took to start, `record` the hold,
`transcribe` the model's time, `total` from press to words. A worst frame
while transcribing close to the idle one means no hitch. In the game itself,
`-speech stub-transcriber` makes hold to talk (H) go through the stub.

In the Editor: `tools/probe.sh transcribe` runs the same check from WAV
files (and `--only microphone` the microphone, see below).

## What will change with a real model

- A `WhisperTranscriber : ITranscriber` and one `case` in
  `SpeechChoice.Make`. It loads the model on the worker thread at its first
  call (so the first press is slower: the timeout may need to allow for
  that, or the model loads once at start), passes the token to whisper's
  abort callback so a cancel ends it, and serialises its own calls if two
  can't share the model.
- The check's line will name the model, and `transcribe` will be the real
  number to compare (tiny.en against base.en, CPU against GPU).
- The stub's gate stays: Whisper's adapter is meant for releases.
- Files to add once cleared: the `whisper.unity` package (MIT) with its
  native libraries for Windows x64 and macOS, and a `ggml-*.en.bin` model
  in `StreamingAssets` or downloaded on first use. None is in the repo now.

## Known problem: opening the microphone on the Mac

On this Mac, in the hidden Editor, `Microphone.Start` blocked the main thread
inside CoreAudio twice (2026-11-03): for 450 s, then it failed with "Error
initializing output device (60)"; the second time for 391 s, then it opened.
Closing it (the clip's cleanup leaving Play) blocked for minutes too. Unity's `Microphone` is main-thread
only, so the open can't simply move off it. Not yet known: whether a
microphone permission prompt was waiting (none was seen), whether it's this
Mac's sound server, or whether a player behaves the same. Before Whisper is
the default, a player on each system has to show the open and close are
quick, and the project's Microphone Usage Description (Player Settings,
empty now) needs text for macOS. That's why the probe's microphone step only runs with
`--only microphone`.
