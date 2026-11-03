using System.Collections.Generic;
using Chem;
using Punchy.Fauna;

// Every species' body, built from the rules in force and kept by index: an animal
// holds its species' index, so swapping a body in keeps it where it is. A
// switch of rules rebuilds only the species whose rows changed, and a species a
// switch takes away leaves a null, so the indices stay what they were. What it
// hunts and what hunts it are set afresh each time, on the bodies as they stand.
public sealed class CreatureBodies
{
    readonly List<CreatureBody> bodies = new();
    readonly Dictionary<string, int> indices = new();
    FaunaTables tables;

    public int Count => bodies.Count;

    /// <summary>How many bodies the last <see cref="Refresh"/> built.</summary>
    public int Rebuilt { get; private set; }

    /// <summary>A species' body by index, or null.</summary>
    public CreatureBody this[int index] => index >= 0 && index < bodies.Count ? bodies[index] : null;

    /// <summary>A species' index by its id, or -1.</summary>
    public int IndexOf(string id) => id != null && indices.TryGetValue(id, out int i) ? i : -1;

    /// <summary>
    /// Who comes out of a door of a village of the species at <paramref name="kind"/>: them, or one of the
    /// species whose row says it comes out among them, by the share it gives. <paramref name="roll"/> is from 0 to 1.
    /// </summary>
    public int FolkAt(int kind, float roll)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b == null || b.Among != bodies[kind].Id) continue;
            roll -= b.Share;
            if (roll < 0f) return i;
        }
        return kind;
    }

    /// <summary>Brings the bodies up to these rules.</summary>
    public void Refresh(Rules rules)
    {
        var next = rules.Fauna;
        var changedParts = tables != null ? next.ChangedParts(tables) : null;
        Rebuilt = 0;
        var kept = new HashSet<string>();
        foreach (var id in next.SpeciesIds)
        {
            kept.Add(id);
            if (!indices.TryGetValue(id, out int i))
            {
                i = bodies.Count;
                indices[id] = i;
                bodies.Add(null);
            }
            if (bodies[i] != null && tables != null && Same(next, bodies[i], changedParts)) continue;
            bodies[i] = CreatureBody.Build(next, id);
            Rebuilt++;
        }
        foreach (var (id, i) in indices)
            if (!kept.Contains(id)) bodies[i] = null;
        tables = next;
        SetHunting();
    }

    // Whether a body's row, and the rows of the donors whose colours a graft wears ("coat": "own"), are as they were.
    bool Same(FaunaTables next, CreatureBody body, ISet<string> changedParts)
    {
        if (!next.SameRows(tables, body.Id, changedParts)) return false;
        foreach (var donor in body.Own)
            if (!next.SameRows(tables, donor.Id, changedParts)) return false;
        return true;
    }

    // What each species hunts (any variant of what it lists counts), and whether anything out there hunts it.
    void SetHunting()
    {
        var hunted = new bool[bodies.Count];
        var prey = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            var hunter = bodies[i];
            if (hunter == null) continue;
            prey.Clear();
            foreach (var listed in hunter.Hunts)
            {
                var kind = bodies[indices[listed]]?.Kind;
                for (int j = 0; j < bodies.Count; j++)
                {
                    if (j == i || bodies[j] == null || bodies[j].Kind != kind || prey.Contains(j)) continue;
                    prey.Add(j);
                    if (hunter.Spawns) hunted[j] = true;
                }
            }
            prey.Sort();
            hunter.Prey = prey.ToArray();
        }
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i] != null) bodies[i].Hunted = hunted[i];
    }
}
