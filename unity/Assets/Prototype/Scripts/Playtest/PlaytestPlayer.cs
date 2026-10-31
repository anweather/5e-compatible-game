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
// it), casts through the SpellCaster (by elements too, through the same
// ComboCommand as the pad and keys, and in chains checked against the cells),
// picks up and throws, sits, opens the menus. Every choice comes from the
// run's dice and every decision goes into the trace, so the same seed on the
// same build does the same thing.
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
    readonly Func<string, string> shoot;
    readonly SurfaceEffects surface;

    public PlaytestPlayer(ChemGrid grid, GameObject wizard, PlaytestRandom dice, List<(string, Vector3)> places,
        Action<string, string> trace, Action<Candidate> report, Func<string, string> shoot)
    {
        this.grid = grid;
        this.wizard = wizard;
        this.dice = dice;
        this.places = places;
        this.trace = trace;
        this.report = report;
        this.shoot = shoot;
        inputs = wizard.GetComponent<StarterAssetsInputs>();
        caster = wizard.GetComponent<SpellCaster>();
        hands = wizard.GetComponent<ItemHolder>();
        sitter = wizard.GetComponent<Sitter>();
        surface = wizard.GetComponent<SurfaceEffects>();
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
            ElementCasts = caster != null ? Chains.ElementCasts(rules.Combos).ToList() : new List<string>(),
            ChainIds = caster != null ? Chains.Possible(rules.Combos).Select(c => c.Id).ToList() : new List<string>(),
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
            case GoalKind.Elements: yield return CastElements(goal.Target); break;
            case GoalKind.Chain: yield return PlayChain(Chains.ById(goal.Target)); break;
            case GoalKind.Throw: yield return Throw(goal.Seconds); break;
            case GoalKind.Sit: yield return Sit(goal.Seconds); break;
            case GoalKind.Menu: yield return Menu(goal.Target); break;
            default: yield return LookAround(goal.Seconds); break;
        }
        Stop();
        yield return Level();
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
    // chemistry spell changed something where it reaches (Watched), waiting for that as long as a
    // cast's result may take to show.
    IEnumerator CastAhead(string spell, string then)
    {
        if (caster == null) yield break;
        var ahead = wizard.transform.position + Flat(wizard.transform.forward).normalized * dice.Range(3f, 9f);
        var cell = grid.CellAt(ahead);
        int dir = Dir(wizard.transform.forward);
        var watched = Watched(spell, cell, dir);
        if (then != null) watched.UnionWith(Watched(then, cell, dir));
        string before = HashIfActive(watched);
        yield return CastOne(spell, cell, dir);
        if (then != null)
        {
            yield return Wait(1.5f);
            yield return CastOne(then, cell, dir);
        }
        yield return UntilChanged(watched, before, ResultSeconds);
        var def = grid.World.Rules.Spell(spell);
        if (before != null && def != null && def.Element != null && HashIfActive(watched) == before)
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

    // --- casting by elements (docs/SPELL_COMBOS.md) ---------------------------------------

    // How long a meter may take to run out (the longest cast is 18 ticks), and how long a cast's
    // result may take to show in the cells (a projectile's flight, a wave rolling out).
    const float MeterSeconds = 6f, ResultSeconds = 6f;
    // Where a chain or a cast by elements aims: this many cells ahead, within Frost's and Wave's reach.
    const float AheadMinCells = 4f, AheadMaxCells = 6.5f;
    // A Geyser's prop counts as thrown when it rises this far (metres); props further than this aren't walked to.
    const float ThrownRise = 0.5f, PropSearch = 60f;

    // A cast by elements a few cells ahead, as a player makes it: each element pressed in order, the
    // last perhaps held to charge, the meter left to run out, the cast going off where it's aimed.
    // Then a shot, and a check that a chemistry spell changed something there.
    IEnumerator CastElements(string combo)
    {
        if (caster == null) yield break;
        var at = Ahead();
        var cell = grid.CellAt(at);
        var world = grid.World;
        var cast = world.Rules.Combos.Combo(combo);
        int charge = Chains.ChargeFor(dice);
        var def = world.Rules.Spell(cast?.SpellAt(charge, world.MaxChargeOf(cast)));
        var watched = Watched(def?.Id, cell, Dir(wizard.transform.forward));
        string before = HashIfActive(watched);
        bool went = false;
        yield return Press(combo, at, ok => went = ok, charge);
        if (!went) yield break;
        yield return Wait(ResultSeconds);
        shoot("elements-" + combo.Replace('+', '-'));
        if (before != null && def?.Element != null && HashIfActive(watched) == before)
            report(new Candidate("elements-nothing", combo, Category.Gameplay, Frame,
                $"{combo} ({def.Id}) at cell {cell.x},{cell.y} changed nothing there", lowConfidence: true));
    }

    // Presses a combo's elements through the caster (the same ComboCommand as the pad and the keys),
    // a frame apart so the second lands inside the window, with the cast aimed at a point; holds the
    // last to the charge asked for, and waits for the meter to run out. Tells whether it went off
    // (it's in the grimoire and nothing is loaded).
    IEnumerator Press(string combo, Vector3 at, Action<bool> went, int charge = 1)
    {
        var world = grid.World;
        string player = caster.CastPlayer;
        // A cast still draining would take the press as its second element.
        float until = Time.time + MeterSeconds;
        while (world.CastOf(player)?.Casting == true && Time.time < until) yield return null;
        var cell = grid.CellAt(at);
        trace("elements", F("{0} at {1},{2} charge {3}", combo, cell.x, cell.y, charge));
        caster.AimOverride = at;
        var elements = Chains.ElementsOf(combo);
        for (int i = 0; i < elements.Length; i++)
        {
            if (i > 0) yield return null;
            if (caster.LoadElement(elements[i], charge > 1 && i == elements.Length - 1)) continue;
            report(new Candidate("element-refused", combo, Category.Gameplay, Frame, $"loading {elements[i]} for {combo} was refused"));
            caster.AimOverride = null;
            went(false);
            yield break;
        }
        if (charge > 1) yield return HoldTo(charge, player);
        until = Time.time + MeterSeconds;
        while (world.CastOf(player)?.Casting == true && Time.time < until) yield return null;
        // The caster aims and casts in the tick the meter ran out; give it the frame.
        yield return null;
        caster.AimOverride = null;
        bool ok = world.CastOf(player)?.Casting != true && world.KnowsCombo(player, combo);
        if (!ok) report(new Candidate("elements-didnt-cast", combo, Category.Gameplay, Frame, $"{combo} was loaded but never went off"));
        went(ok);
    }

    // Lets go once the meter has reached this charge (or it's taken too long).
    IEnumerator HoldTo(int charge, string player)
    {
        float until = Time.time + MeterSeconds;
        while (grid.World.HeldCharge(grid.World.CastOf(player)) < charge && Time.time < until) yield return null;
        caster.LetGo();
    }

    // A chain: the setup cast by elements, then the trigger aimed at what it left, and the cells
    // checked for the rules' result (Playtest.Chains), with a shot of it either way.
    IEnumerator PlayChain(Chain chain)
    {
        if (chain == null || caster == null) yield break;
        PhysicsProp prop = null;
        if (chain.UnderProp)
        {
            prop = NearestProp();
            if (prop == null)
            {
                trace("no chain", F("{0}: no prop within {1:0} m", chain.Name, PropSearch));
                yield break;
            }
            var from = Flat(wizard.transform.position - prop.transform.position).normalized;
            yield return Visit(prop.transform.position + from * (AheadMinCells * grid.cellSize), 30f);
        }
        var target = grid.CellAt(prop != null ? prop.transform.position : Ahead());
        bool went = false;
        yield return Press(chain.Setup, CellPoint(target.x, target.y), ok => went = ok);
        if (!went) yield break;

        (int x, int y)? aim = null;
        float until = Time.time + ResultSeconds;
        while ((aim = chain.Find(grid.World, target.x, target.y)) == null && Time.time < until) yield return null;
        if (aim == null)
        {
            trace("chain", $"{chain.Name}: {chain.Setup} left nothing to work on");
            report(new Candidate("chain-setup-nothing", chain.Id, Category.Gameplay, Frame,
                $"{chain}: the setup left nothing near cell {target.x},{target.y}", lowConfidence: true));
            yield break;
        }

        var (ax, ay) = aim.Value;
        int since = grid.World.Tick;
        float propFrom = prop != null ? prop.transform.position.y : 0f, propTop = propFrom;
        yield return Press(chain.Trigger, CellPoint(ax, ay), ok => went = ok, chain.TriggerCharge);
        if (!went) yield break;
        bool done = false;
        until = Time.time + ResultSeconds;
        // With a prop, the whole wait: it may be thrown after the cells show the jet.
        while (Time.time < until && !(done && prop == null))
        {
            done |= chain.Done(grid.World, ax, ay, since);
            if (prop != null) propTop = Mathf.Max(propTop, prop.transform.position.y);
            yield return null;
        }
        trace("chain", done ? $"{chain.Name}: {chain.Expect}" : $"{chain.Name}: not seen");
        shoot("chain-" + chain.Id);
        if (!done)
            report(new Candidate("chain-nothing", chain.Id, Category.Gameplay, Frame,
                $"{chain}: expected {chain.Expect} near cell {ax},{ay}", lowConfidence: true));
        if (done && prop != null && propTop - propFrom < ThrownRise)
            report(new Candidate("geyser-prop-stayed", prop.kind, Category.Physics, Frame,
                F("the geyser at cell {0},{1} didn't throw the {2} (it rose {3:0.0} m)", ax, ay, prop.kind, propTop - propFrom), lowConfidence: true));
        if (done && chain.WalkAcross) yield return WalkAcross(CellPoint(ax, ay));
    }

    // Walks over the ice and a little past it; swimming on the way means the bridge didn't hold.
    IEnumerator WalkAcross(Vector3 ice)
    {
        var past = ice + Flat(ice - wizard.transform.position).normalized * (2f * grid.cellSize);
        trace("walk across", Place(past));
        bool swam = false;
        float until = Time.time + 10f;
        while (Time.time < until && Flat(past - wizard.transform.position).magnitude > 1f)
        {
            Steer(Flat(past - wizard.transform.position), false);
            swam |= surface != null && surface.Swimming;
            yield return null;
        }
        Stop();
        if (swam)
            report(new Candidate("ice-bridge-swam", Place(ice), Category.Gameplay, Frame,
                "walking across Frost's ice, the wizard was swimming", lowConfidence: true));
    }

    // A point a few cells ahead of the wizard, on the ground.
    Vector3 Ahead()
    {
        var at = wizard.transform.position + Flat(wizard.transform.forward).normalized * (dice.Range(AheadMinCells, AheadMaxCells) * grid.cellSize);
        var cell = grid.CellAt(at);
        return CellPoint(cell.x, cell.y);
    }

    Vector3 CellPoint(int x, int y)
    {
        var c = grid.CellCenter(x, y);
        return new Vector3(c.x, grid.SurfaceY(x, y), c.z);
    }

    // The chunks a cast aimed at a cell can change: every one under its shape at the highest charge
    // and its scatter, and the caster's (a line from self starts there). A burst near a chunk's edge
    // reaches into the next, so the aimed cell's chunk alone can miss what it did.
    HashSet<Vector2Int> Watched(string spellId, Vector2Int cell, int dir)
    {
        var chunks = new HashSet<Vector2Int> { grid.ChunkAt(CellPoint(cell.x, cell.y)), grid.ChunkAt(wizard.transform.position) };
        var def = spellId != null ? grid.World.Rules.Spell(spellId) : null;
        if (def == null) return chunks;
        var cells = grid.World.SpellCells(def, cell.x, cell.y, dir, World.MaxCharge);
        if (cells.Count == 0) return chunks;
        int reach = def.Scatter?.Reach ?? 0;
        int x0 = cells.Min(c => c.x) - reach, x1 = cells.Max(c => c.x) + reach;
        int y0 = cells.Min(c => c.y) - reach, y1 = cells.Max(c => c.y) + reach;
        for (int cy = Chunks.ChunkOf(y0); cy <= Chunks.ChunkOf(y1); cy++)
            for (int cx = Chunks.ChunkOf(x0); cx <= Chunks.ChunkOf(x1); cx++) chunks.Add(new Vector2Int(cx, cy));
        return chunks;
    }

    // The watched chunks' hashes where they're simulated (an inactive chunk is held as it is: no change
    // to see), in a fixed order; null when none is.
    string HashIfActive(HashSet<Vector2Int> chunks)
    {
        var active = chunks.Where(c => grid.IsChunkActive(c.x, c.y)).OrderBy(c => c.y).ThenBy(c => c.x).ToList();
        return active.Count == 0 ? null : string.Join(" ", active.Select(c => grid.World.ChunkHash(c.x, c.y)));
    }

    // Waits until the watched chunks change (the cast landed and did something) or the time is up,
    // looking a few times a second (a chunk's hash reads all its cells).
    IEnumerator UntilChanged(HashSet<Vector2Int> chunks, string before, float seconds)
    {
        float until = Time.time + seconds;
        while (before != null && Time.time < until && HashIfActive(chunks) == before) yield return Wait(HashEvery);
    }

    const float HashEvery = 0.25f;

    // The nearest prop within a walk, by distance then name (the same on every run).
    PhysicsProp NearestProp() =>
        UnityEngine.Object.FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None)
            .Where(p => Flat(p.transform.position - wizard.transform.position).magnitude < PropSearch)
            .OrderBy(p => (p.transform.position - wizard.transform.position).sqrMagnitude).ThenBy(p => p.name, StringComparer.Ordinal)
            .FirstOrDefault();

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
        // Turning only: a tilt held for seconds left the camera on the ground or the sky for minutes (0.1.8 runs).
        var look = new Vector2(dice.Range(-1f, 1f), 0f) * 3f;
        trace("look", F("{0:0.0},{1:0.0}", look.x, look.y));
        while (Time.time < until)
        {
            if (inputs != null) inputs.look = look;
            yield return null;
        }
    }

    // The camera's tilt the bot keeps between goals (degrees below level), and how near is near enough.
    const float LevelPitch = 12f, LevelWithin = 6f, LevelSeconds = 1.5f;

    // Brings the camera back to a little below level through the look stick, the way a player
    // would, so the next goal's shot shows the world and not the ground or the sky. Which way the
    // stick tilts the camera depends on the device, so it reverses if the tilt grows.
    IEnumerator Level()
    {
        if (inputs == null || Camera.main == null) yield break;
        float sign = -1f, until = Time.time + LevelSeconds;
        float off = Pitch() - LevelPitch;
        while (Mathf.Abs(off) > LevelWithin && Time.time < until)
        {
            inputs.look = new Vector2(0f, sign * Mathf.Clamp(off * 0.1f, -3f, 3f));
            yield return null;
            float now = Pitch() - LevelPitch;
            if (Mathf.Abs(now) > Mathf.Abs(off) + 0.5f) sign = -sign;
            off = now;
        }
        inputs.look = Vector2.zero;
    }

    // The main camera's tilt, degrees below level (negative above).
    static float Pitch() => Mathf.DeltaAngle(0f, Camera.main.transform.eulerAngles.x);

    static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    string Place(Vector3 p) => PlaytestPlaces.Near(places, grid, p);

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
}
