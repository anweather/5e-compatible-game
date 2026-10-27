using System.Collections;
using System.Linq;
using Chem;
using Unity.Mathematics;
using UnityEngine;

// The probe's shapeshift scenario (docs/FAST_LOOP.md): out in the meadow, a
// deer is put in front of the wizard (held still), aimed at through a ray
// from the wizard's eyes (ValleyFauna.Aimed, Shapeshift.Aim) and Shapeshift
// cast at it. Then checks the wizard's model is hidden and a deer's body is
// drawn where it stands, with the deer's speeds; walks it so its gait runs;
// casting another spell is refused; a second deer close by doesn't bolt;
// casting changes it back with its own model and speeds; and shifted again,
// a heavy blow knocks it back into itself.
public class ShapeshiftProbe : ProbeScenario
{
    ValleyFauna fauna;
    Shapeshift shape;
    SpellDef spell;
    int deer, targetId;
    float wizardMove, wizardSprint;

    protected override IEnumerator Run()
    {
        fauna = FindFirstObjectByType<ValleyFauna>();
        shape = wizard.GetComponent<Shapeshift>();
        spell = World.Rules.Spell(SpellIds.Shapeshift);
        deer = fauna != null ? fauna.SpeciesIndex("Deer") : -1;
        bool ready = fauna != null && shape != null && spell != null && deer >= 0;
        Check("ready", ready, $"fauna {fauna != null}, Shapeshift part {shape != null}, spell {spell != null}, deer {deer}");
        if (!ready) yield break;

        yield return PutADeerInFront();
        (wizardMove, wizardSprint) = Speeds();
        yield return ShiftIntoTheDeer();
        CheckTheShape();
        yield return WalkAsADeer();
        yield return TryAnotherSpell();
        yield return ADeerCloseBy();
        yield return ChangeBack();
        yield return KnockedBack();
    }

    // The meadow of the fauna probe's parade; a deer 8 m north, held still, facing the wizard.
    IEnumerator PutADeerInFront()
    {
        var home = V(27f, 0f, 26f);
        Teleport(new Vector3(home.x, grid.GroundAt(home) + 0.05f, home.z));
        yield return Wait(0.5f);
        var w = wizard.transform.position;
        var at = w + Vector3.forward * 8f;
        int target = fauna.Spawn(deer, new float3(at.x, grid.GroundAt(at), at.z), math.PI);
        fauna.Puppet(target, math.PI, 0f);
        targetId = fauna.Id(target);
        puppet.Face(at);
        yield return null;
    }

    // Aimed at as the crosshair does (from the eyes to the deer's middle), then cast.
    IEnumerator ShiftIntoTheDeer()
    {
        var ray = RayAtDeer();
        int aimed = fauna.Aimed(ray, 60f);
        Check("aims_at_deer", aimed == Index(targetId), $"aimed at #{aimed}, the deer is #{Index(targetId)}");
        string why = shape.Aim(spell, new Ray(ray.origin, Vector3.up), out _);
        Check("aim_at_nothing_fizzles", why == "Aim at an animal to take its shape", why ?? "no message");
        why = shape.Aim(spell, ray, out var cell);
        Check("aim_ok", why == null, why ?? "ok");
        Check("bar_has_shapeshift", caster.Select(SpellIds.Shapeshift));
        caster.Cast(SpellIds.Shapeshift, cell.x, cell.y, 1);
        yield return Wait(1f);
    }

    // From the wizard's eyes to the held deer's middle (straight ahead if it's gone).
    Ray RayAtDeer()
    {
        var eye = wizard.transform.position + Vector3.up * 1.6f;
        int i = Index(targetId);
        var deerAt = i >= 0 ? (Vector3)fauna.State(i).Position + Vector3.up * 0.7f : eye + wizard.transform.forward * 8f;
        return new Ray(eye, deerAt - eye);
    }

    // An animal's index now (ValleyFauna's list shifts as animals far off are removed), or -1.
    int Index(int id)
    {
        for (int i = 0; i < fauna.Count; i++)
            if (fauna.Id(i) == id) return i;
        return -1;
    }

    void CheckTheShape()
    {
        Check("shifted", shape.Shifted && shape.Species == deer, shape.FormName ?? "not shifted");
        bool hidden = wizard.GetComponentsInChildren<Renderer>().All(r => r.forceRenderingOff);
        Check("model_hidden", shape.ModelHidden && hidden, $"model hidden {shape.ModelHidden}, every renderer off {hidden}");
        float off = math.distance(shape.Drawn.Position.xz, new float2(wizard.transform.position.x, wizard.transform.position.z));
        Check("deer_drawn", shape.DrawnFrames > 10 && off < 0.3f, $"{shape.DrawnFrames} frames, {off:0.00} m from the wizard");
        var (move, sprint) = Speeds();
        var expected = Shapeshift.SpeedsOf(fauna.Body(deer));
        Metric("move_speed", move);
        Metric("sprint_speed", sprint);
        Metric("capsule_height", wizard.GetComponent<CharacterController>().height);
        bool deerSpeeds = Mathf.Approximately(move, expected.move) && Mathf.Approximately(sprint, expected.sprint);
        Check("deer_speeds", deerSpeeds && !Mathf.Approximately(sprint, wizardSprint),
            $"move {move:0.0} sprint {sprint:0.0} (deer {expected.move:0.0} / {expected.sprint:0.0}; wizard {wizardMove:0.0} / {wizardSprint:0.0})");
    }

    // Walking: it moves, and the deer's gait runs.
    IEnumerator WalkAsADeer()
    {
        var before = wizard.transform.position;
        float phase = shape.Drawn.Phase, fastest = 0f;
        puppet.Move(Vector2.up);
        for (float t = 0f; t < 1.5f; t += Time.deltaTime)
        {
            fastest = Mathf.Max(fastest, shape.Drawn.Speed);
            yield return null;
        }
        puppet.Move(Vector2.zero);
        var moved = wizard.transform.position - before;
        float walked = new Vector2(moved.x, moved.z).magnitude;
        Metric("walked_m", walked);
        Check("walks_with_gait", walked > 2f && fastest > 1f && !Mathf.Approximately(phase, shape.Drawn.Phase),
            $"{walked:0.0} m, drawn speed up to {fastest:0.0} m/s, phase {phase:0.00} → {shape.Drawn.Phase:0.00}");
    }

    IEnumerator TryAnotherSpell()
    {
        int tick = World.Tick;
        int mark = ConsoleMark;
        var here = grid.CellAt(wizard.transform.position);
        caster.Cast(SpellIds.FireBolt, here.x + 3, here.y, 1);
        yield return Wait(1.5f);
        bool cast = LogSince(tick).Any(l => l.Contains("Fire Bolt", System.StringComparison.Ordinal));
        Check("casting_refused", !cast && ConsoleSaid("can't cast", mark), string.Join(" / ", ConsoleSince(mark).Take(3)));
    }

    // A deer 6 m away takes it for one of its own (an unshifted wizard this close makes one bolt: the fauna probe).
    IEnumerator ADeerCloseBy()
    {
        var w = wizard.transform.position;
        var at = w + Vector3.right * 6f;
        var near = new float3(at.x, grid.GroundAt(at), at.z);
        int otherId = fauna.Id(fauna.Spawn(deer, near, -math.PI / 2f));
        yield return Wait(3f);
        int other = Index(otherId);
        if (other < 0) Check("deer_stays", false, "the near deer was despawned");
        else
        {
            var s = fauna.State(other);
            float drifted = math.distance(s.Position.xz, near.xz);
            Metric("near_deer_moved_m", drifted);
            Check("deer_stays", drifted < 5f && s.Mode != Punchy.Fauna.CreatureMode.Flee, $"{drifted:0.0} m in 3 s ({s.Mode})");
        }

        // The picture: the wizard as a deer beside a deer.
        Cut(w + new Vector3(3f, 1.4f, -4.5f), w + new Vector3(3f, 0.6f, 0f));
        yield return Wait(0.5f);
        Shot("deer");
    }

    // Cast changes back: its own model and speeds.
    IEnumerator ChangeBack()
    {
        shape.ChangeBackByChoice();
        yield return null;
        var (move, sprint) = Speeds();
        bool shown = wizard.GetComponentsInChildren<Renderer>().All(r => !r.forceRenderingOff);
        bool speeds = Mathf.Approximately(move, wizardMove) && Mathf.Approximately(sprint, wizardSprint);
        Check("changed_back", !shape.Shifted && !shape.ModelHidden && shown && speeds,
            $"shifted {shape.Shifted}, model shown {shown}, move {move:0.0} sprint {sprint:0.0}, capsule {wizard.GetComponent<CharacterController>().height:0.00} m");
    }

    // Shifted again, a heavy blow knocks it back into itself.
    IEnumerator KnockedBack()
    {
        shape.Aim(spell, RayAtDeer(), out var cell);
        caster.Cast(SpellIds.Shapeshift, cell.x, cell.y, 1);
        yield return Wait(1f);
        bool again = shape.Shifted;
        wizard.GetComponent<Vitals>().TakeDamage(100f);
        yield return null;
        Check("blow_changes_back", again && !shape.Shifted, $"shifted again {again}, after the blow {shape.Shifted}");
    }

    (float move, float sprint) Speeds()
    {
        var mover = wizard.GetComponent(WizardRig.ControllerComponent);
        var move = WizardRig.Field(mover, "MoveSpeed", isPublic: true);
        var sprint = WizardRig.Field(mover, "SprintSpeed", isPublic: true);
        return move == null || sprint == null ? (0f, 0f) : ((float)move.GetValue(mover), (float)sprint.GetValue(mover));
    }
}
