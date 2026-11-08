using UnityEngine;

// The merged pairs' own glyphs (D16, docs/design/unified-casting.md, "The merge"): when both hands' charges merge, the
// two icons slam into one of these. One for every spell both hands bring out together in rules/hold.json's hands table
// (Lightning's bolt, Steam's cloud), drawn in code from HudDraw's shapes like the elements' icons, so they stay sharp at
// any size and need no art. Each fits inside its disc: points are in parts of the disc's radius, x right and y down.
public static class PairGlyphs
{
    /// <summary>Draws the glyph of <paramref name="spell"/> on a disc of radius <paramref name="r"/>; false if it has none.</summary>
    public static bool Draw(string spell, Vector2 at, float r, Color mark)
    {
        var g = new Pen(at, r, mark);
        switch (spell)
        {
            case SpellIds.Fireball: Fireball(g); break;
            case SpellIds.Meteor: Meteor(g); break;
            case SpellIds.ScaldingSteam: Steam(g); break;
            case SpellIds.Geyser: Geyser(g); break;
            case SpellIds.Forge: Forge(g); break;
            case SpellIds.LavaPool: LavaPool(g); break;
            case SpellIds.FallingStar: FallingStar(g); break;
            case SpellIds.LightningBolt: Lightning(g); break;
            case SpellIds.Wave: Wave(g); break;
            case SpellIds.Flood: Flood(g); break;
            case SpellIds.WallOfStone: Wall(g); break;
            case SpellIds.StoneRamp: Ramp(g); break;
            case SpellIds.Mud: Mud(g); break;
            case SpellIds.PlantGrowth: Sprout(g); break;
            case SpellIds.Frost: Snowflake(g); break;
            case SpellIds.RainCloud: RainCloud(g); break;
            case SpellIds.RollingBoulder: Boulder(g); break;
            case SpellIds.Whirlwind: Funnel(g); break;
            default: return false;
        }
        return true;
    }

    // Draws in parts of the disc's radius about its middle.
    readonly struct Pen
    {
        readonly Vector2 at;
        readonly float r;
        public readonly Color Mark;

        public Pen(Vector2 at, float r, Color mark) => (this.at, this.r, Mark) = (at, r, mark);

        Vector2 P(float x, float y) => at + new Vector2(x, y) * r;

        public void Disc(float x, float y, float radius) => HudDraw.Disc(P(x, y), radius * r, Mark);
        public void Ring(float x, float y, float inner, float outer) => HudDraw.Ring(P(x, y), inner * r, outer * r, 1f, Mark);
        public void Arc(float x, float y, float inner, float outer, float from, float to) => HudDraw.Arc(P(x, y), inner * r, outer * r, from, to, Mark);
        public void Tri(float ax, float ay, float bx, float by, float cx, float cy) => HudDraw.Triangle(P(ax, ay), P(bx, by), P(cx, cy), Mark);
        // Corners in parts of the radius, in order round the shape.
        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d) => HudDraw.Quad(P(a.x, a.y), P(b.x, b.y), P(c.x, c.y), P(d.x, d.y), Mark);
        public void Bar(float ax, float ay, float bx, float by, float width) => HudDraw.Bar(P(ax, ay), P(bx, by), width * r, Mark);
        public void Box(float left, float top, float right, float bottom) => Quad(new(left, top), new(right, top), new(right, bottom), new(left, bottom));

        // The upper half of a ring, as a wave's crest.
        public void Crest(float x, float y, float inner, float outer)
        {
            Arc(x, y, inner, outer, 0.75f, 1f);
            Arc(x, y, inner, outer, 0f, 0.25f);
        }

        // A puffy cloud along y, from x0 to x1.
        public void Cloud(float y, float x0, float x1)
        {
            float mid = (x0 + x1) / 2f, half = (x1 - x0) / 2f;
            Disc(x0 + half * 0.35f, y, half * 0.38f);
            Disc(mid, y - half * 0.22f, half * 0.5f);
            Disc(x1 - half * 0.35f, y, half * 0.38f);
            Box(x0 + half * 0.3f, y, x1 - half * 0.3f, y + half * 0.36f);
        }

        // A five-pointed star.
        public void Star(float x, float y, float outer)
        {
            float inner = outer * 0.42f;
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f, half = Mathf.PI / 5f;
                Tri(x + Mathf.Sin(a) * outer, y - Mathf.Cos(a) * outer,
                    x + Mathf.Sin(a - half) * inner, y - Mathf.Cos(a - half) * inner,
                    x + Mathf.Sin(a + half) * inner, y - Mathf.Cos(a + half) * inner);
            }
            Disc(x, y, inner * 1.02f);
        }
    }

    // A sun of fire: a ball with eight short rays.
    static void Fireball(Pen g)
    {
        g.Disc(0f, 0f, 0.4f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f, half = Mathf.PI / 14f;
            g.Tri(Mathf.Sin(a - half) * 0.46f, -Mathf.Cos(a - half) * 0.46f, Mathf.Sin(a + half) * 0.46f, -Mathf.Cos(a + half) * 0.46f, Mathf.Sin(a) * 0.72f, -Mathf.Cos(a) * 0.72f);
        }
    }

    // A rock falling from the top left, its tail behind it.
    static void Meteor(Pen g)
    {
        g.Tri(0.06f, 0.42f, 0.42f, 0.06f, -0.62f, -0.62f);
        g.Bar(-0.2f, 0.32f, -0.52f, 0f, 0.07f);
        g.Bar(0.32f, -0.2f, 0f, -0.52f, 0.07f);
        g.Disc(0.24f, 0.24f, 0.3f);
    }

    // A cloud with steam rising off it.
    static void Steam(Pen g)
    {
        g.Cloud(0.22f, -0.6f, 0.6f);
        for (int i = -1; i <= 1; i++)
        {
            float x = i * 0.28f;
            g.Bar(x, -0.12f, x + 0.09f, -0.34f, 0.09f);
            g.Bar(x + 0.09f, -0.34f, x, -0.58f, 0.09f);
        }
    }

    // A jet from the ground, widening, and its spray.
    static void Geyser(Pen g)
    {
        g.Quad(new(-0.1f, 0.48f), new(0.1f, 0.48f), new(0.22f, -0.28f), new(-0.22f, -0.28f));
        g.Disc(-0.27f, -0.4f, 0.14f);
        g.Disc(0f, -0.5f, 0.17f);
        g.Disc(0.27f, -0.4f, 0.14f);
        g.Box(-0.5f, 0.46f, 0.5f, 0.58f);
    }

    // An anvil with sparks off it.
    static void Forge(Pen g)
    {
        g.Box(-0.38f, -0.1f, 0.48f, 0.08f);
        g.Tri(-0.38f, -0.1f, -0.38f, 0.08f, -0.68f, -0.1f);
        g.Quad(new(-0.16f, 0.08f), new(0.26f, 0.08f), new(0.16f, 0.3f), new(-0.06f, 0.3f));
        g.Box(-0.3f, 0.3f, 0.4f, 0.44f);
        g.Bar(0.1f, -0.2f, 0.28f, -0.5f, 0.07f);
        g.Bar(0f, -0.2f, -0.06f, -0.56f, 0.07f);
        g.Bar(0.2f, -0.18f, 0.5f, -0.36f, 0.07f);
    }

    // A pool, low and wide, with bubbles off it.
    static void LavaPool(Pen g)
    {
        g.Box(-0.56f, 0.2f, 0.56f, 0.42f);
        g.Disc(-0.56f, 0.31f, 0.11f);
        g.Disc(0.56f, 0.31f, 0.11f);
        g.Crest(-0.28f, 0.2f, 0f, 0.14f);
        g.Crest(0.16f, 0.2f, 0f, 0.18f);
        g.Ring(-0.12f, -0.18f, 0.06f, 0.11f);
        g.Ring(0.24f, -0.42f, 0.04f, 0.08f);
    }

    // A star coming down from the top left.
    static void FallingStar(Pen g)
    {
        g.Bar(-0.1f, -0.1f, -0.6f, -0.6f, 0.08f);
        g.Bar(-0.18f, 0.16f, -0.56f, -0.22f, 0.06f);
        g.Bar(0.16f, -0.18f, -0.22f, -0.56f, 0.06f);
        g.Star(0.16f, 0.14f, 0.44f);
    }

    // A bolt in two strokes.
    static void Lightning(Pen g)
    {
        g.Tri(0.22f, -0.72f, -0.32f, 0.08f, 0.14f, 0.02f);
        g.Tri(-0.14f, -0.02f, 0.32f, -0.08f, -0.22f, 0.72f);
    }

    // A wave curling over.
    static void Wave(Pen g)
    {
        g.Arc(0.05f, 0.08f, 0.32f, 0.5f, 0.68f, 1f);
        g.Arc(0.05f, 0.08f, 0.32f, 0.5f, 0f, 0.25f);
        g.Disc(0.47f, 0.06f, 0.1f);
        g.Box(-0.62f, 0.4f, 0.62f, 0.52f);
        g.Tri(-0.45f, 0.4f, -0.2f, 0.4f, -0.45f, -0.05f);
    }

    // Three rows of waves.
    static void Flood(Pen g)
    {
        for (int row = -1; row <= 1; row++)
            for (int i = -1; i <= 1; i++) g.Crest(i * 0.38f, 0.1f + row * 0.3f, 0.12f, 0.2f);
    }

    // Bricks.
    static void Wall(Pen g)
    {
        const float Gap = 0.05f, Height = 0.24f, Left = -0.6f, Right = 0.6f;
        for (int row = 0; row < 4; row++)
        {
            float top = -0.5f + row * (Height + Gap), bottom = top + Height;
            float start = row % 2 == 0 ? Left : Left - 0.2f;
            for (float x = start; x < Right; x += 0.4f)
                g.Box(Mathf.Max(Left, x), top, Mathf.Min(Right, x + 0.4f - Gap), bottom);
        }
    }

    // A ramp of stone going up to the right, in steps.
    static void Ramp(Pen g)
    {
        g.Tri(-0.62f, 0.44f, 0.62f, 0.44f, 0.62f, -0.4f);
        g.Box(-0.62f, 0.44f, 0.62f, 0.54f);
        g.Bar(-0.2f, 0.2f, 0.2f, 0.2f, 0.05f);
        g.Bar(0.2f, -0.06f, 0.55f, -0.06f, 0.05f);
    }

    // Blobs and a splash.
    static void Mud(Pen g)
    {
        g.Disc(-0.28f, 0.22f, 0.26f);
        g.Disc(0.12f, 0.28f, 0.22f);
        g.Disc(0.38f, 0.14f, 0.16f);
        g.Box(-0.56f, 0.3f, 0.56f, 0.46f);
        g.Disc(-0.08f, -0.3f, 0.12f);
        g.Disc(0.28f, -0.42f, 0.08f);
        g.Disc(-0.36f, -0.24f, 0.07f);
    }

    // A sprout: a stem, two leaves and the ground.
    static void Sprout(Pen g)
    {
        g.Bar(0f, 0.46f, 0f, -0.3f, 0.09f);
        g.Quad(new(0f, 0.05f), new(-0.24f, -0.12f), new(-0.56f, -0.12f), new(-0.3f, 0.08f));
        g.Quad(new(0f, -0.15f), new(0.24f, -0.34f), new(0.56f, -0.32f), new(0.3f, -0.12f));
        g.Disc(0f, -0.36f, 0.1f);
        g.Box(-0.5f, 0.44f, 0.5f, 0.56f);
    }

    // A snowflake: three bars crossed, a V at each end.
    static void Snowflake(Pen g)
    {
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            float x = Mathf.Sin(a), y = -Mathf.Cos(a);
            g.Bar(0f, 0f, x * 0.66f, y * 0.66f, 0.09f);
            float bx = x * 0.42f, by = y * 0.42f;
            float l = a - 0.7f, rt = a + 0.7f;
            g.Bar(bx, by, bx + Mathf.Sin(l) * 0.2f, by - Mathf.Cos(l) * 0.2f, 0.07f);
            g.Bar(bx, by, bx + Mathf.Sin(rt) * 0.2f, by - Mathf.Cos(rt) * 0.2f, 0.07f);
        }
        g.Disc(0f, 0f, 0.1f);
    }

    // A cloud with rain under it.
    static void RainCloud(Pen g)
    {
        g.Cloud(-0.16f, -0.6f, 0.6f);
        for (int i = -1; i <= 1; i++) g.Bar(i * 0.3f + 0.06f, 0.22f, i * 0.3f - 0.06f, 0.58f, 0.08f);
    }

    // A boulder rolling right, its speed lines behind it.
    static void Boulder(Pen g)
    {
        g.Disc(0.18f, 0.04f, 0.4f);
        g.Bar(-0.66f, -0.14f, -0.32f, -0.14f, 0.08f);
        g.Bar(-0.6f, 0.08f, -0.3f, 0.08f, 0.08f);
        g.Bar(-0.66f, 0.3f, -0.36f, 0.3f, 0.08f);
        g.Box(-0.6f, 0.46f, 0.66f, 0.56f);
    }

    // A funnel narrowing down to the ground.
    static void Funnel(Pen g)
    {
        float[] widths = { 0.62f, 0.48f, 0.34f, 0.2f };
        for (int i = 0; i < widths.Length; i++)
        {
            float y = -0.46f + i * 0.26f, shift = (i % 2 == 0 ? -1f : 1f) * 0.05f;
            g.Bar(-widths[i] + shift, y, widths[i] + shift, y, 0.12f);
        }
        g.Disc(0.02f, 0.5f, 0.07f);
    }
}
