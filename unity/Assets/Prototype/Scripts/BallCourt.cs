using UnityEngine;

// The ball court's game (ValleyBuilder.BuildCourt, docs/BACKLOG.md 15): a ball
// that rolls right over the line in a goal mouth is a goal at that end,
// counted on the scoreboard and in the log, and goes back to the centre spot a
// moment later. A ball that leaves the court (over a rail, out of the open
// gate) goes back there too, after a couple of seconds outside, unless
// someone is carrying it.
//
// Whoever runs the ball's physics decides: solo this machine, online the host
// (a client's ball is a puppet). The host's score goes to everyone through
// NetCourt, which every machine's scoreboard then shows.
public class BallCourt : MonoBehaviour
{
    [Tooltip("The centre spot, on the ground: where the ball goes back to.")]
    public Vector3 centre;
    [Tooltip("The court inside its rails (m): west to east, and south to north.")]
    public float length = 26f, width = 18f;
    [Tooltip("The goal mouths (m): how wide and how high, and how deep the net behind them.")]
    public float goalWidth = 4f, goalHeight = 1.8f, goalDepth = 1.2f;
    public CourtScoreboard board;

    // The ball's radius and a bit (m): a goal is the whole ball over the line.
    const float OverLine = 0.25f;
    // How high above the spot the ball is dropped back (m), and how long after a goal (s).
    const float DropHeight = 0.6f, GoalPause = 1.5f;
    // How far past the rails the ball may be, and for how long, before it's out (m, s).
    const float OutMargin = 0.6f, OutSeconds = 2f;
    // How far below the court it may fall (through the ground, into a dug pit) before it's out (m).
    const float FallMargin = 6f;
    const float FindEvery = 1f;

    int west, east;
    PhysicsProp ball;
    Rigidbody ballBody;
    Carryable ballCarry;
    float nextFind, returnAt = -1f, outSince = -1f;

    /// <summary>Goals at the west end and at the east end (online, the host's count).</summary>
    public int West => NetCourt.Live != null ? NetCourt.Live.West : west;
    public int East => NetCourt.Live != null ? NetCourt.Live.East : east;

    /// <summary>The court's ball, once it has spawned.</summary>
    public PhysicsProp Ball => ball;

    /// <summary>A goal, in everyone's log and with fireworks over that goal (solo from here, online from NetCourt).</summary>
    public static void Announce(bool eastEnd, int westGoals, int eastGoals)
    {
        EventLog.Add($"Goal at the {(eastEnd ? "east" : "west")} end! West {westGoals}, east {eastGoals}");
        var court = FindFirstObjectByType<BallCourt>();
        if (court != null) TowerFireworks.Launch(court.GoalMouth(eastEnd) + Vector3.up * FireworksHeight);
    }

    // How high over the goal mouth the fireworks go up (m).
    const float FireworksHeight = 3f;

    /// <summary>The middle of a goal mouth, on the ground.</summary>
    public Vector3 GoalMouth(bool eastEnd) => centre + Vector3.right * ((eastEnd ? 1f : -1f) * length / 2f);

    void Update()
    {
        if (board != null) board.Show(West, East);
        if (!FindBall() || ball.Puppet) return;
        if (returnAt >= 0f)
        {
            if (Time.time >= returnAt) Return();
            return;
        }
        Watch();
    }

    // The ball on the court (it spawns on the centre spot); found again if it goes.
    bool FindBall()
    {
        if (ball != null && ball.gameObject.activeInHierarchy) return true;
        ball = null;
        if (Time.time < nextFind) return false;
        nextFind = Time.time + FindEvery;
        foreach (var prop in FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None))
        {
            if (prop.kind != ItemIds.Ball || !OnCourt(prop.transform.position)) continue;
            ball = prop;
            ballBody = prop.GetComponent<Rigidbody>();
            prop.TryGetComponent(out ballCarry);
            return true;
        }
        return false;
    }

    void Watch()
    {
        if (ballCarry != null && ballCarry.Holder != null)
        {
            outSince = -1f;
            return;
        }
        var at = ball.transform.position;
        int end = GoalEnd(at);
        if (end != 0)
        {
            Score(end > 0);
            returnAt = Time.time + GoalPause;
            return;
        }
        if (OnCourt(at)) outSince = -1f;
        else if (outSince < 0f) outSince = Time.time;
        else if (Time.time - outSince > OutSeconds) Return();
    }

    /// <summary>Which goal a point is in: -1 the west one, 1 the east one, 0 neither.</summary>
    public int GoalEnd(Vector3 at)
    {
        float dx = at.x - centre.x;
        bool inMouth = Mathf.Abs(at.z - centre.z) < goalWidth / 2f && at.y < centre.y + goalHeight + OverLine;
        float past = Mathf.Abs(dx) - length / 2f;
        if (!inMouth || past < OverLine || past > goalDepth + 2f * OverLine) return 0;
        return dx > 0f ? 1 : -1;
    }

    /// <summary>Whether a point is on the court (a little past its rails counts).</summary>
    public bool OnCourt(Vector3 at) =>
        Mathf.Abs(at.x - centre.x) < length / 2f + OutMargin && Mathf.Abs(at.z - centre.z) < width / 2f + OutMargin && at.y > centre.y - FallMargin;

    void Score(bool eastEnd)
    {
        if (NetCourt.Live != null)
        {
            NetCourt.Live.Scored(eastEnd);
            return;
        }
        if (eastEnd) east++;
        else west++;
        Announce(eastEnd, west, east);
    }

    // Back on the centre spot, still. Online the host's NetworkTransform jumps it there on every screen.
    void Return()
    {
        returnAt = -1f;
        outSince = -1f;
        if (ballBody == null || ballBody.isKinematic) return;
        var to = centre + Vector3.up * DropHeight;
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        if (ball.TryGetComponent<NetProp>(out var net) && net.Hosting) net.Teleport(to);
        else ball.transform.position = to;
        ballBody.position = to;
    }
}
