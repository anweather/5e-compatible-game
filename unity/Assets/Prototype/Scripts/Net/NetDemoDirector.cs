using System.Collections;
using UnityEngine;

// One player's part in the scripted 4-player demo. Every player runs its
// own director for its own wizard; all of them count time in sim ticks from
// the same start tick (sent by NetDemo), so the four parts line up on every
// machine. Casts go through the network like a player's, so the chemistry
// in every recording is the same.
[RequireComponent(typeof(SpellCaster))]
public class NetDemoDirector : MonoBehaviour
{
    public static NetDemoDirector Current { get; private set; }
    public bool Done { get; private set; }

    enum Act { Walk, Cast, Look }

    readonly struct Step
    {
        public readonly float At;
        public readonly Act Act;
        public readonly string Spell;
        public readonly int X, Y, Dir;

        Step(float at, Act act, int x, int y, string spell = null, int dir = 0)
        {
            At = at;
            Act = act;
            X = x;
            Y = y;
            Spell = spell;
            Dir = dir;
        }

        public static Step Walk(float at, int x, int y) => new Step(at, Act.Walk, x, y);
        public static Step Look(float at, int x, int y) => new Step(at, Act.Look, x, y);
        public static Step Cast(float at, string spell, int x, int y, int dir) => new Step(at, Act.Cast, x, y, spell, dir);
    }

    const float Length = 16f;

    // Seconds from the start; cells are (column, row) on the yard map. Directions: 0 N, 1 E, 2 S, 3 W.
    static readonly Step[][] Parts =
    {
        new[] // Player 1, the host: lights the oil spill, then watches the meadow burn.
        {
            Step.Look(0f, 18, 5), Step.Walk(0.5f, 16, 11),
            Step.Cast(3.5f, SpellIds.FireBolt, 19, 3, 0),
            Step.Look(6.5f, 10, 11),
        },
        new[] // Player 2: greases the meadow, then sets it alight.
        {
            Step.Look(0f, 9, 10), Step.Walk(0.5f, 11, 13),
            Step.Cast(5f, SpellIds.Grease, 9, 11, 3),
            Step.Cast(7f, SpellIds.FireBolt, 9, 11, 3),
            Step.Look(11.5f, 19, 6),
        },
        new[] // Player 3: freezes a strip of the pond, then shocks the metal strip.
        {
            Step.Look(0f, 20, 5), Step.Walk(0.5f, 20, 11),
            Step.Cast(8.5f, SpellIds.RayOfFrost, 20, 10, 0),
            Step.Cast(10f, SpellIds.ShockingGrasp, 24, 9, 1),
        },
        new[] // Player 4: water on the burning oil, then wind across the meadow fire.
        {
            Step.Look(0f, 19, 3), Step.Walk(0.5f, 15, 12),
            Step.Cast(6f, SpellIds.CreateWater, 19, 3, 0),
            Step.Cast(11f, SpellIds.GustOfWind, 14, 12, 3),
        },
    };

    ChemGrid grid;
    WizardPuppet puppet;
    SpellCaster caster;
    int startTick;
    int part;
    Vector3 lookAt;

    public void Begin(ChemGrid yard, int fromTick, ulong clientId)
    {
        grid = yard;
        startTick = fromTick;
        part = (int)(clientId % (ulong)Parts.Length);
        puppet = new WizardPuppet(gameObject);
        caster = GetComponent<SpellCaster>();
        caster.enabled = false;
        lookAt = grid.YardCellCenter(16, 8);
        Current = this;
        StartCoroutine(Run());
    }

    float Elapsed => (grid.World.Tick - startTick) / (float)grid.World.Rules.Sim.TicksPerSecond;

    IEnumerator Run()
    {
        yield return new WaitUntil(() => Elapsed >= 0f);
        foreach (var step in Parts[part])
        {
            yield return new WaitUntil(() => Elapsed >= step.At);
            // Parts are written in yard coordinates; the world may extend beyond the yard.
            var at = grid.YardCell(step.X, step.Y);
            var cell = grid.CellCenter(at.x, at.y);
            switch (step.Act)
            {
                case Act.Walk: StartCoroutine(puppet.WalkTo(cell)); break;
                case Act.Look: lookAt = cell; break;
                case Act.Cast:
                    puppet.Face(cell);
                    caster.Cast(step.Spell, at.x, at.y, step.Dir);
                    break;
            }
        }
        yield return new WaitUntil(() => Elapsed >= Length);
        Done = true;
    }

    void LateUpdate()
    {
        if (puppet != null && !Done) puppet.AimCamera(lookAt, 14f, 2.5f);
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
