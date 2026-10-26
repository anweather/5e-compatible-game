using NUnit.Framework;
using UnityEngine;

// Tag's rules (TagRules), without the network: who starts it, tagging within
// reach, no instant tag-backs, and "it" leaving the game.
public class TagRulesTests
{
    static (ulong, Vector3)[] At(params (ulong player, float x)[] spots)
    {
        var list = new (ulong, Vector3)[spots.Length];
        for (int i = 0; i < spots.Length; i++) list[i] = (spots[i].player, new Vector3(spots[i].x, 0f, 0f));
        return list;
    }

    [Test]
    public void NeedsTwoPlayersAndPicksOne()
    {
        var tag = new TagRules();
        Assert.That(tag.Start(new ulong[] { 7 }, 0), Is.False);
        Assert.That(tag.Playing, Is.False);
        Assert.That(tag.Start(new ulong[] { 7, 9 }, 3), Is.True);
        Assert.That(tag.It, Is.EqualTo(9UL), "the pick wraps");
    }

    [Test]
    public void ItTagsWhoeverComesWithinReach()
    {
        var tag = new TagRules();
        tag.Start(new ulong[] { 1, 2, 3 }, 0);
        Assert.That(tag.Update(0f, At((1, 0f), (2, 5f), (3, 9f))), Is.EqualTo(TagRules.Nobody), "no one near");
        Assert.That(tag.Update(1f, At((1, 0f), (2, 1.2f), (3, 9f))), Is.EqualTo(2UL));
        Assert.That(tag.It, Is.EqualTo(2UL));
    }

    [Test]
    public void NoTagBacksForAFewSeconds()
    {
        var tag = new TagRules();
        tag.Start(new ulong[] { 1, 2 }, 0);
        tag.Update(0f, At((1, 0f), (2, 1f)));
        Assert.That(tag.It, Is.EqualTo(2UL));
        Assert.That(tag.Update(1f, At((1, 0f), (2, 1f))), Is.EqualTo(TagRules.Nobody), "player 1 just tagged them");
        Assert.That(tag.Update(TagRules.NoTagBacks + 0.1f, At((1, 0f), (2, 1f))), Is.EqualTo(1UL), "after the wait it's fair game");
    }

    [Test]
    public void WhenItLeavesSomeoneElseIsItAndAloneTheGameStops()
    {
        var tag = new TagRules();
        tag.Start(new ulong[] { 1, 2, 3 }, 0);
        tag.Update(0f, At((2, 0f), (3, 9f)));
        Assert.That(tag.It, Is.EqualTo(2UL));
        tag.Update(1f, At((3, 0f)));
        Assert.That(tag.Playing, Is.False);
    }
}
