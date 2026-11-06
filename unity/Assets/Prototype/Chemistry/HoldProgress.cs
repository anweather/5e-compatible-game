// Progress in the Hold (docs/design/the-hold.md, phase 5): the rows of upgrades a wizard picks inside a run, the rows of unlocks it
// keeps between runs, and the player's own save. Progress belongs to the player, not the world (J-038): the save holds points, and
// what's unlocked follows from those and the rows, so it can't disagree with the rules and a peer needs only the one number.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A row of rules/hold.json "upgrades". Percents on a number the wizard has; health is points.</summary>
    public sealed class UpgradeDef
    {
        public string Id, Name;
        /// <summary>What it touches: a hand holding this, and this gesture; null and NoGesture for anything.</summary>
        public string Holds;
        public int Gesture = HandsDef.NoGesture;
        public int Cooldown, BothCooldown, Damage, Range, Health;
    }

    /// <summary>A row of rules/hold.json "unlocks": one thing a wizard keeps, and the points it costs after the row before.</summary>
    public sealed class UnlockDef
    {
        public string Id, Name, Hand, Element, Kit;
        public int Cost, Gesture = HandsDef.NoGesture;
    }

    /// <summary>One wizard's run: what it's offered now, and what it has picked. Ids only.</summary>
    public sealed class RunWizard
    {
        public string Player;
        /// <summary>The upgrades on offer; empty once it's picked.</summary>
        public List<string> Offer { get; } = new List<string>();
        public List<string> Picks { get; } = new List<string>();
    }

    /// <summary>
    /// A player's progress on their own machine. A save that won't load is kept beside it, the last good copy is used if there
    /// is one, and only then does the player start again: and the file is never written lower than it was.
    /// </summary>
    public sealed class ProgressFile
    {
        public const string FileName = "progress.json";
        const string Backup = ".bak", Broken = ".broken", Fresh = ".new";
        const int Version = 1;

        readonly string path;

        /// <summary>The player's points.</summary>
        public int Points { get; private set; }
        /// <summary>What went wrong loading, for the player to hear; null if nothing did.</summary>
        public string Trouble { get; private set; }

        public ProgressFile(string folder) => path = Path.Combine(folder, FileName);

        /// <summary>Reads the save, or failing that its last good copy.</summary>
        public void Load()
        {
            Trouble = null;
            if (!File.Exists(path) && !File.Exists(path + Backup)) return;
            if (Read(path, out int points))
            {
                Points = points;
                return;
            }
            if (File.Exists(path)) File.Copy(path, Unused(path + Broken));
            if (Read(path + Backup, out points))
            {
                Points = points;
                Trouble = "Your progress save wouldn't load: the last good copy was used, and the bad one kept.";
                return;
            }
            Trouble = "Your progress save wouldn't load, and it's been kept beside the new one.";
        }

        /// <summary>Writes this many points, if it's more: a whole new file put in place, with the last one kept as the copy.</summary>
        public void Save(int points)
        {
            if (points <= Points) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + Fresh, new JObject { ["version"] = Version, ["points"] = points }.ToString());
            if (File.Exists(path) && Read(path, out _)) File.Copy(path, path + Backup, true);
            File.Copy(path + Fresh, path, true);
            File.Delete(path + Fresh);
            Points = points;
        }

        static bool Read(string file, out int points)
        {
            points = 0;
            try
            {
                if (!File.Exists(file)) return false;
                var o = JObject.Parse(File.ReadAllText(file));
                if ((int?)o["version"] != Version || !(o["points"] is JValue v) || v.Type != JTokenType.Integer) return false;
                points = (int)v;
                return points >= 0;
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is OverflowException || e is InvalidCastException)
            {
                return false;
            }
        }

        // The first free name: bad saves are never overwritten.
        static string Unused(string name)
        {
            string file = name;
            int n = 1;
            while (File.Exists(file)) file = name + (n++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return file;
        }
    }
}
