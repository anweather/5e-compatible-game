using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The spell radial (docs/SPELL_RADIAL.md): hold Tab or LB, aim at an element
// with the mouse or the right stick, rest on it or push further out to open
// its spells as an arc, release to pick. A quick tap swaps back to the
// previous spell; B cancels (with the mouse, releasing in the centre does).
// Without opening it: Q / E and RB step through the current element, the
// d-pad's left / right step elements, a number key opens that element's arc.
//
// The choosing is SpellRadial, engine-free and tested in the core; this feeds
// it the controls each frame, pauses camera look while it's open (walking
// goes on, and so does the game, online), draws it with IMGUI, and selects
// through SpellCaster.Select, so casting and online play are as before. The
// wedges are the rules' spell groups (rules/groups.json), laid out again
// whenever the rules change. Only the local player's wizard has it running:
// it waits for its SpellCaster and controls to be switched on (NetWizard).
[RequireComponent(typeof(SpellCaster))]
public class SpellRadialHud : MonoBehaviour
{
    // Ring 1's outer radius at 1080 lines; scaled with the screen.
    const float RingPixels = 230f;

    SpellCaster caster;
    PlayerControls controls;
    SpellRadial radial;
    ChemGrid laidOutFor;
    InputAction heldBy;
    int closedFrame = -1;
    Component lookInput;
    bool lookPaused, lookWasOn;
    readonly Dictionary<int, Texture2D> wedgeTextures = new();
    Texture2D disc;
    GUIStyle label, small, slot;
    // The element whose spells are out (-1: none), since when, and how long they take to unfold (s).
    int arcElement = -1;
    float arcOpenedAt;
    const float UnfoldSeconds = 0.14f;

    /// <summary>The radial's state, for the HUD and the probe (null until the wizard is in a world).</summary>
    public SpellRadial Radial => radial;
    public bool IsOpen => radial != null && radial.IsOpen;
    /// <summary>True while open, and on the frame it closed: the frame's buttons were its own (B cancels it, and also throws).</summary>
    public bool BusyThisFrame => IsOpen || closedFrame == Time.frameCount;

    /// <summary>The element the selected spell is in, for the HUD chip, or null.</summary>
    public RadialGroup CurrentGroup
    {
        get
        {
            if (radial == null || caster == null) return null;
            int g = radial.Layout.GroupOfSpell(caster.SelectedId);
            return g >= 0 ? radial.Layout.Groups[g] : null;
        }
    }

    void Awake()
    {
        caster = GetComponent<SpellCaster>();
        TryGetComponent(out controls);
    }

    void Update()
    {
        if (!Ready())
        {
            if (IsOpen) Close();
            return;
        }
        LayOut();
        if (radial.Current != caster.SelectedId) radial.Chose(caster.SelectedId);
        if (IsOpen) FeedOpen();
        else Idle();
        PauseLook(IsOpen);
    }

    bool Ready() => caster != null && caster.enabled && controls != null && controls.enabled && caster.grid != null && caster.grid.World != null;

    // The wedges for the rules in force: made on the first frame, made again when the rules change.
    void LayOut()
    {
        if (laidOutFor == caster.grid && radial != null) return;
        if (laidOutFor != null) laidOutFor.RulesChanged -= Relayout;
        laidOutFor = caster.grid;
        laidOutFor.RulesChanged += Relayout;
        radial = new SpellRadial(RadialLayout.From(laidOutFor.World.Rules));
        radial.Chose(caster.SelectedId);
    }

    void Relayout()
    {
        if (radial == null || laidOutFor == null) return;
        radial.Layout = RadialLayout.From(laidOutFor.World.Rules);
        if (laidOutFor.World.Rules.Spell(caster.SelectedId) == null && radial.Layout.Count > 0)
            caster.Select(radial.RememberedSpell(0));
    }

    void OnDestroy()
    {
        if (laidOutFor != null) laidOutFor.RulesChanged -= Relayout;
        foreach (var t in wedgeTextures.Values) Destroy(t);
        if (disc != null) Destroy(disc);
    }

    // --- input -------------------------------------------------------------------------

    // Closed: the hold button or a number key opens it; the bumpers, Q / E and the d-pad step.
    void Idle()
    {
        double now = Time.unscaledTimeAsDouble;
        if (controls.RadialHold.WasPressedThisFrame())
        {
            heldBy = controls.RadialHold;
            Apply(radial.Feed(Sample(true, now, false)));
            return;
        }
        int element = controls.PickedElement();
        if (element >= 0 && element < radial.Layout.Count)
        {
            heldBy = controls.PickElement;
            radial.OpenAt(element, now);
            return;
        }
        if (controls.NextSpellPressed()) Select(radial.StepInElement(1));
        if (controls.PrevSpell.WasPressedThisFrame()) Select(radial.StepInElement(-1));
        if (controls.NextElement.WasPressedThisFrame()) Select(radial.StepElement(1));
        if (controls.PrevElement.WasPressedThisFrame()) Select(radial.StepElement(-1));
    }

    void FeedOpen()
    {
        bool held = heldBy != null && heldBy.IsPressed();
        bool cancel = controls.RadialCancel.WasPressedThisFrame();
        Apply(radial.Feed(Sample(held, Time.unscaledTimeAsDouble, cancel)));
        if (!radial.IsOpen) Closed();
    }

    // One frame of aim: the right stick when the gamepad opened it, else the mouse's movement in ring radii.
    RadialInput Sample(bool held, double now, bool cancel)
    {
        // The stick of the pad whose button holds it open (with two pads connected, not just the newest).
        var pad = heldBy?.activeControl?.device as Gamepad;
        if (pad == null && heldBy == controls.RadialHold && controls.UsingGamepad) pad = Gamepad.current;
        if (pad != null)
        {
            var stick = pad.rightStick.ReadValue();
            return RadialInput.Stick(stick.x, stick.y, held, now, cancel);
        }
        var delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        float ring = Radius();
        return RadialInput.Mouse(delta.x / ring, delta.y / ring, held, now, cancel);
    }

    void Apply(RadialResult result)
    {
        if (result.Outcome == RadialOutcome.Selected || result.Outcome == RadialOutcome.SwapPrevious) Select(result.SpellId);
        if (!radial.IsOpen && result.Outcome != RadialOutcome.None) Closed();
    }

    void Select(string spellId)
    {
        if (spellId != null) caster.Select(spellId);
    }

    void Close()
    {
        radial.Feed(RadialInput.Mouse(0, 0, held: false, Time.unscaledTimeAsDouble, cancel: true));
        Closed();
        PauseLook(false);
    }

    void Closed()
    {
        heldBy = null;
        closedFrame = Time.frameCount;
    }

    // Camera look stops while it's open (the mouse and the right stick are aiming at it), and the last
    // look input is cleared so the camera doesn't keep turning. Movement is untouched. Only on opening and
    // closing, and closing puts back what was there (a scripted wizard's look is off: WizardPuppet).
    void PauseLook(bool paused)
    {
        if (paused == lookPaused) return;
        if (lookInput == null) lookInput = GetComponent(WizardRig.InputsComponent);
        if (lookInput == null) return;
        var field = WizardRig.Field(lookInput, "cursorInputForLook", isPublic: true);
        if (field == null) return;
        lookPaused = paused;
        if (!paused)
        {
            field.SetValue(lookInput, lookWasOn);
            return;
        }
        lookWasOn = field.GetValue(lookInput) is true;
        field.SetValue(lookInput, false);
        var look = WizardRig.Field(lookInput, "look", isPublic: true);
        if (look != null && look.FieldType == typeof(Vector2)) look.SetValue(lookInput, Vector2.zero);
    }

    // --- drawing -----------------------------------------------------------------------

    static float Radius() => RingPixels * Scale;

    // Sizes scale with the screen, but never below a readable size on a small window.
    static float Scale => Mathf.Max(0.85f, Screen.height / 1080f);

    void OnGUI()
    {
        if (!IsOpen || !EventLog.HudVisible) return;
        MakeStyles();
        GUI.depth = -50; // over the HUD, under the Esc menu
        var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
        float r = Radius();
        var groups = radial.Layout.Groups;
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(centre.x - r * 1.05f, centre.y - r * 1.05f, r * 2.1f, r * 2.1f), Disc());
        GUI.color = Color.white;
        bool spellsOut = radial.State == RadialState.Spells && radial.Element >= 0;
        int arcFor = spellsOut ? radial.Element : -1;
        if (arcFor != arcElement)
        {
            arcElement = arcFor;
            arcOpenedAt = Time.unscaledTime;
        }
        for (int i = 0; i < groups.Count; i++) DrawWedge(centre, r, i, groups, spellsOut);
        if (spellsOut) DrawArc(centre, r, radial.Element, groups);
        DrawCentre(centre, r);
        GUI.color = Color.white;
    }

    // An element's wedge: lit when highlighted; once an element's spells are out the others fall
    // back, so the spells read as a second pick inside the first.
    void DrawWedge(Vector2 centre, float r, int i, IReadOnlyList<RadialGroup> groups, bool spellsOut)
    {
        double angle = RadialGeometry.WedgeCentre(i, groups.Count);
        bool lit = radial.Element == i;
        var colour = ParseColor(groups[i].Color);
        colour.a = WedgeAlpha(lit, spellsOut);
        var matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot((float)angle, centre);
        GUI.color = colour;
        GUI.DrawTexture(new Rect(centre.x - r, centre.y - r, r * 2f, r * 2f), Wedge(groups.Count));
        GUI.matrix = matrix;
        GUI.color = Color.white;
        var at = Polar(centre, angle, r * 0.68f);
        label.fontSize = Mathf.RoundToInt((lit ? 22 : 18) * Scale);
        Centred(at, $"<b>{groups[i].Name}</b>", label);
    }

    void DrawArc(Vector2 centre, float r, int element, IReadOnlyList<RadialGroup> groups)
    {
        var spells = groups[element].SpellIds;
        var rules = caster.grid.World.Rules;
        for (int s = 0; s < spells.Count; s++)
        {
            double angle = RadialGeometry.SlotCentre(element, groups.Count, s, spells.Count, radial.Tuning);
            var def = rules.Spell(spells[s]);
            bool lit = radial.Spell == s;
            // Unfolding out of the ring's edge as the arc opens.
            var at = Polar(centre, angle, r * Mathf.Lerp(1.02f, 1.45f, Unfold()));
            string level = LevelOf(def);
            string name = def?.Name ?? spells[s];
            string text = lit ? $"<color=#ffd24a><b>{name}</b></color>\n<size={small.fontSize}>{level}</size>" : $"<b>{name}</b>\n<size={small.fontSize}>{level}</size>";
            var size = new Vector2(210f, 58f) * Scale;
            var box = new Rect(at.x - size.x / 2f, at.y - size.y / 2f, size.x, size.y);
            if (lit)
            {
                // Edged in the element's colour.
                GUI.color = ParseColor(groups[element].Color);
                GUI.DrawTexture(new Rect(box.x - 3f, box.y - 3f, box.width + 6f, box.height + 6f), Texture2D.whiteTexture);
            }
            GUI.color = lit ? new Color(0.15f, 0.12f, 0.05f, 0.95f) : new Color(0.05f, 0.06f, 0.08f, 0.8f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            Centred(at, text, slot);
        }
    }

    // The highlighted spell's name and its one-line effect in the centre, else how to cancel.
    void DrawCentre(Vector2 centre, float r)
    {
        var id = radial.HighlightedSpell;
        var def = id != null ? caster.grid.World.Rules.Spell(id) : null;
        bool pad = controls.UsingGamepad;
        string text = def != null ? $"<b>{def.Name}</b>" : $"{StepHint()}\n{CancelHint(pad)}";
        label.fontSize = Mathf.RoundToInt(17 * Scale);
        Centred(centre, text, label);
        if (def?.Blurb != null)
        {
            // The highlighted spell's effect, in a box at the top, under the HUD lines (below the ring it can run off a small window).
            small.wordWrap = true;
            small.fontSize = Mathf.RoundToInt(17 * Scale);
            var box = new Rect(centre.x - r * 1.5f, 78f, r * 3f, small.fontSize * 2.6f);
            GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.85f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 8f, box.y + 4f, box.width - 16f, box.height - 8f), FirstSentence(def.Blurb), small);
        }
        if (!pad)
        {
            var cursor = new Vector2(centre.x + (float)radial.CursorX * r, centre.y - (float)radial.CursorY * r);
            GUI.DrawTexture(new Rect(cursor.x - 5f, cursor.y - 5f, 10f, 10f), Disc());
        }
    }

    // How far the arc has unfolded since it opened (0 to 1, eased over UnfoldSeconds).
    float Unfold() => Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - arcOpenedAt) / UnfoldSeconds);

    // A wedge's opacity: highlighted, ordinary, or fallen back while an element's spells are out.
    static float WedgeAlpha(bool lit, bool spellsOut)
    {
        if (lit) return 0.9f;
        return spellsOut ? 0.18f : 0.45f;
    }

    // Andrew: it wasn't clear it's two picks. The centre says which step it's on.
    string StepHint()
    {
        int size = Mathf.RoundToInt(15 * Scale);
        string step = radial.State == RadialState.Spells && radial.Element >= 0
            ? $"2 · pick a {radial.Layout.Groups[radial.Element].Name} spell"
            : "1 · pick an element, then push out for its spells";
        return $"<size={size}><color=#ffffffcc>{step}</color></size>";
    }

    static string LevelOf(SpellDef def)
    {
        if (def == null) return "";
        return def.Level == 0 ? "cantrip" : $"level {def.Level}";
    }

    static string CancelHint(bool pad) => pad ? PadGlyphs.Text("<color=#ffffffaa>{East}: cancel</color>") : "<color=#ffffffaa>release here: cancel</color>";

    static string FirstSentence(string blurb)
    {
        int stop = blurb.IndexOf(". ", System.StringComparison.Ordinal);
        return stop > 0 ? blurb.Substring(0, stop + 1) : blurb;
    }

    // Degrees clockwise from up, at a distance from the centre, in GUI space (y down).
    static Vector2 Polar(Vector2 centre, double degrees, float distance)
    {
        float a = (float)degrees * Mathf.Deg2Rad;
        return new Vector2(centre.x + Mathf.Sin(a) * distance, centre.y - Mathf.Cos(a) * distance);
    }

    static void Centred(Vector2 at, string text, GUIStyle style) =>
        GUI.Label(new Rect(at.x - 150f, at.y - 40f, 300f, 80f), text, style);

    static Color ParseColor(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.grey;

    void MakeStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        small = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(15 * Scale) };
        slot = new GUIStyle(label) { fontSize = Mathf.RoundToInt(17 * Scale) };
    }

    // One ring-1 wedge pointing straight up, as wide as `count` wedges allow, with a small gap at each side.
    Texture2D Wedge(int count)
    {
        if (wedgeTextures.TryGetValue(count, out var tex)) return tex;
        const int size = 256;
        float half = 180f / count - 1.5f, inner = 0.35f, outer = 1f;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f; // v up (textures start at the bottom)
                float d = Mathf.Sqrt(u * u + v * v);
                float angle = Mathf.Abs(Mathf.Atan2(u, v) * Mathf.Rad2Deg);
                float edge = Mathf.Min(Mathf.Min(d - inner, outer - d) * size * 0.5f, (half - angle) * d * size * 0.01f);
                byte a = (byte)(Mathf.Clamp01(edge) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        wedgeTextures[count] = tex;
        return tex;
    }

    Texture2D Disc()
    {
        if (disc != null) return disc;
        const int size = 128;
        disc = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                byte a = (byte)(Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * size * 0.5f) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        disc.SetPixels32(pixels);
        disc.Apply();
        return disc;
    }
}
