using Chem.Mcp;

// The chat's progress bar: what a prompt has reached, as a share of the way and a line for a player.
// It runs on tools, since a run's first words often come last (docs/telemetry/chatbench-2026-11-02.md).
// How many tools a run will use isn't known, so each one closes part of the gap that's left.
public static class ChatBar
{
    const float Sent = 0.05f, Started = 0.1f, Ready = 0.2f, ToolsReach = 0.7f, Applying = 0.8f, Applied = 0.9f, Finishing = 0.95f;
    const float ToolsHalfway = 3f;

    /// <summary>How far a prompt has got; null with none going.</summary>
    public static (float done, string what)? Of(ChatRecorder recorder)
    {
        if (recorder == null) return null;
        var now = recorder.Progress();
        if (now.InWorld) return now.Done ? (1f, "Done") : (Finishing, "Finishing");
        if (now.Applied) return (Applied, "Drawing the change");
        if (now.Applying) return (Applying, "Changing the world");
        if (now.Done) return (Finishing, "Finishing");
        if (now.ToolsDone > 0 || now.Tools > 0) return (Working(now.ToolsDone), "Working on it");
        if (now.Ready) return (Ready, "Thinking");
        return now.Started ? (Started, "Starting Claude") : (Sent, "Sending");
    }

    static float Working(int tools) => Ready + (ToolsReach - Ready) * tools / (tools + ToolsHalfway);
}
