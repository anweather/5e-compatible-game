// What the world asks of its modes as it runs (D22, docs/design/minigames.md; docs/design/architecture-review.md step 9).
// The Hold, its progress, the defense field and the Gauntlet's course are Chem.Play's (PlayModes), with their state, its
// save and its text: the world keeps none of it, and calls the installed modes, in install order, where it always called
// the Hold's systems. Each tick in the Rest stage after the raiders; a combo's Moment; the defense and course commands;
// a spell zone against a cast; the stands a mode reads; and what a run holds for a wizard's hands (what's held back and
// open, points brought, the kit, an attuning barred, the upgrades picked, a pick). Their state keeps its places in the
// save and the checksums: the save's head keeps "defense" (null with no defense field), and the defense field's own hash
// is "-" until one writes.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        public bool HasModes => installed.Count > 0;

        void TickModes()
        {
            foreach (var m in installed) m.Tick();
        }

        bool StartMoment(string id)
        {
            foreach (var m in installed)
                if (m.StartMoment(id)) return true;
            return false;
        }

        public void ApplyDefense(DefenseCommand command)
        {
            foreach (var m in installed)
                if (m.Apply(command)) return;
            if (command.Kind == DefenseKind.Start) throw new InvalidOperationException("This world has no defense field to start: install its modes (PlayModes.Install)");
            FinishTick();
        }

        public void ApplyCourse(CourseCommand command)
        {
            foreach (var m in installed)
                if (m.Apply(command)) return;
            FinishTick();
        }

        SpellZone ZoneAgainst(SpellDef s, int x, int y)
        {
            foreach (var m in installed)
                if (m.ZoneAgainst(s, x, y) is { } zone) return zone;
            return null;
        }

        public bool ReadsStand(int x, int y) => installed.Any(m => m.ReadsStand(x, y));

        bool KitFor(string player) => installed.Any(m => m.KitFor(player));

        bool AttuneBarred(string player, string element) => installed.Any(m => m.AttuneBarred(player, element));

        IEnumerable<UpgradeDef> PickedBy(string player) => installed.SelectMany(m => m.UpgradesOf(player));

        bool Bring(string player, int points)
        {
            foreach (var m in installed)
                if (m.Bring(player, points)) return true;
            return false;
        }

        bool Pick(string player, string upgrade)
        {
            foreach (var m in installed)
                if (m.Pick(player, upgrade)) return true;
            return false;
        }

        void SaveModesAt(SavePlace place, JObject save)
        {
            foreach (var m in installed) m.SaveAt(place, save);
        }

        string ModesTextAt(TextPlace place)
        {
            if (installed.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var m in installed) sb.Append(m.TextAt(place));
            return sb.ToString();
        }

        public string DefenseHash()
        {
            string text = ModesTextAt(TextPlace.Field);
            return Fnv1a.Hash(text.Length == 0 ? "-" : text);
        }
    }
}
