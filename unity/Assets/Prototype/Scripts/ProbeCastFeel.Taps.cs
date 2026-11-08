using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The castfeel probe's hand taps (J-078, casting-taps): a hand's button let go at once goes off tapTicks after
// its press, so its beats must keep up: a quick wind-up (CastFeel.handTapWindUpSeconds), the slam and the thrust, and taps on each
// other's heels each show theirs. A hand held down still gathers on its row's castTicks and charges.
public partial class CastFeelProbe
{
    // Frames at 60 fps: a tick is 10. From the press to the thrust: the tap's two ticks and the gesture's release.
    const int TapThrustFrames = 45;

    bool Hand(int hand, bool down, bool bumper = true)
    {
        string me = caster.CastPlayer;
        var command = down ? ComboCommand.HandDown(me, hand, bumper) : ComboCommand.HandUp(me, hand);
        var net = grid.GetComponent<NetChem>();
        if (net == null || !net.Online) return World.ApplyCombo(command);
        net.RequestCombo(command);
        return true;
    }

    IEnumerator Taps()
    {
        var hands = World.Rules.Sim.Hold.Hands;
        if (hands.TapTicks <= 0)
        {
            Check("hand_taps", true, "no taps in these rules");
            yield break;
        }
        string me = caster.CastPlayer;
        FixClock(true);
        ChemGrid.ExactFrames = true;
        yield return HandsRested(me);
        yield return OneTap(me, hands);
        yield return HandsRested(me);
        yield return HeldHand(me, hands);
        yield return HandsRested(me);
        ChemGrid.ExactFrames = false;
        yield return SpamTaps(me, hands);
        FixClock(false);
    }

    // One tap, pressed on a tick and let go the frame after.
    IEnumerator OneTap(string me, HandsDef hands)
    {
        beatFrames.Clear();
        firedCasts.StartFrom(World);
        int pressed = -1, fired = -1;
        void OnTick()
        {
            if (pressed < 0 && Hand(HandsDef.Right, true)) pressed = Time.frameCount;
            if (fired < 0 && firedCasts.Take(World).Any(f => f.Player == me)) fired = Time.frameCount;
        }
        void SawWindUp(Beat beat) => sawHandTap |= beat == Beat.WindUp && beats.HandTap;
        sawHandTap = false;
        beats.BeatChanged += SawWindUp;
        grid.TickDone += OnTick;
        while (pressed < 0) yield return null;
        yield return null;
        Hand(HandsDef.Right, false);
        Check("a_tap_is_a_tap", World.HandsOf(me).Tap[HandsDef.Right] && World.CastOf(me)?.Total == hands.TapTicks, $"tap {World.HandsOf(me).Tap[HandsDef.Right]}, meter {World.CastOf(me)?.Total}");
        yield return AtBeat(Beat.Neutral, 0.3f);
        grid.TickDone -= OnTick;
        beats.BeatChanged -= SawWindUp;
        var seenTap = beatFrames.Select(b => b.beat).ToList();
        int thrust = beatFrames.FirstOrDefault(b => b.beat == Beat.Release).frame;
        Metric("tap_fires_frames", fired - pressed);
        Metric("tap_thrust_frames", thrust - pressed);
        Check("a_tap_fires_on_its_ticks", fired - pressed <= (hands.TapTicks + 1) * 10, $"{fired - pressed} frames from the press");
        int windUp = beatFrames.FirstOrDefault(b => b.beat == Beat.WindUp).frame, slam = beatFrames.FirstOrDefault(b => b.beat == Beat.Slam).frame;
        int windUpFrames = Mathf.CeilToInt(beats.feel.handTapWindUpSeconds * 60f - 0.01f);
        Check("a_tap_slams_and_thrusts", seenTap.Contains(Beat.Slam) && seenTap.Contains(Beat.Release), string.Join(" ", seenTap));
        Check("a_taps_wind_up_is_quick", sawHandTap && (windUp == 0 || slam - windUp >= windUpFrames), $"hand tap seen {sawHandTap}, wind-up {slam - windUp} frames of {windUpFrames}: {Frames()}");
        Check("a_tap_thrusts_quickly", thrust > pressed && thrust - pressed <= TapThrustFrames, $"thrust {thrust - pressed} frames after the press: {Frames()}");
    }

    // A held hand: it gathers on its row's castTicks, as before, then charges.
    IEnumerator HeldHand(string me, HandsDef hands)
    {
        beatFrames.Clear();
        int pressed = -1;
        void OnHold()
        {
            if (pressed < 0 && Hand(HandsDef.Right, true)) pressed = Time.frameCount;
        }
        grid.TickDone += OnHold;
        while (pressed < 0) yield return null;
        grid.TickDone -= OnHold;
        for (float t = 0f; beats.Now != Beat.Charge && t < WaitLimit; t += Time.deltaTime) yield return null;
        int charged = Time.frameCount - pressed;
        var row = hands.One(World.HandHolds(me, HandsDef.Right), HandsDef.Throw);
        Metric("hold_charge_frames", charged);
        Check("a_held_hand_still_charges", beats.Now == Beat.Charge && !World.HandsOf(me).Tap[HandsDef.Right], $"{beats.Now} after {charged} frames");
        Check("a_held_hand_keeps_its_wind_up", charged >= (row.CastTicks - 1) * 10, $"charge at {charged} frames, castTicks {row.CastTicks}");
        Hand(HandsDef.Right, false);
        yield return AtBeat(Beat.Neutral, 0.3f);
    }

    // Taps as fast as the hand takes them: each goes off, each slams and thrusts.
    IEnumerator SpamTaps(string me, HandsDef hands)
    {
        const int Casts = 5;
        beatFrames.Clear();
        struckSlams = 0;
        beats.Struck += CountSlam;
        firedCasts.StartFrom(World);
        int firedTaps = 0, firstTick = -1, lastTick = -1;
        void CountTap()
        {
            if (firedCasts.Take(World).All(f => f.Player != me)) return;
            if (firstTick < 0) firstTick = World.Tick;
            lastTick = World.Tick;
            firedTaps++;
        }
        grid.TickDone += CountTap;
        for (int i = 0; i < Casts; i++)
        {
            for (float t = 0f; (World.HandCooling(me, HandsDef.Right) > 0 || World.HandsOf(me).Casting || World.TickInProgress) && t < WaitLimit; t += Time.deltaTime) yield return null;
            Hand(HandsDef.Right, true);
            yield return null;
            Hand(HandsDef.Right, false);
            yield return null;
        }
        for (float t = 0f; firedTaps < Casts && t < WaitLimit; t += Time.deltaTime) yield return null;
        yield return AtBeat(Beat.Neutral, 0.3f);
        grid.TickDone -= CountTap;
        beats.Struck -= CountSlam;
        int slams = beatFrames.Count(b => b.beat == Beat.Slam), releases = beatFrames.Count(b => b.beat == Beat.Release);
        float perTap = firedTaps > 1 ? (lastTick - firstTick) / (float)(firedTaps - 1) : 0f;
        Metric("tap_spam_ticks_apart", perTap);
        Check("taps_spam", firedTaps == Casts && perTap <= hands.TapTicks + hands.TapCooldown + 1, $"{firedTaps} of {Casts}, {perTap:0.0} ticks apart");
        Check("spammed_taps_each_slam_and_thrust", slams == Casts && struckSlams == Casts && releases == Casts, $"{slams} slams, {struckSlams} struck, {releases} thrusts: {string.Join(" ", beatFrames.Select(b => b.beat))}");
    }

    bool sawHandTap;

    string Frames() => string.Join(" ", beatFrames.Select(b => $"{b.beat}@{b.frame}"));

    IEnumerator HandsRested(string me)
    {
        for (float t = 0f; t < WaitLimit && (World.HandsOf(me).Busy || World.HandCooling(me, HandsDef.Left) > 0 || World.HandCooling(me, HandsDef.Right) > 0); t += Time.deltaTime) yield return null;
        yield return AtBeat(Beat.Neutral, 0.2f);
    }
}
