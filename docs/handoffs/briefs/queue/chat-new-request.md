Needs: core
Area: the chat's prompt assembly and instructions (GameChat and what it sends Claude: the player's words, the note
list, the room's state), core tests; the Unity side only to say what it must change
Branch: chat-new-request

# "I don't see a new request" to a real request (playtest 2026-11-08 L2; given to u2)

From Andrew's chat records (`chat-log-2026-11-08.jsonl`, model claude-haiku-5-5, voice, two players), the lead's
excerpts:

- prompt "Make it daytime." -> "I don't see a new request in your message, so I haven't changed anything. Right now
  three mods are on ..."
- prompt "Make it daytime." again -> "... The last note in your list (\"UnMake it daytime\") reads as unfinished, and I
  can't tell what you want undone or changed."
- prompt "I can make the centaur three times taller." (voice misheard "make") -> "I don't have a new request from you
  yet ..." (a fair reading). The retry "Make the centaur three times taller." worked.

So Claude saw the player's words as "UnMake it daytime", glued to something before it, inside a "list" of notes,
rather than as this turn's request. Find how the prompt is built (history, the notes list, the player's new words)
and why a request can end up looking like an old note with a prefix stuck to it ("Un" from an earlier undo?). Make
the new request unmistakable: its own field or block, clearly "the player now says", never joined to earlier text.
Add a test that builds the prompt after an undo or a revert and checks the new words arrive whole and marked as new.
Also check the instructions tell Claude to act on the newest words even when they repeat an earlier ask.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in full, `dotnet format`, and
`node --test tools/chat-report.test.mjs` if you touch the record format. Push the branch only, remove this claim on the
branch in the last commit. The PC compiles it and the lead lands it.
