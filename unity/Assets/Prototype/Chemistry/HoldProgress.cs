// Progress in the Hold (docs/design/the-hold.md, phase 5): the rows of upgrades a wizard picks inside a run, the rows of unlocks it
// keeps between runs, and the player's own save. Progress belongs to the player, not the world (J-038): the save holds points, and
// what's unlocked follows from those and the rows, so it can't disagree with the rules and a peer needs only the one number.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A row of rules/hold.json "upgrades". Percents on a number the wizard has; health is points.</summary>
    public sealed class UpgradeDef
    {
        public string Id, Name;
        /// <summary>What it touches: a hand holding this, and this gesture; null and NoGesture for anything.</summary>
        public string Holds;
        public int Gesture = HandsDef.NoGesture;
        /// <summary>Whether it needs both hands ("hands": "both"), so a one-handed wizard can't use it.</summary>
        public bool BothHands;
        public int Cooldown, BothCooldown, Damage, Range, Health;

        /// <summary>What it does, in words for the player: "fire spells do 30% more damage".</summary>
        public string Words()
        {
            string what = (Holds != null ? Holds + " " : "") + (Gesture != HandsDef.NoGesture ? HandsDef.GestureName(Gesture) + "s" : "spells");
            var parts = new List<string>();
            if (Damage != 0) parts.Add($"{what} do {Percent(Damage)} {(Damage > 0 ? "more" : "less")} damage");
            if (Range != 0) parts.Add($"{what} reach {Percent(Range)} {(Range > 0 ? "further" : "less far")}");
            if (Cooldown != 0) parts.Add($"{(Holds == null && Gesture == HandsDef.NoGesture ? "your hands are" : what + " are")} ready {Percent(Cooldown)} {(Cooldown < 0 ? "sooner" : "later")}");
            if (BothCooldown != 0) parts.Add($"two-handed spells are ready {Percent(BothCooldown)} {(BothCooldown < 0 ? "sooner" : "later")}");
            if (Health != 0) parts.Add($"{Math.Abs(Health)} {(Health > 0 ? "more" : "less")} health");
            if (parts.Count == 0) return "nothing yet";
            string words = string.Join(", ", parts);
            return char.ToUpperInvariant(words[0]) + words.Substring(1);
        }

        static string Percent(int value) => Math.Abs(value).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>A row of rules/hold.json "unlocks": one thing a wizard keeps, and the points it costs after the row before.</summary>
    public sealed class UnlockDef
    {
        public string Id, Name, Hand, Element, Kit;
        public int Cost, Gesture = HandsDef.NoGesture;

        /// <summary>What kind of thing it gives, for the player: "a gesture", "a hand", "an element" or "a kit spell".</summary>
        public string Kind
        {
            get
            {
                if (Gesture != HandsDef.NoGesture) return "a gesture";
                if (Hand != null) return "a hand";
                if (Element != null) return "an element";
                return Kit != null ? "a kit spell" : "something";
            }
        }
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
        public const string FileName = PlayerSave.FileName;

        readonly PlayerSave save;

        /// <summary>The player's points.</summary>
        public int Points => save.Points;
        /// <summary>What went wrong loading, for the player to hear; null if nothing did.</summary>
        public string Trouble => save.Trouble;

        public ProgressFile(string folder) => save = new PlayerSave(folder);

        /// <summary>Reads the save, or failing that its last good copy.</summary>
        public void Load() => save.Load();

        /// <summary>Writes this many points, if it's more: the player save, whole, with what else it holds kept.</summary>
        public void Save(int points)
        {
            if (save.Raise(points)) save.Save();
        }
    }
}
