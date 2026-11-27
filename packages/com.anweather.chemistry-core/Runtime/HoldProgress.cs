// Progress in the Hold (docs/design/the-hold.md, phase 5): the rows of upgrades a wizard picks inside a run, the rows of unlocks it
// keeps between runs, and the player's own save. Progress belongs to the player, not the world (J-038): the save holds points, and
// what's unlocked follows from those and the rows, so it can't disagree with the rules and a peer needs only the one number.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed class UpgradeDef
    {
        public string Id, Name;
        public string Holds;
        public int Gesture = HandsDef.NoGesture;
        public bool BothHands;
        public int Cooldown, BothCooldown, Damage, Range, Health;

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

    public sealed class UnlockDef
    {
        public string Id, Name, Hand, Element, Kit;
        public int Cost, Gesture = HandsDef.NoGesture;

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

    public sealed class RunWizard
    {
        public string Player;
        public List<string> Offer { get; } = new List<string>();
        public List<string> Picks { get; } = new List<string>();
    }

    public sealed class ProgressFile
    {
        public const string FileName = PlayerSave.FileName;

        readonly PlayerSave save;

        public int Points => save.Points;
        public string Trouble => save.Trouble;

        public ProgressFile(string folder) => save = new PlayerSave(folder);

        public void Load() => save.Load();

        public void Save(int points)
        {
            if (save.Raise(points)) save.Save();
        }
    }
}
