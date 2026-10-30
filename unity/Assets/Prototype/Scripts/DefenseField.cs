using Chem;
using UnityEngine;

// The defense field north of the yard (DefenseFieldBuilder builds it;
// docs/BACKLOG.md 17): a gate where raiders come in, a flag they march on,
// and a start plate. The waves themselves are chemistry (World.Defense.cs):
// standing on the plate for a moment sends the start command, which solo runs
// at once and online goes to the host (NetChem.RequestDefense) to be stamped
// into the next tick's batch, so every machine starts the waves on the same
// tick and runs them the same way, and a late joiner replays the command.
// This side only asks and shows: the raiders as Punchy creatures sliding
// smoothly from square to square, their health, death puffs
// (DefenseField.Draw.cs), the lives on the flag's banner, and a HUD line near
// the field.
public partial class DefenseField : MonoBehaviour
{
    public ChemGrid grid;
    [Tooltip("The gate's middle, where raiders come in, and the flag's foot, which they march on.")]
    public Transform gate, flag;
    [Tooltip("The start plate's middle (its top) and radius (m): stand on it to call the raiders.")]
    public Vector3 startPlate;
    public float plateRadius = 1.4f;
    [Tooltip("The lives (and the wave) on the flag's banner.")]
    public TextMesh banner;
    [Tooltip("How near the field's middle (m) its HUD line shows.")]
    public float hudRange = 70f;

    const float HoldToStart = 1.2f, FindEvery = 1f;

    CharacterController local;
    float nextFind, onPlate;
    bool asked;
    string shownBanner;
    GUIStyle hudStyle;

    public World World => grid != null ? grid.World : null;

    /// <summary>The gate's square and the flag's.</summary>
    public Vector2Int GateCell => grid.CellAt(gate.position);
    public Vector2Int FlagCell => grid.CellAt(flag.position);

    /// <summary>The command this field's plate sends: start the waves from its gate toward its flag.</summary>
    public DefenseCommand StartCommand
    {
        get
        {
            Vector2Int g = GateCell, f = FlagCell;
            return new DefenseCommand(DefenseKind.Start, g.x, g.y, f.x, f.y);
        }
    }

    /// <summary>Whether waves are coming now.</summary>
    public bool Running => World?.Defense != null && World.Defense.Phase == DefensePhase.Running;

    /// <summary>Starts the waves (or again, after a win or a loss), solo at once or online through the host.</summary>
    public void RequestStart() => Request(grid, StartCommand);

    /// <summary>The one door for defense commands: solo the world runs it now; online the host stamps it for every machine.</summary>
    public static void Request(ChemGrid grid, DefenseCommand command)
    {
        if (grid == null || grid.World == null) return;
        if (grid.TryGetComponent<NetChem>(out var net) && net.Online)
        {
            net.RequestDefense(command);
            return;
        }
        grid.World.ApplyDefense(command);
    }

    void Update()
    {
        if (World == null) return;
        if (Time.time >= nextFind) FindLocal();
        Plate();
        Banner();
        UpdateRaiders();
    }

    void FindLocal()
    {
        nextFind = Time.time + FindEvery;
        local = null;
        foreach (var body in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            if (body.name == WizardRig.Name) local = body;
    }

    // Standing on the plate a moment (while no waves come) calls them; stepping off arms it again.
    void Plate()
    {
        bool on = local != null && Flat(local.transform.position - startPlate) < plateRadius && Mathf.Abs(local.transform.position.y - startPlate.y) < 1.5f;
        if (!on)
        {
            onPlate = 0f;
            asked = false;
            return;
        }
        onPlate += Time.deltaTime;
        if (asked || Running || onPlate < HoldToStart) return;
        asked = true;
        RequestStart();
        EventLog.Add("The gate opens: raiders are coming for the flag!");
    }

    void Banner()
    {
        if (banner == null) return;
        var d = World.Defense;
        string text = d == null
            ? $"LIVES {World.Rules.Sim.Defense.Lives}"
            : $"LIVES {Mathf.Max(0, d.Lives)}\nWAVE {d.Wave}/{World.Rules.Sim.Defense.Waves}";
        if (text == shownBanner) return;
        shownBanner = text;
        banner.text = text;
    }

    /// <summary>The HUD line: the wave, raiders left and lives, or how it ended, or how to start.</summary>
    public string HudLine()
    {
        var d = World?.Defense;
        var p = World?.Rules.Sim.Defense;
        if (p == null) return "";
        if (d == null) return "The defense field: stand on the start plate to call the raiders";
        return d.Phase switch
        {
            DefensePhase.Running => $"Wave {d.Wave}/{p.Waves}: {World.DefenseLeft} left, lives {Mathf.Max(0, d.Lives)}/{p.Lives}",
            DefensePhase.Won => $"The flag stands! All {p.Waves} waves beaten with {d.Lives}/{p.Lives} lives. Stand on the plate to play again",
            DefensePhase.Lost => $"The flag fell in wave {d.Wave}/{p.Waves}. Stand on the plate to try again",
            _ => "",
        };
    }

    bool Near()
    {
        if (local == null || gate == null || flag == null) return false;
        var middle = (gate.position + flag.position) / 2f;
        return Flat(local.transform.position - middle) < hudRange;
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    void OnGUI()
    {
        HudScale.Apply();
        if (!EventLog.HudVisible || World == null || !Near()) return;
        DrawHealthBars();
        hudStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
        GUI.Label(new Rect(HudScale.Width / 2f - 400f, 54f, 800f, 36f), $"<b>{HudLine()}</b>", hudStyle);
    }
}
