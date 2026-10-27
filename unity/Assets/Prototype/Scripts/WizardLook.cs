using UnityEngine;

// A wizard's look (docs/WORLD_LIFE.md, "Wizards"): the hat, the robe, the
// beard and the staff, every size and colour drawn from one seed, so a seed
// is all a peer needs to send for everyone to see the same wizard. The parts
// themselves are built on the rig's bones by WizardOutfit. The draw is our own
// small hash, not System.Random, so the same seed gives the same wizard on
// every platform.
public sealed class WizardLook
{
    public enum StaffKind { None, Gnarled, Crystal, Orb }

    // Robe and hat colours, in pairs that sit well together: the cloth, and its trim.
    static readonly (Color cloth, Color trim)[] Palettes =
    {
        (new Color(0.13f, 0.18f, 0.45f), new Color(0.85f, 0.72f, 0.32f)),   // midnight blue and gold
        (new Color(0.45f, 0.10f, 0.12f), new Color(0.88f, 0.76f, 0.45f)),   // crimson and old gold
        (new Color(0.12f, 0.32f, 0.20f), new Color(0.75f, 0.78f, 0.80f)),   // forest green and silver
        (new Color(0.32f, 0.15f, 0.42f), new Color(0.80f, 0.70f, 0.95f)),   // violet and lilac
        (new Color(0.12f, 0.12f, 0.14f), new Color(0.62f, 0.15f, 0.18f)),   // black and blood red
        (new Color(0.55f, 0.55f, 0.58f), new Color(0.25f, 0.30f, 0.55f)),   // grey and slate blue
        (new Color(0.62f, 0.42f, 0.16f), new Color(0.32f, 0.20f, 0.10f)),   // ochre and brown
        (new Color(0.10f, 0.40f, 0.45f), new Color(0.90f, 0.85f, 0.70f)),   // teal and cream
    };

    static readonly Color[] BeardColours =
    {
        new(0.92f, 0.92f, 0.90f), new(0.62f, 0.62f, 0.62f), new(0.30f, 0.20f, 0.12f),
        new(0.10f, 0.09f, 0.08f), new(0.66f, 0.30f, 0.12f),
    };

    public int Seed { get; private set; }
    public Color Robe, Trim, Hat, Band, Beard;
    /// <summary>Hem below the hips (m), the skirt's flare at the hem (radius, m).</summary>
    public float RobeLength, RobeFlare;
    /// <summary>The hat's height and brim radius (m), how far its tip droops (0 upright, 1 flopped), and its sideways bend.</summary>
    public float HatHeight, Brim, Droop, Bend;
    /// <summary>The beard's length (m); 0 for none.</summary>
    public float BeardLength;
    public StaffKind Staff;
    public Color StaffGlow;

    public static WizardLook From(int seed)
    {
        uint s = (uint)seed * 2654435761u + 0x9E3779B9u;
        float Next()
        {
            s = s * 1664525u + 1013904223u;
            return s / 4294967296f;
        }
        int Pick(int n) => Mathf.Min(n - 1, (int)(Next() * n));

        var look = new WizardLook { Seed = seed };
        var (cloth, trim) = Palettes[Pick(Palettes.Length)];
        look.Robe = cloth;
        look.Trim = trim;
        // Most hats match the robe; some go their own way.
        look.Hat = Next() < 0.7f ? cloth * 0.92f : Palettes[Pick(Palettes.Length)].cloth;
        look.Band = trim;
        look.RobeLength = Mathf.Lerp(0.55f, 0.85f, Next());
        look.RobeFlare = Mathf.Lerp(0.27f, 0.40f, Next());
        look.HatHeight = Mathf.Lerp(0.32f, 0.58f, Next());
        look.Brim = Mathf.Lerp(0.22f, 0.34f, Next());
        look.Droop = Next() < 0.45f ? Mathf.Lerp(0.3f, 1f, Next()) : Mathf.Lerp(0f, 0.15f, Next());
        look.Bend = Mathf.Lerp(-0.5f, 0.5f, Next());
        look.BeardLength = Next() < 0.25f ? 0f : Mathf.Lerp(0.10f, 0.36f, Next());
        look.Beard = BeardColours[Pick(BeardColours.Length)];
        look.Staff = (StaffKind)Pick(4);
        look.StaffGlow = Color.Lerp(trim, Color.white, 0.3f);
        // VillageLit reads a vertex colour's alpha as glow: cloth and hair don't glow.
        look.Robe = Matte(look.Robe);
        look.Trim = Matte(look.Trim);
        look.Hat = Matte(look.Hat);
        look.Band = Matte(look.Band);
        look.Beard = Matte(look.Beard);
        return look;
    }

    static Color Matte(Color c) => new(c.r, c.g, c.b, 0f);

    /// <summary>A new seed for a fresh look (from the clock: not shared state, only a starting point).</summary>
    public static int NewSeed() => (int)(System.DateTime.UtcNow.Ticks & 0x7fffffff) ^ Random.Range(1, int.MaxValue);
}
