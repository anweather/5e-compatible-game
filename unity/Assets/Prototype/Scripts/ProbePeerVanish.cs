using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using Chem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

// The probe's peervanish scenario (playtest 2026-11-08 B1: the other player's body stopped being drawn after a lost Hold
// run, came back at a reset, and went again). This Editor hosts and the virtual player joins; the two stand together, a Hold
// run starts (a moment, so both peers go), the keep is put on its last point at the same net tick on both and the raiders
// take it, the host resets the world, and the client walks about for a while. At each step each peer says whether it
// draws the other's wizard (Drawn: its renderers on, not hidden, not shadows only, its bounds round its body, where the
// owner has it). Nothing here is in the game: the keep's last point is the probe's, put in by each peer itself.
public class PeerVanishProbe : RoomProbe
{
    const string Moment = "first-wave";
    const int Lead = 18;
    const float Settle = 1.5f, MostToLose = 150f, Later = 20f;
    // How far a drawn wizard may be from where its owner has it (m), and its bounds' centre from its feet.
    const float Lag = 1.5f, BoundsReach = 3f;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    /// <summary>
    /// Whether this peer draws a wizard: active, its body's renderers on and not hidden (Shapeshift's forceRenderingOff, the
    /// camera's shadows-only), on a layer the camera draws, and its bounds round where it stands. With what was found.
    /// </summary>
    public static (bool drawn, string why) Drawn(GameObject wizard)
    {
        if (wizard == null) return (false, "gone");
        var at = wizard.transform.position;
        var sb = new StringBuilder();
        sb.Append(string.Format(CultureInfo.InvariantCulture, "at {0:0.0},{1:0.0},{2:0.0}", at.x, at.y, at.z));
        if (!wizard.activeInHierarchy) return (false, sb.Append(" inactive").ToString());
        bool animal = wizard.TryGetComponent<Shapeshift>(out var shape) && shape.Shifted;
        if (animal) sb.Append(" animal");
        if (wizard.TryGetComponent<Vitals>(out var vitals) && vitals.IsDown) sb.Append(" down");
        var cam = Camera.main;
        int shown = 0, all = 0;
        foreach (var r in wizard.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.EndsWith("(silhouette)", StringComparison.Ordinal) || r is ParticleSystemRenderer) continue;
            all++;
            var why = Hidden(r, cam, at);
            if (why == null) shown++;
            else if (r is SkinnedMeshRenderer) sb.Append(' ').Append(r.name).Append(':').Append(why);
        }
        sb.Append($" {shown}/{all} shown");
        var anim = wizard.GetComponent<Animator>();
        if (anim != null && !anim.enabled) sb.Append(" animator off");
        var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hips != null)
        {
            var off = hips.position - at;
            sb.Append(string.Format(CultureInfo.InvariantCulture, " hips {0:0.0},{1:0.0},{2:0.0} off", off.x, off.y, off.z));
        }
        // An animal stands in for the model: drawn if the animal is.
        bool drawn = animal ? shape.DrawnFrames > 0 : shown > 0 && Body(wizard) is { } body && Hidden(body, cam, at) == null;
        return (drawn, sb.ToString());
    }

    static SkinnedMeshRenderer Body(GameObject wizard) =>
        wizard.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == "Armature_Mesh");

    // Why a renderer isn't drawn, or null.
    static string Hidden(Renderer r, Camera cam, Vector3 feet)
    {
        if (!r.gameObject.activeInHierarchy) return "inactive";
        if (!r.enabled) return "off";
        if (r.forceRenderingOff) return "forced off";
        if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return "shadows only";
        if (cam != null && (cam.cullingMask & (1 << r.gameObject.layer)) == 0) return $"layer {r.gameObject.layer} not drawn";
        var b = r.bounds;
        if (b.size.sqrMagnitude <= 0f || Vector3.Distance(b.center, feet) > BoundsReach + b.extents.magnitude)
            return string.Format(CultureInfo.InvariantCulture, "bounds at {0:0.0},{1:0.0},{2:0.0} size {3:0.0}", b.center.x, b.center.y, b.center.z, b.size.magnitude);
        return null;
    }

    /// <summary>Every other player's wizard on this peer, drawn or not: "player 2 drawn (at ...)", one each, ;-separated.</summary>
    public static string Others()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return "";
        return string.Join("; ", UnityEngine.Object.FindObjectsByType<NetWizard>(FindObjectsSortMode.None)
            .Where(w => w.IsSpawned && !w.IsOwner)
            .OrderBy(w => w.OwnerClientId)
            .Select(w =>
            {
                var (drawn, why) = Drawn(w.gameObject);
                return $"player {w.OwnerClientId + 1} {(drawn ? "drawn" : "NOT drawn")} ({why})";
            }));
    }

    /// <summary>This peer's own wizard's feet, "x,y,z".</summary>
    public static string Self()
    {
        var nm = NetworkManager.Singleton;
        var me = nm != null && nm.IsListening ? nm.SpawnManager.GetLocalPlayerObject() : null;
        if (me == null) return "";
        var p = me.transform.position;
        return string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00}", p.x, p.y, p.z);
    }

    /// <summary>The keep on its last point, put in by each peer straight after it has run <paramref name="tick"/>.</summary>
    public static IEnumerator LastPointAfter(NetChem net, int tick, Action<bool> done = null)
    {
        bool put = false;
        void On(int t, string hash)
        {
            if (t != tick) return;
            var world = ChemGrid.Current?.World;
            if (world?.Hold != null) world.Hold.Health = 1;
            put = true;
        }
        net.TickApplied += On;
        for (float t = 0f; t < 20f && !put; t += Time.unscaledDeltaTime) yield return null;
        net.TickApplied -= On;
        done?.Invoke(put);
    }

    GameObject Local => NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;

    // Each peer draws the other's wizard, and where its owner has it.
    IEnumerator Look(string name)
    {
        yield return Ask("report");
        if (Theirs == null) yield break;
        var here = Others();
        var there = Theirs.others;
        float gap = Gap(Theirs.self);
        Check($"{name}_host_draws_the_client", IsDrawn(here) && gap < Lag,
            $"{here}; the client has itself at {Theirs.self}, {gap:0.0} m off");
        Check($"{name}_client_draws_the_host", IsDrawn(there), $"{there}; the host at {Self()}");
    }

    // Others() named a wizard, and every one it named is drawn.
    static bool IsDrawn(string others) =>
        !string.IsNullOrEmpty(others) && others.Contains(" drawn (", StringComparison.Ordinal) && !others.Contains("NOT drawn", StringComparison.Ordinal);

    // How far the client's wizard here is from where the client has it.
    static float Gap(string self)
    {
        if (string.IsNullOrEmpty(self)) return float.MaxValue;
        var a = self.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var them = UnityEngine.Object.FindObjectsByType<NetWizard>(FindObjectsSortMode.None).FirstOrDefault(w => w.IsSpawned && !w.IsOwner);
        return them == null ? float.MaxValue : Vector3.Distance(them.transform.position, new Vector3(a[0], a[1], a[2]));
    }

    protected override IEnumerator Steps()
    {
        var here = grid.CellAt(Local.transform.position);
        yield return Ask("stand", $"{here.x + 3},{here.y}");
        if (Theirs == null) yield break;
        yield return Wait(Settle);
        yield return Look("joined");
        yield return OutOfSight("early");

        // A run, for everyone: each wizard gets up at the castle.
        var me = Local.GetComponent<SpellCaster>();
        Check("run_started", me.Moment(Moment), "the core refused the moment");
        for (float t = 0f; t < 10f && !World.HoldRunning; t += Time.unscaledDeltaTime) yield return null;
        Check("run_running", World.HoldRunning, World.Hold?.Phase.ToString() ?? "no hold");
        yield return Wait(3f);
        yield return Look("in_the_run");

        // The keep on its last point on both peers at one tick; the first blow takes it.
        int tick = Net.NetTick + Lead;
        bool putHere = false;
        StartCoroutine(LastPointAfter(Net, tick, p => putHere = p));
        yield return Ask("lastpoint", tick.ToString(CultureInfo.InvariantCulture), 20f);
        if (Theirs == null) yield break;
        for (float t = 0f; t < 20f && !putHere; t += Time.unscaledDeltaTime) yield return null;
        for (float t = 0f; t < MostToLose && World.Hold.Phase == HoldPhase.Running; t += Time.unscaledDeltaTime) yield return null;
        Check("run_lost", World.Hold.Phase == HoldPhase.Lost, World.Hold.Phase.ToString());
        // In front of the host's camera, for the film.
        var seen = Behind(-InFront);
        yield return Ask("stand", $"{seen.x},{seen.y}");
        if (Theirs == null) yield break;
        Roll();
        Keep(true);
        yield return Wait(3f);
        Keep(false);
        yield return Look("after_the_loss");

        // The host's reset, for everyone (as holding R or Back does).
        int lastReset = Net.LastResetTick;
        EventLog.RebuildYard();
        for (float t = 0f; t < 15f && Net.LastResetTick == lastReset; t += Time.unscaledDeltaTime) yield return null;
        Check("reset_happened", Net.LastResetTick > lastReset, $"last reset at {Net.LastResetTick}");
        yield return Wait(1f);
        Keep(true);
        yield return Wait(3f);
        Keep(false);
        yield return Look("after_the_reset");

        // The client walks off and back, and the two go on a while.
        yield return Ask("walk", "1,0,8");
        if (Theirs == null) yield break;
        yield return Look("after_a_walk");
        yield return Ask("walk", "-1,0,8");
        if (Theirs == null) yield break;
        yield return Wait(Later);
        yield return Look("later");

        yield return OutOfSight("late");
        yield return Punches();
        if (film != null)
        {
            yield return film.Stop();
            Metric("film_seconds", film.Seconds);
            Check("film_saved", film.Saved > 100, $"{film.Saved} of {film.Frames} frames");
        }
        HashesFrom = 0;
    }

    // The host punches the client beside it (B3): every punch the core says hit is felt by the puncher too (ItemHolder.FistLanded).
    IEnumerator Punches()
    {
        var me = Local;
        // Beside the host, square to its camera, so the film sees the fist land.
        var right = Camera.main.transform.right;
        right.y = 0f;
        var front = grid.CellAt(me.transform.position + right.normalized * PunchAhead);
        yield return Ask("stand", $"{front.x},{front.y}");
        if (Theirs == null) yield break;
        var target = grid.CellCenter(front.x, front.y);
        new WizardPuppet(me).Face(target);
        yield return Wait(Settle);
        Keep(true);
        var strike = me.GetComponent<WizardStrike>();
        var hands = me.GetComponent<ItemHolder>();
        string player = me.GetComponent<SpellCaster>().CastPlayer;
        int hits = 0, misses = 0, felt = hands.FistHits;
        void On(StrikeHit h)
        {
            if (h.Attacker != player) return;
            if (h.Hit) hits++;
            else misses++;
        }
        strike.Struck += On;
        // Real punches, the arm and all (ItemHolder.HandAct), each sent as a strike when the fist gets there.
        for (int i = 0; i < 8 && hits < 2; i++)
        {
            hands.HandAct("punch");
            yield return Wait(0.9f);
        }
        yield return Wait(1f);
        strike.Struck -= On;
        felt = hands.FistHits - felt;
        Metric("punch_hits", hits);
        Metric("punch_misses", misses);
        Check("puncher_feels_each_hit", hits > 0 && felt == hits, $"{hits} hits, {misses} misses, {felt} felt by the puncher");
    }

    IEnumerator OutOfSight(string name)
    {
        // Out of the host's sight (far behind its camera, past the shadows' reach), the client casts a few times, then
        // comes back in front: the host draws it again, its body where it stands.
        var away = Behind(Away);
        yield return Ask("stand", $"{away.x},{away.y}");
        if (Theirs == null) yield break;
        yield return Wait(Settle);
        unseen = watched = 0;
        mostOff = 0f;
        var watch = StartCoroutine(WatchHips());
        // Casts with the hands, held to charge too, as a player does: CastBeats lays its poses over the animation.
        yield return Ask("attune", "fire,earth");
        if (Theirs == null) yield break;
        yield return Ask("handcasts");
        if (Theirs == null) yield break;
        StopCoroutine(watch);
        Metric($"{name}_away_m", Vector3.Distance(Camera.main.transform.position, grid.CellCenter(away.x, away.y)));
        Metric($"{name}_away_frames_unseen", unseen);
        Metric($"{name}_away_frames", watched);
        Metric($"{name}_away_hips_most_off_m", mostOff);
        // Unseen, the Animator used to stop writing the bones and the poses laid over them each frame piled up.
        Check($"{name}_unseen_body_stays_whole", mostOff < 0.6f, $"hips up to {mostOff:0.00} m from where they belong over {watched} frames, {unseen} unseen");
        var front = Behind(-InFront);
        Keep(true);
        yield return Ask("stand", $"{front.x},{front.y}");
        if (Theirs == null) yield break;
        yield return Wait(Settle + 1f);
        Keep(false);
        yield return Look($"{name}_back_in_sight");
    }

    int unseen, watched;
    float mostOff;

    // With --shots, a film from the host's camera of the moments worth seeing (the clip): after the loss, after the reset,
    // the client back in sight, the punches.
    ClipFilm film;
    const float PunchAhead = 1.5f;

    void Roll()
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) return;
        film = new ClipFilm(this, ScreenshotDir) { Keeping = false };
        Check("film_sound_renders", film.Roll(), "AudioRenderer.Start");
    }

    void Keep(bool on)
    {
        if (film != null) film.Keeping = on;
    }

    // Each frame while the client is out of sight: whether its body is seen, and how far its hips are from where they belong.
    IEnumerator WatchHips()
    {
        while (true)
        {
            var them = FindObjectsByType<NetWizard>(FindObjectsSortMode.None).FirstOrDefault(w => w.IsSpawned && !w.IsOwner);
            var anim = them != null ? them.GetComponent<Animator>() : null;
            var hips = anim != null ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            var body = them != null ? Body(them.gameObject) : null;
            if (hips != null && body != null)
            {
                watched++;
                if (!body.isVisible) unseen++;
                mostOff = Mathf.Max(mostOff, Vector3.Distance(hips.position, them.transform.position + Vector3.up));
            }
            yield return null;
        }
    }

    const float Away = 90f, InFront = 8f;

    // A dry, open cell about this far behind the host's camera, flat (negative: in front of it).
    Vector2Int Behind(float metres)
    {
        var cam = Camera.main.transform;
        var back = -cam.forward;
        back.y = 0f;
        back.Normalize();
        var best = grid.CellAt(Local.transform.position);
        for (float m = metres; Mathf.Abs(m) > 1.5f; m *= 0.8f)
            for (int turn = 0; turn < 12; turn++)
            {
                var way = Quaternion.AngleAxis(turn * 10f * (turn % 2 == 0 ? 1 : -1), Vector3.up) * back;
                var cell = grid.CellAt(cam.position + way * m);
                if (!World.InBounds(cell.x, cell.y)) continue;
                var c = World.At(cell.x, cell.y);
                if (c.Water == 0 && !World.IsBlocking(c)) return cell;
            }
        return best;
    }
}
