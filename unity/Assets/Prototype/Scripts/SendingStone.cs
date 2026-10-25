using UnityEngine;

// One of a pair of sending stones (items.json "send"). Using it casts Sending
// to whoever bears the other stone: they hear the message, and a ping shows
// them where it came from. Nobody carrying the other stone? Then it hums
// where it lies, and the ping shows the sender where that is.
//
// Solo, both ends are on this machine. Online, Send is where a message would
// go to the host and on to the machine whose player holds the partner (as
// NetChem.RequestCast does for casts); until then only the local end runs.
public class SendingStone : ItemUse
{
    /// <summary>How long a ping marker stays up, in seconds.</summary>
    public const float PingSeconds = 8f;

    static readonly Color PingColor = new Color(0.55f, 0.8f, 1f);

    public override void Use(ItemHolder holder) => Send(holder);

    /// <summary>Speaks the item's message into this stone; returns the marker the far end shows, or null.</summary>
    public PingMarker Send(ItemHolder from)
    {
        var other = Item.partner;
        string message = Item.Def.Message;
        if (other == null)
        {
            EventLog.Add($"The {Item.Name.ToLowerInvariant()} has no partner to send to");
            return null;
        }
        if (other.Holder != null)
        {
            // Heard by the other stone's bearer, with a ping where the sender stands.
            EventLog.Add($"Sending: \"{message}\" (heard by {other.Holder.name}, {Distance(from, other):0} m away)");
            return PingMarker.Show(from.transform.position, $"\"{message}\"", PingColor, PingSeconds);
        }
        EventLog.Add($"Sending: \"{message}\". The other stone hums {Distance(from, other):0} m away {Compass(other.transform.position - from.transform.position)}");
        return PingMarker.Show(other.transform.position, $"\"{message}\"", PingColor, PingSeconds);
    }

    static float Distance(ItemHolder from, Carryable to) => Vector3.Distance(from.transform.position, to.transform.position);

    // North is +z, as in the chemistry grid.
    static string Compass(Vector3 offset)
    {
        string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        float bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        return names[Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8];
    }
}
