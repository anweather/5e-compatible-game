using Chem;
using UnityEngine;

// The hands (D16, docs/design/unified-casting.md; the Hold's side in the-hold.md, phase 3): the wizard's side of World.Hands,
// everywhere but the debug layout (F2). A hand's button goes down as a HandDown, and the hand charges in the core while it's held;
// letting go of the hand's buttons sends its HandUp, and the core sets it off at the charge it reached (a tap is charge 1). Both of a
// hand's buttons are its sweep. Both hands making the same gesture merge where the table has a row for what they hold, and the core
// sets that off at the lower charge on either release; anything else goes off hand by hand, so one hand can tap while the other
// charges. What comes out goes off like any other cast (FiredCastsHeard): a spell aimed from the crosshair, pulled back to its
// gesture's range, or an empty hand's act. Attuning is the spell list's (AttuneTo), anywhere but a Hold run and never mid-cast; in
// the attuning room a hand's trigger still moves it to the next element. A locked hand or gesture says so when pressed, buzzes, and
// shows a lock on its icon (CastHud).
public partial class SpellCaster
{
    /// <summary>Where the last hand spell was aimed (the probe).</summary>
    public Vector2Int LastHandAim { get; private set; }

    // The hands whose HandDown went out while their buttons are still down: each sends its HandUp as they let go.
    readonly bool[] handHeld = new bool[HandsDef.Count];
    readonly float[] lockedAt = { float.NegativeInfinity, float.NegativeInfinity };
    static readonly CastFeel.Thump LockedBuzz = new(0f, 0.6f, 0.12f);

    /// <summary>When a press of this hand was last refused as locked, by the unscaled clock (CastHud's lock).</summary>
    public float LockedAt(int hand) => lockedAt[hand];

    void Hands(string player, bool off)
    {
        // Read from the buttons, not their release this frame: whatever turns the controls off lets go too, or a hand would
        // charge for ever. Before the presses, so a tap inside one frame lets go on the next.
        for (int hand = 0; hand < HandsDef.Count; hand++)
            if (handHeld[hand] && (off || !HandButtonDown(hand)))
            {
                handHeld[hand] = false;
                Send(ComboCommand.HandUp(player, hand));
            }
        if (off) return;
        bool thrustLeft = controls.ThrustLeft.WasPressedThisFrame(), thrustRight = controls.ThrustRight.WasPressedThisFrame();
        var tower = grid.World.TowerOf(player);
        if (AttuneAtTower(player, tower, HandsDef.Left, thrustLeft)) thrustLeft = false;
        if (AttuneAtTower(player, tower, HandsDef.Right, thrustRight)) thrustRight = false;
        Press(player, HandsDef.Right, thrustRight, controls.ThrowRightPressed());
        Press(player, HandsDef.Left, thrustLeft, controls.ThrowLeft.WasPressedThisFrame());
    }

    // A hand's buttons pressed this frame: the thrust, the throw, or the throw with the thrust down, its sweep (the throw that makes
    // one isn't a locked throw). The core makes a sweep of either order inside the window.
    void Press(string player, int hand, bool thrust, bool thrown)
    {
        var thrustButton = hand == HandsDef.Left ? controls.ThrustLeft : controls.ThrustRight;
        if (thrust && Open(player, hand, HandsDef.Thrust) && Send(ComboCommand.HandDown(player, hand))) handHeld[hand] = true;
        if (thrown && Open(player, hand, thrust || thrustButton.IsPressed() ? HandsDef.Sweep : HandsDef.Throw) && Send(ComboCommand.HandDown(player, hand, bumper: true)))
            handHeld[hand] = true;
    }

    bool HandButtonDown(int hand) => hand == HandsDef.Left
        ? controls.ThrustLeft.IsPressed() || controls.ThrowLeft.IsPressed()
        : controls.ThrustRight.IsPressed() || controls.ThrowRight.IsPressed();

    // A cancel drops what the hands hold: the buttons still down send nothing.
    void DropHeld() => System.Array.Clear(handHeld, 0, handHeld.Length);

    /// <summary>
    /// The charge the hands' held gestures have built: the most of either hand held, or merged, the lower of the two, which is
    /// what a merge goes off at. 1 with none held (CastBeats' levels, the marker).
    /// </summary>
    public static int HeldHandCharge(World world, string player)
    {
        var h = world.HandsOf(player);
        int left = world.HandCharge(player, HandsDef.Left), right = world.HandCharge(player, HandsDef.Right);
        if (world.CastOf(player)?.Merge != null) return Mathf.Max(1, Mathf.Min(left, right));
        int most = 1;
        if (h.Down[HandsDef.Left]) most = Mathf.Max(most, left);
        if (h.Down[HandsDef.Right]) most = Mathf.Max(most, right);
        return most;
    }

    const float LockedEvery = 2f;
    float lockedSaidAt = float.NegativeInfinity;

    /// <summary>What the player last heard about something locked (the probe).</summary>
    public string LockedLine { get; private set; }

    // Whether this hand and gesture are the wizard's; if not it hears why with a buzz, and the press goes nowhere as it would in the core.
    bool Open(string player, int hand, int gesture)
    {
        var world = grid.World;
        string why = null;
        if (!world.HandOpen(player, hand)) why = Locked($"Your {Side(hand)} hand", world, player, u => u.Hand == Side(hand));
        else if (!world.GestureOpen(player, gesture)) why = Locked($"The {HandsDef.GestureName(gesture)}", world, player, u => u.Gesture == gesture);
        if (why == null) return true;
        lockedAt[hand] = Time.unscaledTime;
        PadRumble.Play(LockedBuzz, 1f);
        if (Time.unscaledTime - lockedSaidAt > LockedEvery || why != LockedLine)
        {
            lockedSaidAt = Time.unscaledTime;
            LockedLine = why;
            EventLog.Add(why);
        }
        return false;
    }

    static string Side(int hand) => hand == HandsDef.Left ? "left" : "right";

    static string Locked(string what, World world, string player, System.Func<UnlockDef, bool> gives)
    {
        var row = world.UnlockGiving(gives);
        int away = row != null ? world.PointsAway(player, row) : 0;
        return $"{what} is locked: {away} more {(away == 1 ? "point" : "points")}";
    }

    /// <summary>Picks an upgrade from this wizard's offer (ProgressKeeper).</summary>
    public bool PickUpgrade(string id) => grid != null && grid.World != null && Send(ComboCommand.Pick(CastPlayer, id));

    /// <summary>Tells every peer this wizard's points (ProgressKeeper).</summary>
    public bool BringPoints(int points) => grid != null && grid.World != null && Send(ComboCommand.Bring(CastPlayer, points));

    /// <summary>Goes into a moment, for every peer (HoldMoments).</summary>
    public bool Moment(string id) => ComboCommand.Moment(CastPlayer, id).TakenBy(HoldMoments.Offered) && grid != null && grid.World != null && Send(ComboCommand.Moment(CastPlayer, id));

    /// <summary>
    /// Attunes a hand to an element (null: nothing, so the other hand's element is on both), from the spell list. It takes a short
    /// moment; in a Hold run only at a tower, to its element (hold-batch). Null if it went to the core; else why it can't, which the player hears too.
    /// </summary>
    public string AttuneTo(int hand, string element)
    {
        if (grid == null || grid.World == null) return "No wizard here yet";
        var world = grid.World;
        string player = CastPlayer, why = null;
        if (!world.HandOpen(player, hand)) why = Locked($"Your {Side(hand)} hand", world, player, u => u.Hand == Side(hand));
        else if (!world.ElementOpen(player, element)) why = Locked(element, world, player, u => u.Element == element);
        else if (world.TowerOf(player) is { } tower && tower.Element != element) why = $"Only {tower.Element} here";
        else if (world.HoldRunning && world.TowerOf(player) == null) why = "Attune at a tower";
        else if (world.HandsOf(player).AttuneLeft > 0) why = "One hand at a time";
        else if (world.CastOf(player)?.Casting == true) why = "Not mid-cast";
        else if (world.HandsOf(player).Holds[hand] == element) return null;
        else if (!Send(ComboCommand.Attune(player, hand, element))) why = "The hands can't attune now";
        if (why != null)
        {
            EventLog.Add(why);
            return why;
        }
        return null; // the hand shows it: no line for it
    }

    // At a tower, a hand's trigger on a hand that holds something else attunes it to the tower's element, which takes its moment
    // (AttuneTo says why not, a locked one too); on a hand that already holds it, the trigger casts as anywhere. True if it attuned.
    bool AttuneAtTower(string player, HoldTower tower, int hand, bool pressed)
    {
        if (!pressed || tower == null || grid.World.HandsOf(player).Holds[hand] == tower.Element) return false;
        // A locked hand buzzes and says so, as any press of it does.
        if (Open(player, hand, HandsDef.Thrust)) AttuneTo(hand, tower.Element);
        return true;
    }

    // A hand's fired spell or act, for this wizard.
    void FromHand(ComboFired fired)
    {
        if (fired.Spell == null) hands?.HandAct(fired.Act);
        else CastAtCrosshair(fired.Spell, fired.Charge, fired.Range);
    }
}
