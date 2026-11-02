Brief from the PM: talk to Claude in the game by voice, Windows first, built so Whisper plugs in later (J-061). PC, in the Editor.

Andrew: "go for the Windows route first ... then we'll move to whisper after that ... a good seam for us to unplug the windows side and plug in the whisper side ... like a port and adapters way to do this."

Branch `game-voice` off dev.
1. **The port.** A small `ISpeech` the game knows, with no Windows or Unity recogniser types in it: start and stop listening, partial and final text, and a state (listening, unavailable and why). Text goes into `GameChat.Type`. Nothing outside the adapter may name Windows or the engine. An adapter is picked in one place, with a factory and a settings value.
2. **The Windows adapter.** Unity's `DictationRecognizer`, behind `#if` so other platforms build. If Windows' online speech setting is off, the chat says so in one plain line, with where to turn it on. Tell the player once that Windows sends audio to Microsoft.
3. **A stand-in adapter** for the probe, proving the port works with no Windows in it. This is the seam's test: if Whisper needed a change outside its adapter, the seam is wrong.
4. **Push to talk.** A key, and a pad button or chord. Shows it's listening and the words as they come; the box fills, and letting go sends. A pad player can now use chat. Say what's bound in rebinds.
5. **Record what Whisper's adapter needs** (the audio format from Unity's `Microphone`, threading) in a short note in the code or `docs/design/world-tools.md`. Don't build it.

Window tests only when Andrew says he's off the PC; a stand-in needs none. Check `git diff origin/dev...HEAD --stat` before pushing. Gates, a `voice` probe, a new test player, a clip if it can show anything real. Real dictation needs his voice: leave that to him and say so. Report; push, not merged.
