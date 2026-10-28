using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chem;
using Playtest;
using StarterAssets;
using UnityEngine;

// Carries out the playtest bot's goals in the game (playtest/DESIGN.md,
// section 2): walks the wizard through its own input (the stick a gamepad
// would push, relative to the camera, as the third-person controller reads
// it), casts through the SpellCaster, picks up and throws, sits, opens the
// menus. Every choice comes from the run's dice and every decision goes into
// the trace, so the same seed on the same build does the same thing.
public sealed class PlaytestPlayer
{
    const float Arrive = 4f, NoProgressSeconds = 12f, JumpCheckSeconds = 1f;
    // How near an item must be to pick it up (metres).
    const float Reach = 3f;

    static readonly (string first, string then)[] ComboPairs =
    {
        ("grease", "fire-bolt"), ("create-water", "ray-of-frost"), ("create-water", "shocking-grasp"),
        ("fog-cloud", "gust-of-wind"), ("plant-growth", "fire-bolt"),
    };

    static readonly string[] Pages = { "", "controls", "sound", "credits", "mods" };

    readonly ChemGrid grid;
    readonly GameObject wizard;
    readonly StarterAssetsInputs inputs;
    readonly SpellCaster caster;
    readonly ItemHolder hands;
    readonly Sitter sitter;
    readonly PlaytestRandom dice;
    readonly List<(string name, Vector3 at)> places;
    readonly Action<string, string> trace;
    readonly Action<Candidate> report;

    public PlaytestPlayer(ChemGrid grid, GameObject wizard, PlaytestRandom dice, List<(string, Vector3)> places,
        Action<string, string> trace, Action<Candidate> report)
    {
        this.grid = grid;
        this.wizard = wizard;
        this.dice = dice;
        this.places = places;
        this.trace = trace;
        this.report = report;
        inputs = wizard.GetComponent<StarterAssetsInputs>();
        caster = wizard.GetComponent<SpellCaster>();
        hands = wizard.GetComponent<ItemHolder>();
        sitter = wizard.GetComponent<Sitter>();
    }

    /// <summary>
    /// The bot owns the wizard's input: the real mouse and stick never turn the camera. Menus turn
    /// look back on as they close (PauseMenu.SetLook), so PlaytestBot calls this every frame. The
    /// bot's own look (LookAround) writes look directly and still works.
    /// </summary>
    public void HoldLook()
    {
        if (inputs != null) inputs.cursorInputForLook = false;
    }

    /// <summary>Whether the bot is pushing the stick (for the stuck detector).</summary>
    public bool Pushing => inputs != null && inputs.move.sqrMagnitude > 0.01f;
    /// <summary>The frame the bot last moved the wizard itself (a teleport).</summary>
    public int TeleportedOnFrame { get; private set; } = -1;
    /// <summary>The frame number, as the bot counts it (set by PlaytestBot each frame).</summary>
    public int Frame { get; set; }

    /// <summary>What the bot can choose from in this world.</summary>
    public GoalOptions Options()
    {
        var rules = grid.World.Rules;
        var spells = rules.Spells.Where(s => s.Radial).Select(s => s.Id).ToList();
        return new GoalOptions
        {
            Places = places.Select(p => p.name).Where(n => n != "start").ToList(),
            Spells = spells,
            Combos = ComboPairs.Where(c => rules.Spell(c.first) != null && rules.Spell(c.then) != null).ToList(),
            MenuPages = Pages.Select(p => p.Length == 0 ? "main" : p).ToList(),
            HasSeats = SeatsAndItems().Any(o => o is Seat) && sitter != null,
            HasItems = SeatsAndItems().Any(o => o is Carryable) && hands != null,
        };
    }

    // Seats and items in the scene (found once per question; there are few of them).
    static IEnumerable<Component> SeatsAndItems() =>
        UnityEngine.Object.FindObjectsByType<Seat>(FindObjectsSortMode.None).Cast<Component>()
            .Concat(UnityEngine.Object.FindObjectsByType<Carryable>(FindObjectsSortMode.None));

    /// <summary>Carries a goal out, for at most its seconds of game time.</summary>
    public IEnumerator Do(Goal goal)
    {
        trace("goal", goal.ToString());
        switch (goal.Kind)
        {
            case GoalKind.Visit: yield return Visit(places.First(p => p.name == goal.Target).at, goal.Seconds); break;
            case GoalKind.Cast: yield return CastAhead(goal.Spell, null); break;
            case GoalKind.Combo: yield return CastAhead(goal.Spell, goal.Spell2); break;
            case GoalKind.Throw: yield return Throw(goal.Seconds); break;
            case GoalKind.Sit: yield return Sit(goal.Seconds); break;
            case GoalKind.Menu: yield return Menu(goal.Target); break;
            default: yield return LookAround(goal.Seconds); break;
        }
        Stop();
    }

    void Stop()
    {
        if (inputs == null) return;
        inputs.move = Vector2.zero;
        inputs.look = Vector2.zero;
        inputs.sprint = false;
        inputs.jump = false;
    }

    // Walks to a point: steers through the stick, sprints when far, jumps when blocked, and
    // teleports near it when it makes no progress for a while (logged, so it isn't blamed on the game).
    IEnumerator Visit(Vector3 target, float seconds)
    {
        float until = Time.time + seconds, lastProgress = Time.time, nextJumpCheck = Time.time + JumpCheckSeconds;
        float best = Flat(target - wizard.transform.position).magnitude;
        var checkFrom = wizard.transform.position;
        while (Time.time < until)
        {
            var to = Flat(target - wizard.transform.position);
            float dist = to.magnitude;
            if (dist < Arrive)
            {
                trace("arrive", Place(target));
                yield break;
            }
            if (dist < best - 1f)
            {
                best = dist;
                lastProgress = Time.time;
            }
            Steer(to, dist > 20f);
            if (Time.time >= nextJumpCheck)
            {
                bool blocked = Flat(wizard.transform.position - checkFrom).magnitude < 0.3f;
                if (inputs != null) inputs.jump = blocked;
                if (blocked) trace("jump", "blocked");
                checkFrom = wizard.transform.position;
                nextJumpCheck = Time.time + JumpCheckSeconds;
            }
            if (Time.time - lastProgress > NoProgressSeconds)
            {
                Teleport(target - (Arrive * 0.5f) * to.normalized);
                yield break;
            }
            yield return null;
        }
        trace("gave up", Place(target));
    }

    // The stick, as a gamepad would push it: the third-person controller moves relative to the camera's yaw.
    void Steer(Vector3 to, bool sprint)
    {
        if (inputs == null || Camera.main == null) return;
        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float rel = (want - Camera.main.transform.eulerAngles.y) * Mathf.Deg2Rad;
        inputs.move = new Vector2(Mathf.Sin(rel), Mathf.Cos(rel));
        inputs.sprint = sprint;
    }

    void Teleport(Vector3 at)
    {
        // Never off the world: a place out there is the bot's mistake, not the game's.
        if (!PlaytestPlaces.InWorld(grid, at))
        {
            trace("no teleport", F("{0:0},{1:0} is off the world", at.x, at.z));
            return;
        }
        var ground = new Vector3(at.x, grid.GroundAt(at) + 0.1f, at.z);
        bool hasBody = wizard.TryGetComponent<CharacterController>(out var body);
        if (hasBody) body.enabled = false;
        wizard.transform.position = ground;
        if (hasBody) body.enabled = true;
        TeleportedOnFrame = Frame;
        trace("teleport", F("{0:0},{1:0}", ground.x, ground.z));
    }

    // Casts a spell (and then a second one on the same cells) a few metres ahead, and checks a
    // chemistry spell changed something there.
    IEnumerator CastAhead(string spell, string then)
    {
        if (caster == null) yield break;
        var ahead = wizard.transform.position + Flat(wizard.transform.forward).normalized * dice.Range(3f, 9f);
        var cell = grid.CellAt(ahead);
        int dir = Dir(wizard.transform.forward);
        var chunk = grid.ChunkAt(ahead);
        string before = grid.IsChunkActive(chunk.x, chunk.y) ? grid.World.ChunkHash(chunk.x, chunk.y) : null;
        yield return CastOne(spell, cell, dir);
        if (then != null)
        {
            yield return Wait(1.5f);
            yield return CastOne(then, cell, dir);
        }
        yield return Wait(4f);
        var def = grid.World.Rules.Spell(spell);
        if (before != null && def != null && def.Element != null && grid.World.ChunkHash(chunk.x, chunk.y) == before)
            report(new Candidate("spell-nothing", spell, Category.Gameplay, Frame,
                $"{spell} at cell {cell.x},{cell.y} changed nothing there", lowConfidence: true));
    }

    IEnumerator CastOne(string spell, Vector2Int cell, int dir)
    {
        int charge = dice.Range(1, 3);
        trace("cast", F("{0} at {1},{2} dir {3} charge {4}", spell, cell.x, cell.y, dir, charge));
        if (!caster.Select(spell))
        {
            report(new Candidate("spell-missing", spell, Category.Gameplay, Frame, $"{spell} couldn't be selected"));
            yield break;
        }
        caster.Cast(spell, cell.x, cell.y, dir, charge);
        yield return null;
    }

    // N, E, S, W as the chemistry counts them: north (y-1) is +z.
    static int Dir(Vector3 forward)
    {
        if (Mathf.Abs(forward.z) >= Mathf.Abs(forward.x)) return forward.z >= 0 ? 0 : 2;
        return forward.x >= 0 ? 1 : 3;
    }

    // Walks to the nearest item, picks it up and throws it.
    IEnumerator Throw(float seconds)
    {
        var item = UnityEngine.Object.FindObjectsByType<Carryable>(FindObjectsSortMode.None)
            .OrderBy(c => (c.transform.position - wizard.transform.position).sqrMagnitude).ThenBy(c => c.name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (item == null || hands == null) yield break;
        yield return Visit(item.transform.position, seconds);
        // Only within reach: a wizard who didn't get there doesn't pick it up from afar.
        if (Flat(item.transform.position - wizard.transform.position).magnitude > Reach)
        {
            trace("no pickup", item.name + " is out of reach");
            yield break;
        }
        trace("pickup", item.name);
        hands.PickUp(item);
        yield return Wait(1f);
        float speed = dice.Range(6f, 14f);
        trace("throw", F("{0:0.0} m/s", speed));
        hands.Throw(speed);
        yield return Wait(3f);
    }

    // Walks to the nearest seat, sits a while, stands.
    IEnumerator Sit(float seconds)
    {
        var seat = Seat.Nearest(wizard.transform.position, 500f);
        if (seat == null || sitter == null) yield break;
        yield return Visit(seat.Foot, seconds);
        trace("sit", Place(seat.Foot));
        sitter.Sit(seat);
        yield return Wait(4f);
        trace("stand", "");
        sitter.Stand();
        yield return Wait(1f);
    }

    // Opens a menu page, then closes the menu; a menu that won't open or close is a candidate.
    IEnumerator Menu(string page)
    {
        var menu = PauseMenu.Instance;
        if (menu == null) yield break;
        trace("menu", page);
        if (page == "main") menu.SetOpen(true);
        else menu.ShowPage(page);
        yield return Wait(2f);
        if (!menu.Open) report(new Candidate("menu-not-open", page, Category.Gameplay, Frame, $"the {page} menu didn't open"));
        menu.SetOpen(false);
        yield return Wait(0.5f);
        if (menu.Open)
        {
            report(new Candidate("menu-stuck", page, Category.GameBreaking, Frame, $"the {page} menu didn't close"));
            menu.SetOpen(false);
        }
    }

    IEnumerator LookAround(float seconds)
    {
        float until = Time.time + seconds;
        var look = new Vector2(dice.Range(-1f, 1f), dice.Range(-0.2f, 0.2f)) * 3f;
        trace("look", F("{0:0.0},{1:0.0}", look.x, look.y));
        while (Time.time < until)
        {
            if (inputs != null) inputs.look = look;
            yield return null;
        }
    }

    static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    string Place(Vector3 p) => PlaytestPlaces.Near(places, grid, p);

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
}
