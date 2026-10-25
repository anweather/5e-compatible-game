using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// Picks a spell from rules/spells.json and casts it on the grid (controls
// in PlayerControls). Aiming matches the web sandbox: beams and Gust start
// next to the wizard and point at the target, everything else lands on the
// targeted cell. The cells it will touch are highlighted.
public class SpellCaster : MonoBehaviour
{
    public ChemGrid grid;
    public PlayerControls controls;
    public string[] spellIds = { "fire-bolt", "ray-of-frost", "create-water", "grease", "shocking-grasp", "gust-of-wind" };

    public int Selected { get; private set; }
    public string SelectedName => grid != null && grid.World != null ? grid.World.Rules.Spell(spellIds[Selected])?.Name ?? spellIds[Selected] : spellIds[Selected];

    readonly List<GameObject> markers = new();
    Material markerMat;

    void Start()
    {
        markerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        markerMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.3f));
        markerMat.EnableKeyword("_EMISSION");
        markerMat.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.2f) * 1.5f);
    }

    void OnDestroy() => Destroy(markerMat);

    void Update()
    {
        if (controls == null || grid == null || grid.World == null) return;

        int slot = controls.PickedSlot();
        if (slot >= 0 && slot < spellIds.Length) Selected = slot;
        if (controls.NextSpell.WasPressedThisFrame()) Selected = (Selected + 1) % spellIds.Length;
        if (controls.PrevSpell.WasPressedThisFrame()) Selected = (Selected + spellIds.Length - 1) % spellIds.Length;

        // With mouse-look or a gamepad, aim through the crosshair at the screen centre;
        // with a free cursor (e.g. after Esc in the Editor), aim where it points.
        bool centre = Cursor.lockState == CursorLockMode.Locked || controls.UsingGamepad || Mouse.current == null;
        var aimAt = centre ? new Vector2(Screen.width / 2f, Screen.height / 2f) : Mouse.current.position.ReadValue();
        var ray = Camera.main.ScreenPointToRay(aimAt);
        if (!RaycastPastSelf(ray, out var hit))
        {
            ShowMarkers(null);
            return;
        }
        var target = grid.CellAt(hit.point - hit.normal * 0.05f);
        var aim = Aim(target, out var error);
        var spell = grid.World.Rules.Spell(spellIds[Selected]);
        ShowMarkers(aim.HasValue && spell != null ? grid.World.SpellCells(spell, aim.Value.x, aim.Value.y, aim.Value.dir) : null);

        if (!controls.Cast.WasPressedThisFrame()) return;
        if (error != null) { EventLog.Add(error); return; }
        if (aim.HasValue) Cast(spellIds[Selected], aim.Value.x, aim.Value.y, aim.Value.dir);
    }

    readonly RaycastHit[] hits = new RaycastHit[16];

    // The nearest hit that isn't the caster: the over-the-shoulder crosshair
    // often passes through the wizard's own collider first.
    bool RaycastPastSelf(Ray ray, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(ray, hits, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= best) continue;
            best = hits[i].distance;
            nearest = hits[i];
        }
        return best < float.MaxValue;
    }

    // The cast is shown at once (gesture, projectile) and takes effect when it
    // lands. Online, the cast goes to the host and lands on every machine at
    // the same tick, timed to roughly when the projectile arrives. The demos
    // cast through this too.
    public void Cast(string spellId, int x, int y, int dir)
    {
        var online = Net;
        if (online != null && online.Online)
        {
            int delay = Mathf.CeilToInt(FlightSeconds(spellId, x, y, dir) * grid.World.Rules.Sim.TicksPerSecond);
            online.RequestCast(spellId, x, y, dir, delay);
            return;
        }
        PlayCastEffect(spellId, x, y, dir, () => grid.World.Cast(spellId, x, y, dir));
    }

    /// <summary>
    /// Shows a cast: the casting gesture, the projectile if the spell has one, and the effect
    /// where it lands. <paramref name="onArrive"/> runs once, when it lands (at once without a projectile).
    /// </summary>
    public void PlayCastEffect(string spellId, int x, int y, int dir, System.Action onArrive = null)
    {
        var look = visuals != null ? visuals.For(spellId) : null;
        var landing = Landing(spellId, x, y, dir);
        if (pose != null) pose.Play(landing, look?.glow ?? Color.white);

        void Land()
        {
            if (look?.impact != null) Destroy(Instantiate(look.impact, landing, Quaternion.LookRotation(Flat(landing - Hand))), look.impactSeconds);
            onArrive?.Invoke();
        }

        if (look?.projectile != null) SpellProjectile.Launch(look.projectile, Hand, landing, Land);
        else Land();
    }

    [Tooltip("How each spell looks when cast. Without it, spells take effect with no show.")]
    public SpellVisuals visuals;

    CastPose pose;

    void Awake() => pose = GetComponent<CastPose>();

    Vector3 Hand => pose != null ? pose.HandPosition : transform.position + Vector3.up * 1.3f + transform.forward * 0.4f;

    // Where the spell's show lands: the far end of a beam, the caster for a burst, otherwise the target cell.
    Vector3 Landing(string spellId, int x, int y, int dir)
    {
        var spell = grid.World.Rules.Spell(spellId);
        if (spell != null && spell.Shape == "line")
        {
            var cells = grid.World.SpellCells(spell, x, y, dir);
            if (cells.Count > 0) (x, y) = cells[cells.Count - 1];
        }
        else if (spell != null && spell.Shape == "burst") return transform.position + Vector3.up;
        return grid.CellCenter(x, y) + Vector3.up * (grid.SurfaceY(x, y) + 0.3f);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-4f ? v : Vector3.forward;
    }

    float FlightSeconds(string spellId, int x, int y, int dir)
    {
        var look = visuals != null ? visuals.For(spellId) : null;
        return look?.projectile != null ? Vector3.Distance(Hand, Landing(spellId, x, y, dir)) / SpellProjectile.DefaultSpeed : 0f;
    }

    NetChem net;

    // Found lazily: a networked wizard gets its grid assigned after Awake.
    NetChem Net => net != null ? net : net = grid != null ? grid.GetComponent<NetChem>() : null;

    void OnEnable() => EventLog.Track(this);

    (int x, int y, int dir)? Aim(Vector2Int target, out string error)
    {
        error = null;
        var spell = grid.World.Rules.Spell(spellIds[Selected]);
        if (spell == null) { error = $"{spellIds[Selected]} isn't in rules/spells.json"; return null; }
        if (!grid.World.InBounds(target.x, target.y)) return null;
        var me = grid.CellAt(transform.position);
        int? dir = DirFrom(me, target);
        if (spell.Origin != "self") return (target.x, target.y, dir ?? 1);
        if (spell.Shape == "burst") return (me.x, me.y, dir ?? 1);
        if (dir == null) { error = $"Click away from the wizard to point {spell.Name} that way."; return null; }
        var (dx, dy) = Step(dir.Value);
        return (me.x + dx, me.y + dy, dir.Value);
    }

    // 0 = north (y-1), 1 = east, 2 = south, 3 = west: the sandbox's directions.
    static int? DirFrom(Vector2Int a, Vector2Int b)
    {
        int dx = b.x - a.x, dy = b.y - a.y;
        if (dx == 0 && dy == 0) return null;
        return Mathf.Abs(dx) >= Mathf.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0);
    }

    static (int, int) Step(int dir) => dir switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };

    void ShowMarkers(List<(int x, int y)> cells)
    {
        int n = cells?.Count ?? 0;
        while (markers.Count < n)
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m.name = "Aim";
            Destroy(m.GetComponent<Collider>());
            m.GetComponent<Renderer>().sharedMaterial = markerMat;
            m.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markers.Add(m);
        }
        for (int i = 0; i < markers.Count; i++)
        {
            bool on = i < n;
            markers[i].SetActive(on);
            if (!on) continue;
            var (x, y) = cells[i];
            var c = grid.CellCenter(x, y);
            markers[i].transform.position = new Vector3(c.x, grid.SurfaceY(x, y) + 0.06f, c.z);
            markers[i].transform.localScale = new Vector3(0.3f * grid.cellSize, 0.02f, 0.3f * grid.cellSize);
        }
    }
}
