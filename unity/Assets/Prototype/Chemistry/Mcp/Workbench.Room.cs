// The room tools (docs/RULES_MCP.md phase 3): apply, revert and room. In the game
// they go to its own world through an IRoomDesk (GameRoomDesk): there a change goes
// out as a room command in a tick batch and every peer decides it (World.Room.cs).
// With no desk they run against a world in memory with a room open on it: the command
// goes straight in and the world steps one tick, which is what a solo game does.
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>A running game's room, as the tools see it. Called off the game's main thread.</summary>
    public interface IRoomDesk
    {
        /// <summary>Says what the room is, as Workbench.Describe does. A mode there is refused: it was chosen when the room opened.</summary>
        string Room(string mode);

        /// <summary>Sends the command on its way and waits for the room's verdict, as a sentence.</summary>
        string Decide(RoomCommand command);

        /// <summary>A mod was saved to this file.</summary>
        void Saved(string path);
    }

    public sealed partial class Workbench
    {
        /// <summary>The running game's room; null works on a world in memory.</summary>
        public IRoomDesk Desk { get; set; }

        /// <summary>The name this MCP's own player goes by in the room: the host.</summary>
        public const string HostName = "host";

        World live;
        World Live => live ??= NewRoom(ChangeMode.Open);

        World NewRoom(ChangeMode mode)
        {
            var w = new World(Base, 64, 64, 1);
            w.OpenRoom(mode, HostName, Base, Budgets);
            return w;
        }

        /// <summary>The room's change mode and the mods on in it. With a mode, first makes a new room in it (as a room is made).</summary>
        public string Room(string mode = null)
        {
            if (Desk != null) return Desk.Room(mode);
            if (!string.IsNullOrWhiteSpace(mode))
            {
                var m = RoomPolicy.Parse(mode) ?? throw new RulesToolException($"{mode.Trim()} isn't a change mode; a room is open, host-only or locked.");
                live = NewRoom(m);
            }
            return Describe(Live.Room, Live.Tick);
        }

        /// <summary>A room in words: its mode, what that means, and the mods on.</summary>
        public static string Describe(RoomState room, int tick)
        {
            var sb = new StringBuilder($"{RoomPolicy.Words(room.Mode)} room, at tick {tick}; the host is {room.Host}. ");
            sb.Append(room.Mode switch
            {
                ChangeMode.HostOnly => "Only the host's changes go in.",
                ChangeMode.Locked => "No changes go in: the rules stay as the room started.",
                _ => "Anyone's change goes in, and anyone can revert one.",
            });
            if (room.Mods.Count == 0) return sb.Append("\nNo mods on: the base rules.").ToString();
            sb.Append($"\n{room.Mods.Count} mod{(room.Mods.Count == 1 ? "" : "s")} on, in order:");
            for (int i = 0; i < room.Mods.Count; i++)
            {
                var mod = room.Mods[i];
                sb.Append($"\n{i + 1}. {mod.Title} ({mod.Id}), added by {mod.By} at tick {mod.Tick}");
            }
            return sb.ToString();
        }

        /// <summary>Switches a draft, a mod in rules/mods or patch JSON on in the room, as <paramref name="peer"/> (the host when left out).</summary>
        public string Apply(string draftOrMod, string peer = null, string title = null)
        {
            var key = Required(draftOrMod, "draft, mod or patch");
            var json = PatchJson(key);
            var id = key.StartsWith('{') ? IdOf(json) : key;
            // A draft has no title of its own, and players shouldn't read its id.
            if (string.IsNullOrWhiteSpace(title) && drafts.ContainsKey(key)) title = "a new mod";
            return Decide(RoomCommand.Apply(PeerOf(peer), id, json, title));
        }

        /// <summary>Reverts a mod on in the room (by its id, as room lists it), as <paramref name="peer"/> (the host when left out).</summary>
        public string Revert(string mod, string peer = null) => Decide(RoomCommand.Revert(PeerOf(peer), Required(mod, "mod")));

        string Decide(RoomCommand command)
        {
            if (Desk != null) return Desk.Decide(command);
            var verdict = Live.ApplyRoom(command);
            Live.Step(); // in force from the next tick
            return verdict.Line + ".\n" + Room();
        }

        static string PeerOf(string peer) => string.IsNullOrWhiteSpace(peer) ? HostName : peer.Trim();

        // Patch JSON pasted in: its title as an id, else "patch".
        static string IdOf(string json)
        {
            try
            {
                var title = JToken.Parse(json) is JObject o ? o["title"] : null;
                var slug = title?.Type == JTokenType.String ? Slug((string)title) : "";
                return slug.Length > 0 ? slug : "patch";
            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                return "patch";
            }
        }

        /// <summary>The room in memory, as the tools left it.</summary>
        public RoomState RoomNow => Live.Room;
    }
}
