using System;
using System.Threading;
using Chem;
using Chem.Mcp;
using UnityEngine;

// What the rules MCP's room tools do in a running game: they cross to the main thread,
// go in as this player's room commands (RuleSwitcher, as the mods panel does), and wait
// for the room's verdict, so Claude hears what the players see. Called off the main
// thread (RulesMcpServer). Whoever the tool says is asking, it's this player.
public class GameRoomDesk : IRoomDesk
{
    const int VerdictMs = 8000;

    readonly RulesMcpServer server;

    public GameRoomDesk(RulesMcpServer server) => this.server = server;

    // Runs on the main thread and waits for its answer; a game that has stopped says so.
    string OnMainThread(Func<Action<string>, string> act)
    {
        string answer = null;
        using var done = new ManualResetEventSlim();
        server.Post(() =>
        {
            void Finish(string text)
            {
                answer = text;
                done.Set();
            }
            string now = act(Finish);
            if (now != null) Finish(now);
        });
        return done.Wait(VerdictMs) ? answer : "The game didn't answer in time; ask for the room to see whether it went in.";
    }

    static RuleSwitcher Switcher => UnityEngine.Object.FindAnyObjectByType<RuleSwitcher>();

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
        var mine = command.Change == RoomChange.Revert
            ? RoomCommand.Revert(switcher.Me, command.Mod)
            : RoomCommand.Apply(switcher.Me, command.Mod, command.Json, command.Title);
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
