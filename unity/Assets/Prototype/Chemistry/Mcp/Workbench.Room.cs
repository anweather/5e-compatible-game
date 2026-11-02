// The room tools (docs/RULES_MCP.md phase 3): apply, revert and room. In the game
// they go to its own world through an IRoomDesk (GameRoomDesk): there a change goes
// out as a room command in a tick batch and every peer decides it (World.Room.cs).
// With no desk they run against a world in memory with a room open on it: the command
// goes straight in and the world steps one tick, which is what a solo game does.
using System.Collections.Generic;
using System.Linq;
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

        StandInValley standIn;
        World live;
        World Live => live ??= NewRoom(ChangeMode.Open);

        World NewRoom(ChangeMode mode)
        {
            // Where the world files are there, the room is on a stand-in of the valley (StandInValley), so the world tools have a world.
            standIn ??= StandInValley.Load(RulesDir, Budgets) ?? StandInValley.None;
            World w;
            if (standIn == StandInValley.None) w = new World(Base, 64, 64, 1);
            else
            {
                w = new World(Base, standIn.W, standIn.H, 1);
                w.MakeFrom(standIn);
                w.KeepStart();
            }
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
                Refresh();
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

        /// <summary>
        /// Told of each patch going to the room, by the key apply was given (chat telemetry: apply's input is only a
        /// draft's id, so this is the record's one source for the patch). On whichever thread the tool runs.
        /// </summary>
        public System.Action<string, string> Applied { get; set; }

        /// <summary>Switches a draft, a mod in rules/mods or patch JSON on in the room, as <paramref name="peer"/> (the host when left out).</summary>
        public string Apply(string draftOrMod, string peer = null, string title = null)
        {
            var key = Required(draftOrMod, "draft, mod or patch");
            var json = PatchJson(key);
            if (!drafts.TryGetValue(key, out var draft))
            {
                Applied?.Invoke(key, json);
                return Decide(RoomCommand.Apply(PeerOf(peer), key.StartsWith('{') ? IdOf(json) : key, json, title));
            }
            if (string.IsNullOrWhiteSpace(title)) throw new RulesToolException("A draft needs a title, the name players read in the log; apply it again with one.");
            var slug = Slug(title);
            // Read before it goes in: after, the draft's own values are what's in force.
            string changes = Changes(draft) + NewSpellsReach(draft);
            Applied?.Invoke(key, json);
            string said = Decide(RoomCommand.Apply(PeerOf(peer), slug.Length > 0 ? slug : draft.Id, json, title.Trim()));
            return Refused(said) ? said : said + "\n" + changes;
        }

        /// <summary>What a draft does, in a sentence: each entry it holds against the rules in force.</summary>
        public string Changes(Draft draft) => "It changes " + string.Join("; ", draft.Changed.Select(c => ChangeOf(draft, c))) + ".";

        string NewSpellsReach(Draft draft) =>
            string.Concat(draft.Changed.Where(c => c.StartsWith("spells ", System.StringComparison.Ordinal)).Select(c => c.Substring("spells ".Length))
                .Where(id => EntryIn(Raw, "spells", id) == null).Select(id => "\n" + ReachOf(draft, id)));

        string ChangeOf(Draft draft, string changed)
        {
            int space = changed.IndexOf(' ');
            string table = changed.Substring(0, space), id = changed.Substring(space + 1);
            if (table == "sim") return $"{id} from {Compact(Raw["sim"].SelectToken(id))} to {Compact(draft.Patch["sim"]?.SelectToken(id))}";
            if (table == BuildingsTable) return BuildingChange(id);
            if (BuildingTables.Tables.Contains(table)) return $"a {Single(table)} row, {id}";
            if (table == ThingsTable) return ThingChange(draft, id);
            if (table == "reactions")
            {
                var rule = (draft.Patch["reactions"]?["add"] as JArray ?? new JArray()).Concat(draft.Patch["reactions"]?["addEntity"] as JArray ?? new JArray()).FirstOrDefault(r => Str(r["id"]) == id);
                var before = EntryIn(Raw, table, id);
                return before == null || rule == null ? $"a rule, {id}" : $"the rule {id}: {string.Join(", ", Differences("", before, Rules.MergedEntry(before, rule)))}";
            }
            var (was, now) = table switch
            {
                "spells" => (Raw["spells"].FirstOrDefault(s => Str(s["id"]) == id), draft.Patch["spells"]?.FirstOrDefault(s => Str(s["id"]) == id)),
                "elements" => (Doc["elements"].Any(e => Str(e) == id) ? ElementEntry(id) ?? new JObject() : null, (draft.Patch["elements"] as JObject)?[id]),
                _ => (EntryIn(Raw, table, id), draft.Patch[table]?[id]),
            };
            if (!(now is JObject entry)) return changed;
            return EntryChange(table, id, was, Rules.MergedEntry(was, entry));
        }

        // Field by field, down through groups: "burn.fuel from 8 to 4".
        static IEnumerable<string> Differences(string path, JToken was, JToken now)
        {
            if (now is JObject group && was is JObject before)
                return group.Properties().Select(p => p.Name).Union(before.Properties().Select(p => p.Name))
                    .SelectMany(k => Differences(path.Length == 0 ? k : path + "." + k, before[k], group[k]));
            return JToken.DeepEquals(was, now) ? Enumerable.Empty<string>() : new[] { $"{path} from {Compact(was)} to {Compact(now)}" };
        }

        /// <summary>Reverts a mod on in the room (by its id, as room lists it), as <paramref name="peer"/> (the host when left out).</summary>
        public string Revert(string mod, string peer = null)
        {
            string held = StillHeld(Required(mod, "mod"));
            string said = Decide(RoomCommand.Revert(PeerOf(peer), mod.Trim()));
            return Refused(said) ? said : said + held;
        }

        string Decide(RoomCommand command)
        {
            if (Desk != null) return Desk.Decide(command);
            var verdict = Live.ApplyRoom(command);
            Live.Step(); // in force from the next tick
            Refresh();
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

        /// <summary>The world in memory the tools act on, when no game is attached.</summary>
        public World LiveWorld => Live;

        /// <summary>What LiveWorld was made from, for its save; null for a world of plain dirt.</summary>
        public IChunkSource LiveSource => standIn == StandInValley.None ? null : standIn;
    }
}
