using System.Linq;
using Chem;
using UnityEngine;

// The cast on the HUD (EventLog draws it): about two thirds of the way down the screen,
// and, bottom left and sized like the portrait's block, the d-pad cross of the kit: the
// selected spell on top, arrows either side, the spell list's book below. Reads the core
// (World.CastOf, World.HandsOf); sends nothing but the merge's rumble.
//
// With the hands (D16, docs/design/unified-casting.md) each hand has an icon, left on the
// left: its element's glyph, and from the moment a gesture starts the gesture's mark on it
// (the thrust a short burst at the hand, the throw a streak with an arrowhead, the sweep a
// ring of points). A held gesture's charge ring fills in steps, one a level, as the core's
// charge and CastBeats' rumble beats reach them. When both hands' charges merge the icons
// fly together and slam into the pair's own icon, with a beat; a cooling hand is greyed and
// refills, and a locked one shows a lock, which shakes when pressed. Under them the cast
// bar fills smoothly (the core's meter counts ticks; the bar runs by the time left).
// In the debug layout a cast by elements shows its element orbs, the bar and its charge's
// steps instead, with the held spell's name once it's reached.
public sealed class CastHud
{
    const float OrbRadius = 44f, OrbGap = 14f, MeterWidth = 260f, MeterHeight = 14f, StepHeight = 7f, StepGap = 4f;
    // How fast a charge step lights up (per second), and the bar's bright leading edge.
    const float StepRate = 9f, EdgeWidth = 3f;
    const float DownTheScreen = 2f / 3f;
    // The cross's three by three cells: a block about the size of VitalsHud's.
    const float Cell = 56f;
    // A hand's icon: its radius, how far each sits from the middle, its charge ring, and the merge's flight and slam (s).
    const float HandRadius = 32f, HandApart = 74f, RingIn = HandRadius + 11f, RingOut = HandRadius + 16f, RingGap = 0.025f;
    const float MergeSeconds = 0.14f, SlamSeconds = 0.3f, LockShakeSeconds = 0.45f;

    static readonly Color Shade = new(0f, 0f, 0f, 0.55f), Meter = new(1f, 0.82f, 0.3f), Edge = new(1f, 0.96f, 0.8f), Glyph = new(1f, 1f, 1f, 0.9f);
    static readonly Color Gold = new(1f, 0.82f, 0.3f), Grey = new(0.55f, 0.55f, 0.58f);

    /// <summary>How full the cast bar was drawn last, 0 to 1, for the probe.</summary>
    public float Share { get; private set; }

    /// <summary>A hand's icon as drawn last, for the probe: its element, gesture (or NoGesture), lit charge steps, cooling, locked.</summary>
    public (string element, int gesture, int charge, bool cooling, bool locked) HandShown(int hand) => shown[hand];

    /// <summary>The hands' icons have met in the pair's icon (the merge), and how many slams there have been, for the probe.</summary>
    public bool MergeShown => merge >= 1f && mergedSpell != null;
    public int Slams { get; private set; }

    GUIStyle label, small, hint;
    int tickSeen, passedSeen;
    float tickAt, barAt;
    bool barRunning;
    readonly float[] stepsLit = new float[World.MaxCharge];
    readonly float[][] handSteps = { new float[World.MaxCharge], new float[World.MaxCharge], new float[World.MaxCharge] };
    readonly int[] coolTotal = new int[HandsDef.Count];
    readonly (string, int, int, bool, bool)[] shown = new (string, int, int, bool, bool)[HandsDef.Count];
    float merge, slamAt = float.NegativeInfinity;
    string mergedSpell;
    bool slammed;

    /// <summary>Draws both parts for <paramref name="caster"/>.</summary>
    public void Draw(SpellCaster caster, GUIStyle style)
    {
        label ??= new GUIStyle(style) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        small ??= new GUIStyle(style) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        hint ??= new GUIStyle(style) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        var world = caster.grid.World;
        var cast = world.CastOf(caster.CastPlayer);
        bool hands = caster.controls != null && caster.controls.HandPad;
        if (hands) Hands(world, cast, caster);
        else if (cast != null && cast.Casting) Loaded(world, cast, caster);
        else NoteTick(world);
        Cross(world, caster);
    }

    void Loaded(World world, PlayerCast cast, SpellCaster caster)
    {
        float cx = HudScale.Width / 2f, y = HudScale.Height * DownTheScreen;
        var dpad = world.DpadOf(cast.Player);
        var hand = world.HandSpellsUnderWay(cast.Player);
        if (cast.DpadCasting >= 0 && cast.DpadCasting < dpad.Count)
            GUI.Label(new Rect(cx - 200f, y - 22f, 400f, 44f), $"<b>{dpad[cast.DpadCasting].Name}</b>", small);
        else if (hand.Count > 0)
            GUI.Label(new Rect(cx - 200f, y - 22f, 400f, 44f), $"<b>{string.Join(" + ", hand.Select(s => world.Rules.Spell(s)?.Name ?? s))}</b>", small);
        else if (cast.Second != null || cast.Window > 0)
        {
            float x = OrbRadius + OrbGap / 2f;
            HudDraw.ElementOrb(new Vector2(cx - x, y), OrbRadius, cast.First);
            if (cast.Second != null) HudDraw.ElementOrb(new Vector2(cx + x, y), OrbRadius, cast.Second);
            else HudDraw.Ring(new Vector2(cx + x, y), OrbRadius - 3f, OrbRadius, 1f, new Color(1f, 1f, 1f, 0.45f));
        }
        else if (cast.First != null) HudDraw.ElementOrb(new Vector2(cx, y), OrbRadius, cast.First);
        var meter = Bar(world, cast, caster, cx, y + OrbRadius + 12f);
        Steps(world, cast, caster, meter, y - OrbRadius - 36f);
    }

    // The cast bar at this height, its cancel hint under it.
    Rect Bar(World world, PlayerCast cast, SpellCaster caster, float cx, float top)
    {
        var meter = new Rect(cx - MeterWidth / 2f, top, MeterWidth, MeterHeight);
        Fill(meter, Shade);
        Share = Progress(world, cast);
        float fill = (meter.width - 4f) * Share;
        Fill(new Rect(meter.x + 2f, meter.y + 2f, fill, meter.height - 4f), Meter);
        Fill(new Rect(meter.x + 2f + Mathf.Max(0f, fill - EdgeWidth), meter.y + 2f, Mathf.Min(fill, EdgeWidth), meter.height - 4f), Edge);
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        GUI.Label(new Rect(cx - 150f, meter.yMax + StepHeight + 6f, 300f, 22f), pad ? PadGlyphs.Text("{East} cancel") : "B cancel", hint);
        return meter;
    }

    // ---------- the hands ----------

    void Hands(World world, PlayerCast cast, SpellCaster caster)
    {
        float cx = HudScale.Width / 2f, y = HudScale.Height * DownTheScreen;
        string player = caster.CastPlayer;
        bool casting = cast != null && cast.Casting;
        var dpad = world.DpadOf(player);
        if (casting && cast.DpadCasting >= 0 && cast.DpadCasting < dpad.Count)
        {
            GUI.Label(new Rect(cx - 200f, y - 22f, 400f, 44f), $"<b>{dpad[cast.DpadCasting].Name}</b>", small);
            Bar(world, cast, caster, cx, y + HandRadius + 12f);
            return;
        }
        if (casting) Bar(world, cast, caster, cx, y + HandRadius + 40f);
        else NoteTick(world);

        var h = world.HandsOf(player);
        Merge(cast?.Merge, caster);
        if (MergeShown)
        {
            Pair(world, player, h, new Vector2(cx, y));
            return;
        }
        float apart = HandApart * (1f - merge * merge);
        Hand(world, caster, h, HandsDef.Left, new Vector2(cx - apart, y));
        Hand(world, caster, h, HandsDef.Right, new Vector2(cx + apart, y));
    }

    // The two icons fly together while the core says the hands merge, and slam once they meet; they part at once when they don't.
    void Merge(string merged, SpellCaster caster)
    {
        if (merged == null)
        {
            merge = 0f;
            slammed = false;
            mergedSpell = null;
            return;
        }
        mergedSpell = merged;
        merge = Mathf.MoveTowards(merge, 1f, Time.unscaledDeltaTime / MergeSeconds);
        if (merge < 1f || slammed) return;
        slammed = true;
        slamAt = Time.unscaledTime;
        Slams++;
        if (caster.TryGetComponent<CastBeats>(out var beats) && beats.IsLocal) PadRumble.Play(beats.feel.slamRumble, 1f);
    }

    void Hand(World world, SpellCaster caster, PlayerHands h, int hand, Vector2 at)
    {
        string player = caster.CastPlayer;
        string element = world.HandHolds(player, hand);
        int gesture = h.Gesture[hand];
        bool attuning = h.AttuneLeft > 0 && h.AttuneHand == hand;
        bool locked = !world.HandOpen(player, hand);
        int cooling = world.HandCooling(player, hand);
        float alpha = 1f;
        if (attuning)
        {
            element = h.AttuneTo ?? h.Holds[1 - hand];
            alpha = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
        }
        else if (cooling > 0 || locked) alpha = 0.4f;
        var colour = HudDraw.ElementColor(element);
        if (gesture != HandsDef.NoGesture) Mark(at, gesture, Color.Lerp(colour, Color.white, 0.45f));
        HudDraw.ElementOrb(at, HandRadius, element, alpha);
        if (cooling > 0 || locked) HudDraw.Disc(at, HandRadius, new Color(Grey.r, Grey.g, Grey.b, 0.55f));
        if (cooling > 0)
        {
            coolTotal[hand] = Mathf.Max(coolTotal[hand], cooling);
            HudDraw.Ring(at, HandRadius - 4f, HandRadius, 1f - cooling / (float)coolTotal[hand], new Color(1f, 1f, 1f, 0.7f));
        }
        else coolTotal[hand] = 0;
        if (attuning) HudDraw.Ring(at, RingIn, RingOut, 1f - h.AttuneLeft / (float)Mathf.Max(h.AttuneLeft, world.Rules.Sim.Hold.Hands.AttuneTicks), Glyph);

        var row = gesture != HandsDef.NoGesture ? world.Rules.Sim.Hold.Hands.One(element, gesture) : null;
        int lit = h.Down[hand] ? world.HandCharge(player, hand) : 0;
        ChargeRing(world, at, row, lit, handSteps[hand]);
        float since = Time.unscaledTime - caster.LockedAt(hand);
        if (locked || since < LockShakeSeconds) Lock(at + new Vector2(HandRadius * 0.62f + Shake(since), -HandRadius * 0.62f));
        if (row?.Spell != null) Name(world.Rules.Spell(row.Spell)?.Name ?? row.Spell, at.x, at.y + HandRadius + 6f);
        shown[hand] = (element, gesture, lit, cooling > 0, locked);
    }

    // The merged pair: one icon of the two colours, the spell's initials on it, its charge ring at the lower charge, and a slam.
    void Pair(World world, string player, PlayerHands h, Vector2 at)
    {
        string left = world.HandHolds(player, HandsDef.Left), right = world.HandHolds(player, HandsDef.Right);
        float age = Time.unscaledTime - slamAt;
        float pop = age < SlamSeconds ? 1f + 0.35f * (1f - age / SlamSeconds) * (1f - age / SlamSeconds) : 1f;
        float r = HandRadius * 1.2f * pop;
        if (age < SlamSeconds) HudDraw.Ring(at, r + 10f + age * 160f, r + 14f + age * 160f, 1f, new Color(1f, 1f, 1f, 0.8f * (1f - age / SlamSeconds)));
        int gesture = h.Gesture[HandsDef.Left];
        if (gesture != HandsDef.NoGesture) Mark(at, gesture, Color.Lerp(Color.Lerp(HudDraw.ElementColor(left), HudDraw.ElementColor(right), 0.5f), Color.white, 0.45f), r);
        string name = world.Rules.Spell(mergedSpell)?.Name ?? mergedSpell;
        PairIcon(left, right, mergedSpell, name, at, r, small);
        var row = gesture != HandsDef.NoGesture ? world.Rules.Sim.Hold.Hands.Both(left, right, gesture) : null;
        int lit = h.Down[HandsDef.Left] || h.Down[HandsDef.Right] ? SpellCaster.HeldHandCharge(world, player) : 0;
        ChargeRing(world, at, row, lit, handSteps[2], r - HandRadius);
        Name(name, at.x, at.y + r + 6f);
        shown[HandsDef.Left] = (left, gesture, lit, false, false);
        shown[HandsDef.Right] = (right, gesture, lit, false, false);
    }

    /// <summary>
    /// A merged pair's icon, without its charge ring or slam: the pair's own glyph (PairGlyphs) on the two colours mixed and
    /// darkened, so a white glyph reads even on air's pale blue, in a rim of each hand's colour, its own side. The spell's
    /// initials stand in for a spell with no glyph. For the HUD, and the probe's sheet of every pair.
    /// </summary>
    public static void PairIcon(string left, string right, string spell, string name, Vector2 at, float r, GUIStyle style)
    {
        Color a = HudDraw.ElementColor(left), b = HudDraw.ElementColor(right);
        HudDraw.Disc(at, r + 3f, new Color(0f, 0f, 0f, 0.6f));
        HudDraw.Arc(at, r - RimWidth, r, 0.5f, 1f, a);
        HudDraw.Arc(at, r - RimWidth, r, 0f, 0.5f, b);
        var middle = Color.Lerp(Color.Lerp(a, b, 0.5f), Color.black, 0.45f);
        middle.a = 1f;
        HudDraw.Disc(at, r - RimWidth, middle);
        if (!PairGlyphs.Draw(spell, at, r - RimWidth, Glyph))
            GUI.Label(new Rect(at.x - r, at.y - r, r * 2f, r * 2f), $"<size=24><b>{Initials(name)}</b></size>", style);
    }

    const float RimWidth = 5f;

    // The ring a held gesture charges round its icon, one step a level; none for a spell that doesn't charge, or a tap.
    void ChargeRing(World world, Vector2 at, HandRowDef row, int lit, float[] steps, float grow = 0f)
    {
        int max = row?.Spell != null && World.Chargeable(world.Rules.Spell(row.Spell)) ? World.MaxCharge : 1;
        if (lit <= 0 || max < 2)
        {
            System.Array.Clear(steps, 0, steps.Length);
            return;
        }
        for (int i = 0; i < max && i < steps.Length; i++)
        {
            steps[i] = Mathf.MoveTowards(steps[i], i < lit ? 1f : 0f, StepRate * Time.unscaledDeltaTime);
            float from = i / (float)max + RingGap, to = (i + 1) / (float)max - RingGap;
            HudDraw.Arc(at, RingIn + grow, RingOut + grow, from, to, Color.Lerp(Shade, Meter, steps[i]));
        }
    }

    // A gesture's mark on its icon: the thrust a short fat burst at the hand, the throw a streak with an arrowhead, the sweep a ring of points.
    static void Mark(Vector2 at, int gesture, Color colour, float r = HandRadius)
    {
        switch (gesture)
        {
            case HandsDef.Thrust:
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f + Mathf.PI / 8f, half = Mathf.PI / 11f;
                    HudDraw.Triangle(at + Way(a - half) * (r * 0.9f), at + Way(a + half) * (r * 0.9f), at + Way(a) * (r * 1.32f), colour);
                }
                break;
            case HandsDef.Throw:
                HudDraw.Triangle(at + new Vector2(-r * 0.62f, r * 0.2f), at + new Vector2(r * 0.62f, r * 0.2f), at + new Vector2(0f, r * 1.75f), new Color(colour.r, colour.g, colour.b, 0.6f));
                HudDraw.Triangle(at + new Vector2(-r * 0.5f, -r * 1.02f), at + new Vector2(r * 0.5f, -r * 1.02f), at + new Vector2(0f, -r * 1.62f), colour);
                break;
            case HandsDef.Sweep:
                for (int i = 0; i < 12; i++) HudDraw.Disc(at + Way(i * Mathf.PI / 6f) * (r * 1.2f), r * 0.1f, colour);
                break;
        }
    }

    // Clockwise from the top, as HudDraw's rings go.
    static Vector2 Way(float angle) => new(Mathf.Sin(angle), -Mathf.Cos(angle));

    // A small padlock: the shackle, then the body over its lower half.
    static void Lock(Vector2 at)
    {
        HudDraw.Disc(at, 12f, new Color(0f, 0f, 0f, 0.6f));
        HudDraw.Ring(at + new Vector2(0f, -3f), 3.5f, 6f, 1f, Glyph);
        Fill(new Rect(at.x - 7f, at.y - 2f, 14f, 10f), Gold);
    }

    static float Shake(float since) => since < LockShakeSeconds ? Mathf.Sin(since * 70f) * 4f * (1f - since / LockShakeSeconds) : 0f;

    void Name(string name, float cx, float top)
    {
        float width = hint.CalcSize(new GUIContent(name)).x + 16f;
        Fill(new Rect(cx - width / 2f, top + 2f, width, 20f), Shade);
        GUI.Label(new Rect(cx - 120f, top, 240f, 24f), $"<b>{name}</b>", hint);
    }

    // ---------- the bar ----------

    // Between casts, when the last tick was: a cast can start at any moment between two.
    void NoteTick(World world)
    {
        barRunning = false;
        if (world.Tick == tickSeen) return;
        (tickSeen, tickAt) = (world.Tick, Time.time);
    }

    // How far through the cast it is, 0 to 1. Each frame the bar closes its share of what's left to fill
    // against the time left, the ticks still to run less what's gone of this one. So the bar starts empty,
    // rises evenly wherever between two ticks the cast began, and a held cast, waiting on its last tick, is
    // full. A tick landing late or early, or a tap ending and the hold's ticks coming off, changes the pace
    // and never leaps the bar: every press starts as a hold, and those can land on one frame.
    float Progress(World world, PlayerCast cast)
    {
        int passed = cast.Total - cast.Meter, perSecond = world.Rules.Sim.TicksPerSecond;
        if (!barRunning || passed < passedSeen)
        {
            barRunning = true;
            Share = 0f;
            barAt = Time.time;
        }
        // The meter's own drain times the ticks in a cast: a tick can run over a few frames (ChemGrid's
        // budget), so World.Tick can turn a frame or two away from it.
        else if (passed > passedSeen) tickAt = Time.time;
        passedSeen = passed;
        float tick = Mathf.Clamp01((Time.time - tickAt) * perSecond);
        float left = Mathf.Max(0f, cast.Meter - tick) / perSecond;
        float frame = Time.time - barAt;
        barAt = Time.time;
        return frame + left > 0f ? Share + (1f - Share) * frame / (frame + left) : Share;
    }

    // The levels under the meter, from the second (a tap never shows them), and the held spell's name over the orbs.
    void Steps(World world, PlayerCast cast, SpellCaster caster, Rect meter, float nameY)
    {
        int charge = Mathf.Max(caster.HoldCharge, cast.Charge);
        if (charge < 2)
        {
            System.Array.Clear(stepsLit, 0, stepsLit.Length);
            return;
        }
        var combo = world.ComboOf(cast);
        int max = world.MaxChargeOf(combo);
        float width = (meter.width - StepGap * (max - 1)) / max;
        for (int i = 0; i < max && i < stepsLit.Length; i++)
        {
            stepsLit[i] = Mathf.MoveTowards(stepsLit[i], i < charge ? 1f : 0f, StepRate * Time.unscaledDeltaTime);
            Fill(new Rect(meter.x + i * (width + StepGap), meter.yMax + 3f, width, StepHeight), Color.Lerp(Shade, Meter, stepsLit[i]));
        }
        string spell = combo?.SpellAt(charge, max);
        if (spell != null && spell != combo.Spell) HeldName(world.Rules.Spell(spell)?.Name ?? spell, meter.center.x, nameY);
    }

    // The spell a full hold turns the cast into, on a plate of its own so it reads over anything.
    void HeldName(string name, float cx, float y)
    {
        float width = small.CalcSize(new GUIContent(name)).x + 28f;
        Fill(new Rect(cx - width / 2f, y + 2f, width, 28f), Shade);
        GUI.Label(new Rect(cx - 200f, y, 400f, 32f), $"<color=#ffd24a><b>{name}</b></color>", small);
    }

    // ---------- the kit's cross ----------

    void Cross(World world, SpellCaster caster)
    {
        float x = VitalsHud.Margin, y = HudScale.Height - VitalsHud.Margin - Cell * 3f;
        var top = Middle(x, y, 1, 0);
        var book = Middle(x, y, 1, 2);
        var left = Middle(x, y, 0, 1);
        var right = Middle(x, y, 2, 1);
        foreach (var at in new[] { top, book, left, right, Middle(x, y, 1, 1) })
            Fill(new Rect(at.x - Cell / 2f, at.y - Cell / 2f, Cell, Cell), Shade);
        float r = Cell * 0.3f;
        HudDraw.Triangle(left + new Vector2(r * 0.7f, -r), left + new Vector2(r * 0.7f, r), left + new Vector2(-r, 0f), Glyph);
        HudDraw.Triangle(right + new Vector2(-r * 0.7f, -r), right + new Vector2(-r * 0.7f, r), right + new Vector2(r, 0f), Glyph);
        Book(book);

        var spell = world.SelectedDpad(caster.CastPlayer);
        if (spell == null) return;
        var colour = spell.Spell != null ? new Color(0.62f, 0.4f, 0.95f) : new Color(0.4f, 0.4f, 0.45f);
        HudDraw.Disc(top, Cell * 0.42f, colour);
        GUI.Label(new Rect(top.x - Cell / 2f, top.y - Cell / 2f, Cell, Cell), $"<b>{Initials(spell.Name)}</b>", small);
        GUI.Label(new Rect(top.x + Cell * 0.7f, top.y - 22f, 300f, 44f), $"<b>{spell.Name}</b>", label);
    }

    static Vector2 Middle(float x, float y, int column, int row) => new(x + Cell * (column + 0.5f), y + Cell * (row + 0.5f));

    // The spell list: covers and a spine.
    static void Book(Vector2 at)
    {
        float w = Cell * 0.26f, h = Cell * 0.32f;
        Fill(new Rect(at.x - w, at.y - h, w * 2f, h * 2f), new Color(0.62f, 0.4f, 0.95f));
        Fill(new Rect(at.x - 1.5f, at.y - h, 3f, h * 2f), Color.white);
        Fill(new Rect(at.x - w * 0.7f, at.y - h * 0.5f, w * 0.45f, 3f), Color.white);
        Fill(new Rect(at.x + w * 0.25f, at.y - h * 0.5f, w * 0.45f, 3f), Color.white);
    }

    static string Initials(string name)
    {
        var words = name.Split(' ');
        return words.Length > 1 ? $"{words[0][0]}{words[1][0]}" : name.Substring(0, Mathf.Min(2, name.Length));
    }

    static void Fill(Rect r, Color colour)
    {
        GUI.color = colour;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
