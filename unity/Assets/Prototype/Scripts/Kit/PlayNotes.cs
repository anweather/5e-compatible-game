using System;

// A note for the play log from below it (docs/design/architecture-review.md step 10): a feature that must not name the
// shell (the chat's telemetry) notes a line here, and the play log, while there is one, takes it as its own Note.
public static class PlayNotes
{
    public static Action<string> To { get; set; }

    public static void Note(string what) => To?.Invoke(what);
}
