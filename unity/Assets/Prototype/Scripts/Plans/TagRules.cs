using System.Collections.Generic;
using UnityEngine;

// The rules of tag, apart from the network (NetTag runs them on the host): one
// player is "it"; when "it" comes within reach of another player, that player
// is it, and can't tag straight back for a few seconds. Players are client
// ids; positions are where their wizards stand.
public sealed class TagRules
{
    /// <summary>No one: the game isn't on.</summary>
    public const ulong Nobody = ulong.MaxValue;
    /// <summary>How close "it" has to get (metres, wizard to wizard).</summary>
    public const float Reach = 1.4f;
    /// <summary>How long the player who was just tagged can't tag back the one who tagged them.</summary>
    public const float NoTagBacks = 3f;

    ulong taggedBy = Nobody;
    float noTagBackUntil;

    /// <summary>Who is it, or Nobody.</summary>
    public ulong It { get; private set; } = Nobody;

    public bool Playing => It != Nobody;

    /// <summary>Starts a game with one of these players it (<paramref name="pick"/>, any number: it wraps); false with fewer than two.</summary>
    public bool Start(IReadOnlyList<ulong> players, int pick)
    {
        if (players == null || players.Count < 2) return false;
        It = players[((pick % players.Count) + players.Count) % players.Count];
        taggedBy = Nobody;
        return true;
    }

    public void Stop()
    {
        It = Nobody;
        taggedBy = Nobody;
    }

    /// <summary>
    /// One look at where everyone stands. Returns the player "it" just tagged (who is now it), or
    /// Nobody. If "it" has left the game, the first player left is it.
    /// </summary>
    public ulong Update(float now, IReadOnlyList<(ulong player, Vector3 at)> players)
    {
        if (!Playing || players == null) return Nobody;
        if (players.Count < 2)
        {
            Stop();
            return Nobody;
        }
        int me = -1;
        for (int i = 0; i < players.Count; i++) if (players[i].player == It) me = i;
        if (me < 0)
        {
            It = players[0].player;
            taggedBy = Nobody;
            return Nobody;
        }
        var at = players[me].at;
        for (int i = 0; i < players.Count; i++)
        {
            var (other, there) = players[i];
            if (other == It || (other == taggedBy && now < noTagBackUntil)) continue;
            var gap = there - at;
            gap.y = 0f;
            if (gap.magnitude > Reach) continue;
            taggedBy = It;
            noTagBackUntil = now + NoTagBacks;
            It = other;
            return other;
        }
        return Nobody;
    }
}
