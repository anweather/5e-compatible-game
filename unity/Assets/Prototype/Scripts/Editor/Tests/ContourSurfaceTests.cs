using NUnit.Framework;

// A water tile's points blend the tops and flows of the cells round them from arrays read once a
// draw (ContourSurface.BlendKept, #70) where they used to ask a delegate per cell (BlendAround):
// the same values, bit for bit. The probe's showerstorm checks whole tiles both ways in play.
public class ContourSurfaceTests
{
    [Test]
    public void BlendingKeptCellsGivesWhatAskingEachCellDoes()
    {
        const int x0 = 37, y0 = 52, width = 20, height = 20;
        var rng = new System.Random(5);
        float[] kept = new float[width * height], flowX = new float[width * height], flowY = new float[width * height];
        for (int n = 0; n < kept.Length; n++)
        {
            kept[n] = rng.Next(4) == 0 ? float.NaN : (float)(rng.NextDouble() * 40.0 - 10.0);
            flowX[n] = (float)(rng.NextDouble() * 3.0 - 1.5);
            flowY[n] = (float)(rng.NextDouble() * 3.0 - 1.5);
        }
        System.Func<int, int, float?> Of(float[] values) => (x, y) =>
        {
            float v = values[(y - y0) * width + x - x0];
            return float.IsNaN(v) ? null : v;
        };
        int compared = 0;
        for (int k = 0; k < 20000; k++)
        {
            // Lattice points (quarters), edge points between them and the box's corners.
            float gx = x0 + 0.5f + (float)(rng.NextDouble() * (width - 1)), gy = y0 + 0.5f + (float)(rng.NextDouble() * (height - 1));
            if (k % 3 == 0)
            {
                gx = Quarter(gx);
                gy = Quarter(gy);
            }
            if (!ContourSurface.InKept(x0, y0, width, height, gx, gy)) continue;
            float asked = ContourSurface.BlendAround(gx, gy, Of(kept), -1f), read = ContourSurface.BlendKept(kept, x0, y0, width, gx, gy, -1f);
            Same(asked, read, $"top at ({gx}, {gy})");
            float withFlow = ContourSurface.BlendKept(kept, (x0, y0, width), (gx, gy), -1f, (flowX, flowY), out var flow);
            Same(asked, withFlow, $"top with the flow at ({gx}, {gy})");
            Same(ContourSurface.BlendAround(gx, gy, Of(flowX), 0f), flow.x, $"flow x at ({gx}, {gy})");
            Same(ContourSurface.BlendAround(gx, gy, Of(flowY), 0f), flow.y, $"flow y at ({gx}, {gy})");
            compared++;
        }
        Assert.Greater(compared, 15000);
        Assert.IsTrue(ContourSurface.InKept(x0, y0, width, height, x0 + 0.5f, y0 + 0.5f), "the box's first cell centre");
        Assert.IsFalse(ContourSurface.InKept(x0, y0, width, height, x0 + width - 0.5f, y0 + 0.5f), "the last cell centre reads one past");
    }

    static void Same(float expected, float actual, string what) =>
        Assert.AreEqual(System.BitConverter.SingleToInt32Bits(expected), System.BitConverter.SingleToInt32Bits(actual), $"{what}: {expected} against {actual}");

    static float Quarter(float g) => (float)System.Math.Round(g * 4f) / 4f;
}
