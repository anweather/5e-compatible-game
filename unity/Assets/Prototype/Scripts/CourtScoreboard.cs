using UnityEngine;

// The ball court's scoreboard (ValleyBuilder.BuildCourt): a board on posts by
// the court's north rail with two two-digit numbers in lit seven-segment
// figures, the goals at the west end on the left and at the east end on the
// right, as you see it from the court. The builder lays the segments out; this
// only switches them on and off (BallCourt says what to show).
public class CourtScoreboard : MonoBehaviour
{
    /// <summary>Segments in a figure (a to g: top, upper right, lower right, bottom, lower left, upper left, middle) and figures on the board.</summary>
    public const int SegmentsPerDigit = 7, DigitCount = 4;

    [Tooltip("Seven segments per figure, a to g, for the four figures: west tens, west ones, east tens, east ones.")]
    public Renderer[] segments;

    // Which segments light for each digit, a in bit 0 to g in bit 6.
    static readonly int[] Digits = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

    /// <summary>The numbers on the board now (-1 before the first Show).</summary>
    public int West { get; private set; } = -1;
    public int East { get; private set; } = -1;

    /// <summary>Shows the goals at each end (up to 99).</summary>
    public void Show(int west, int east)
    {
        if (west == West && east == East) return;
        West = west;
        East = east;
        ShowNumber(0, west);
        ShowNumber(2, east);
    }

    // Two figures from the first; no leading zero.
    void ShowNumber(int first, int value)
    {
        value = Mathf.Clamp(value, 0, 99);
        ShowDigit(first, value >= 10 ? Digits[value / 10] : 0);
        ShowDigit(first + 1, Digits[value % 10]);
    }

    void ShowDigit(int digit, int lit)
    {
        if (segments == null) return;
        for (int s = 0; s < SegmentsPerDigit; s++)
        {
            int i = digit * SegmentsPerDigit + s;
            if (i < segments.Length && segments[i] != null) segments[i].enabled = (lit & (1 << s)) != 0;
        }
    }
}
