using System;
using System.Threading.Tasks;
using Chem;
using Chem.Mcp;
using UnityEngine;

// What the rules MCP's room tools do in a running game: they cross to the main thread,
// go in as this player's room commands (RuleSwitcher, as the mods panel does), and wait
// for the room's verdict, so Claude hears what the players see. Now reads the running
// world the same way, so the tools answer with the rules, clock and weather in force.
// Called off the main thread (RulesMcpServer). Whoever the tool says is asking, it's this player.
public class GameRoomDesk : ILiveDesk, IWorldDesk
{
    const int VerdictMs = 8000;

    readonly RulesMcpServer server;

    public GameRoomDesk(RulesMcpServer server) => this.server = server;

    // Runs on the main thread and waits for its answer; a game that has stopped says so.
    string OnMainThread(Func<Action<string>, string> act)
    {
        var answer = new TaskCompletionSource<string>();
        server.Post(() =>
        {
            string now = act(text => answer.TrySetResult(text));
            if (now != null) answer.TrySetResult(now);
        });
        return answer.Task.Wait(VerdictMs) ? answer.Task.Result : "The game didn't answer in time; ask for the room to see whether it went in.";
    }

    static RuleSwitcher Switcher => UnityEngine.Object.FindAnyObjectByType<RuleSwitcher>();

    public WorldNow Now()
    {
        WorldNow now = null;
        OnMainThread(_ =>
        {
            var switcher = Switcher;
            if (switcher != null) now = WorldNow.Of(switcher.GetComponent<ChemGrid>().World);
            return "";
        });
        return now;
    }

    public T Read<T>(Func<World, T> read)
    {
        T value = default;
        Exception failed = null;
        string said = OnMainThread(_ =>
        {
            var switcher = Switcher;
            if (switcher == null) return NoGame;
            try
            {
                value = read(switcher.GetComponent<ChemGrid>().World);
            }
            catch (RulesToolException e)
            {
                failed = e;
            }
            return "";
        });
        if (failed != null) throw failed;
        if (said.Length > 0) throw new RulesToolException(said);
        return value;
    }

    public string Room(string mode)
    {
        if (!string.IsNullOrWhiteSpace(mode)) throw new RulesToolException("This game's change mode was chosen when its room opened; it can't change now.");
        return OnMainThread(_ =>
        {
            var switcher = Switcher;
            if (switcher == null) return NoGame;
            var grid = switcher.GetComponent<ChemGrid>();
            return Workbench.Describe(switcher.Room, grid.World.Tick) + $"\nYou play as {switcher.Me}.";
        });
    }

    public string Decide(RoomCommand command) => OnMainThread(finish =>
    {
        var switcher = Switcher;
        if (switcher == null) return NoGame;
        var grid = switcher.GetComponent<ChemGrid>();
        var mine = command.As(switcher.Me);
        void Heard(RoomCommand decided, RoomVerdict verdict)
        {
            if (decided.Peer != mine.Peer || decided.Mod != mine.Mod || decided.Change != mine.Change) return;
            grid.RoomDecided -= Heard;
            finish(verdict.Line + ".");
        }
        grid.RoomDecided += Heard;
        switcher.Ask(mine);
        return null;
    });

    public void Saved(string path) => server.Post(() =>
    {
        var switcher = Switcher;
        if (switcher != null) switcher.Catalog.Refresh(path);
    });

    const string NoGame = "There's no world running: the game is between scenes. Try again in a moment.";
}
