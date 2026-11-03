// Reading bodies (bodies-and-surfaces.md sections 1 and 3): list parts, and describe for a
// species or a part. A species' slots come flattened, each copy where it sits at rest, and the
// body in a line of words, since headless Claude can't see one. What a variant takes from the
// species it's built on is said as that species', not as its own row's.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        const string PartsTable = FaunaTables.PartsKey;

        // list fauna: the name, how it's built and whether it comes out on its own.
        List<string> SpeciesLines()
        {
            var fauna = InForce.Fauna;
            return fauna.SpeciesIds.Select(id =>
            {
                var s = fauna.Species(id);
                string built = s.VariantOf != null ? $"a variant of {s.VariantOf}" : $"{s.Slots.Count} slots";
                string where = s.Among != null ? $"among {s.Among}" : $"{Num(s.Density)} a hectare";
                if (!s.Spawns) where = "only where placed";
                return $"{id}: {s.Name}, {built}, {where}";
            }).ToList();
        }

        // list parts: each part's role, its length, and who wears it.
        List<string> PartLines()
        {
            var fauna = InForce.Fauna;
            var wearers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var id in fauna.SpeciesIds)
                foreach (var slot in fauna.Species(id).Slots.Where(s => s.Part != null))
                {
                    if (!wearers.TryGetValue(slot.Part, out var list)) wearers[slot.Part] = list = new List<string>();
                    if (!list.Contains(id)) list.Add(id);
                }
            return fauna.PartIds.Select(id =>
            {
                var p = fauna.Part(id);
                var by = wearers.TryGetValue(id, out var w) ? w : new List<string>();
                string more = by.Count > 4 ? $" and {by.Count - 4} more" : "";
                string worn = by.Count == 0 ? "worn by none" : $"worn by {string.Join(", ", by.Take(4))}{more}";
                return $"{id} ({p.Role}): {Count(p.Segments.Count, "segment")}, {Num(Math.Round(p.Segments.Sum(s => s.Length), 2))} m; {worn}";
            }).ToList();
        }

        // describe fauna: the body in words, then each slot flattened, then what a variant takes from its body.
        string SpeciesNote(string id, Draft draft)
        {
            var fauna = (draft == null ? InForce : RulesWith(draft.Patch.ToString())).Fauna;
            var s = fauna.Species(id);
            var body = fauna.BodyOf(id);
            if (s == null || body == null) return "";
            double size = (body.Size[0] + body.Size[1]) / 2 * (s.VariantOf != null ? s.VariantScale : 1);
            var shape = BodyShape.Of(fauna, body);
            var lines = new List<string> { $"\nShape: {shape.Words(size)} (at rest, an animal of middle size)." };
            if (s.VariantOf != null)
                lines.Add($"A variant of {s.VariantOf}: its body, slots and gait are {body.Id}'s, and it wanders {Num(body.Wander)} m, flees at {Num(body.Flee)} m, idles {Span(body.Idle)} s and keeps in groups of {Span(body.Group)} as {body.Id} does (set those on {body.Id}). Its own: size ×{Num(s.VariantScale)}, coat, hair, active {s.Active}, graze {Num(s.Graze)}, thirst {Num(s.Thirst)}, play {Num(s.Play)}, laze {Num(s.Laze)}, hunger {Num(s.Hunger)}, chase {Num(s.Chase)}.");
            if (s.Grafted.Count > 0)
                lines.Add($"A graft, flattened from {string.Join(", ", s.Grafted)}: the slots below are what it's built of; a slot whose id was taken has its donor's id before it, and coat own wears that donor's coats and hair.");
            lines.Add($"Body: {Num(body.Length)} m long, radius {Num(body.Radius)} m, taper {Num(body.Taper)}, flatten {Num(body.Flatten)}; " +
                "a body slot's at is in body units (x across the radius, y up the radius, z along the half-length, +1 the front); a slot on a slot is at metres from the end of that one.");
            lines.Add($"Slots, flattened ({shape.Limbs.Count} drawn, metres at rest before size, x right, y up, z ahead):");
            foreach (var limb in shape.Limbs) lines.Add(LimbLine(limb, shape));
            var missing = body.Slots.Where(slot => fauna.Part(slot.Part) == null).Select(slot => $"{slot.Id} ({slot.Part})").ToList();
            if (missing.Count > 0) lines.Add($"Not drawn, no such part: {string.Join(", ", missing)}.");
            return string.Join("\n", lines);
        }

        static string LimbLine(BodyShape.Limb limb, BodyShape shape)
        {
            var slot = limb.Slot;
            string hand = limb.Mirrored ? " (left)" : " (right)";
            string name = slot.Id + (slot.Mirror ? hand : "");
            string on = limb.Parent < 0 ? $"on the body at [{string.Join(", ", slot.At.Select(Num))}] body units" : $"on {shape.Limbs[limb.Parent].Slot.Id}, [{string.Join(", ", slot.At.Select(Num))}] m from its end";
            string option = limb.Shown ? "" : " (another option)";
            string extra = (slot.Dir.Any(d => Math.Abs(d) > 1e-9) ? $", dir [{string.Join(", ", slot.Dir.Select(Num))}]" : "") +
                (Math.Abs(slot.Scale - 1) > 1e-9 ? $", scale {Num(slot.Scale)}" : "") + (slot.Style > 0 ? $", style {slot.Style}{option}" : "") + (slot.OwnCoatOf != null ? $", coat own ({slot.OwnCoatOf})" : "");
            return $"- {name}: {slot.Part} ({limb.Part.Role}), {on}{extra}; from {BodyShape.Metres(limb.Start)} to {BodyShape.Metres(limb.End)}";
        }

        // describe parts: the segments and every slot that hangs it.
        string PartNote(string id)
        {
            var fauna = InForce.Fauna;
            var p = fauna.Part(id);
            if (p == null) return "";
            var segs = p.Segments.Select(s => $"{Num(s.Length)} m (radius {Num(s.RadiusStart)} to {Num(s.RadiusEnd)} m)");
            var slots = fauna.SpeciesIds.SelectMany(sp => fauna.Species(sp).Slots.Where(s => s.Part == id).Select(s => $"{sp}.{s.Id}")).ToList();
            return $"\nRole {p.Role}: {string.Join(", then ", segs)}." +
                (slots.Count == 0 ? "\nNo species wears it." : $"\nWorn in {Count(slots.Count, "slot")}: {string.Join(", ", slots)}.");
        }

        static string Span(double[] r) => r.Length == 2 && Math.Abs(r[0] - r[1]) > 1e-9 ? $"{Num(r[0])} to {Num(r[1])}" : Num(r[0]);

        static string Count(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";
    }
}
