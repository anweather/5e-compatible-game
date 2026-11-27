using System.Collections.Generic;
using NUnit.Framework;

// The tree budget (TreeBudget): in a dense wood (the Wide World's temperate band, ~5,000 trees within 150 m on its walk,
// 54 ms a frame) the prefabs and impostors draw to a shorter distance; in the nine's sparser woods (~530 within 150 m)
// they draw as far as ever. These fail if the dense distances are lost.
[Category("Unit")]
public class TreeBudgetTests
{
    static List<float> Disc(int trees, float radius)
    {
        var d2 = new List<float>(trees);
        for (int k = 0; k < trees; k++) d2.Add(radius * radius * (k + 0.5f) / trees);
        return d2;
    }

    [Test]
    public void ADenseWoodStandsItsPrefabsNearer()
    {
        float near = TreeBudget.Near(Disc(5000, 158f), 150f);
        Assert.That(near, Is.LessThan(100f), "5,000 trees within 150 m should stand fewer than all as prefabs");
        Assert.That(near, Is.GreaterThanOrEqualTo(TreeBudget.NearFloor));
        Assert.That(near, Is.EqualTo(158f * System.Math.Sqrt((double)TreeBudget.MostNear / 5000)).Within(1f));
    }

    [Test]
    public void TheNinesWoodsStandAsFarAsEver()
    {
        Assert.That(TreeBudget.Near(Disc(531, 158f), 150f), Is.EqualTo(150f));
        Assert.That(TreeBudget.MostNear, Is.GreaterThan(531), "the nine stands ~530 prefabs at its stand");
    }

    [Test]
    public void NoDensityTakesThePrefabsUnderTheFloor() =>
        Assert.That(TreeBudget.Near(Disc(50000, 158f), 150f), Is.EqualTo(TreeBudget.NearFloor));

    [Test]
    public void ADenseForestDrawsItsImpostorsNearer()
    {
        var chunks = new List<(float d2, int trees)>();
        for (int i = -18; i <= 18; i++)
            for (int j = -18; j <= 18; j++)
            {
                float d = System.MathF.Sqrt(i * i + j * j) * 40f;
                if (d <= 700f) chunks.Add((d * d, 160));
            }
        float far = TreeBudget.Far(chunks, 700f);
        Assert.That(far, Is.LessThan(700f));
        Assert.That(far, Is.GreaterThanOrEqualTo(TreeBudget.FarFloor));
    }

    [Test]
    public void ASparseForestDrawsItsImpostorsToTheHaze()
    {
        var chunks = new List<(float d2, int trees)> { (0f, 500), (200f * 200f, 4000), (600f * 600f, 8000) };
        Assert.That(TreeBudget.Far(chunks, 700f), Is.EqualTo(700f));
    }
}
