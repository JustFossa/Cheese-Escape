using System.Collections.Generic;
using NUnit.Framework;

public class MatchRulesTests
{
    [Test] public void RageStartsAtZeroAndEndsAtOne()
    {
        Assert.AreEqual(0f, RoundRules.Rage(0, 6, 0f, 300f), 1e-4f);
        Assert.AreEqual(1f, RoundRules.Rage(6, 6, 300f, 300f), 1e-4f);
    }

    [Test] public void RageIsHalfCheeseHalfTime()
    {
        Assert.AreEqual(0.5f, RoundRules.Rage(6, 6, 0f, 300f), 1e-4f);
        Assert.AreEqual(0.5f, RoundRules.Rage(0, 6, 300f, 300f), 1e-4f);
    }

    [Test] public void RageIgnoresAnUnknownDoorAndOverrun()
    {
        Assert.AreEqual(0f, RoundRules.Rage(3, 0, 0f, 300f), 1e-4f);
        Assert.AreEqual(1f, RoundRules.Rage(9, 6, 999f, 300f), 1e-4f);
    }

    [Test] public void RageScalesSpeedAndCooldown()
    {
        Assert.AreEqual(1f, RoundRules.RageSpeedScale(0f), 1e-4f);
        Assert.AreEqual(1.10f, RoundRules.RageSpeedScale(1f), 1e-4f);
        Assert.AreEqual(25f, RoundRules.SenseCooldown(0f), 1e-4f);
        Assert.AreEqual(15f, RoundRules.SenseCooldown(1f), 1e-4f);
    }

    [Test] public void SmallLobbiesGetOneHunterAndBigOnesTwo()
    {
        Assert.AreEqual(1, RoundRules.HunterCount(1));
        Assert.AreEqual(1, RoundRules.HunterCount(5));
        Assert.AreEqual(2, RoundRules.HunterCount(6));
        Assert.AreEqual(2, RoundRules.HunterCount(8));
    }

    [Test] public void TheLeastHuntedPlayerHuntsNext()
    {
        var hunts = new Dictionary<ulong, int> { { 0, 2 }, { 1, 1 }, { 2, 0 } };
        List<ulong> picked = RoundRules.PickHunters(new ulong[] { 0, 1, 2 }, c => hunts[c], 1, n => 0);
        CollectionAssert.AreEqual(new ulong[] { 2 }, picked);
    }

    [Test] public void TiesAreBrokenByTheRandomSource()
    {
        var hunts = new Dictionary<ulong, int> { { 0, 0 }, { 1, 0 }, { 2, 1 } };
        Assert.AreEqual(0UL, RoundRules.PickHunters(new ulong[] { 0, 1, 2 }, c => hunts[c], 1, n => 0)[0]);
        Assert.AreEqual(1UL, RoundRules.PickHunters(new ulong[] { 0, 1, 2 }, c => hunts[c], 1, n => n - 1)[0]);
    }

    [Test] public void TwoHuntersAreDistinctAndNeverMoreThanTheLobby()
    {
        var hunts = new Dictionary<ulong, int> { { 0, 0 }, { 1, 0 }, { 2, 0 } };
        List<ulong> two = RoundRules.PickHunters(new ulong[] { 0, 1, 2 }, c => hunts[c], 2, n => 0);
        Assert.AreEqual(2, two.Count);
        Assert.AreNotEqual(two[0], two[1]);

        Assert.AreEqual(1, RoundRules.PickHunters(new ulong[] { 7 }, c => 0, 2, n => 0).Count);
    }

    [Test] public void PointsRewardEscapingAndCatching()
    {
        Assert.AreEqual(3, RoundRules.SurvivorPoints(LifeState.Escaped));
        Assert.AreEqual(1, RoundRules.SurvivorPoints(LifeState.Alive));
        Assert.AreEqual(0, RoundRules.SurvivorPoints(LifeState.Downed));
        Assert.AreEqual(0, RoundRules.SurvivorPoints(LifeState.Caught));
        Assert.AreEqual(6, RoundRules.HunterPoints(3));
    }

    [Test] public void CarryingSlowsYouAndTheCapHolds()
    {
        Assert.AreEqual(1f, RoundRules.CarrySpeedScale(0), 1e-4f);
        Assert.AreEqual(0.93f, RoundRules.CarrySpeedScale(1), 1e-4f);
        Assert.AreEqual(0.8649f, RoundRules.CarrySpeedScale(2), 1e-4f);
        Assert.AreEqual(RoundRules.CarrySpeedScale(2), RoundRules.CarrySpeedScale(9), 1e-4f);
    }

    [Test] public void PerksChangeOnlyTheirOwnNumbers()
    {
        Assert.AreEqual(3f, RoundRules.ReviveSeconds(Perk.Medic), 1e-4f);
        Assert.AreEqual(5f, RoundRules.ReviveSeconds(Perk.Scout), 1e-4f);
        Assert.Greater(RoundRules.RegenScale(Perk.Sprinter), 1f);
        Assert.Less(RoundRules.DrainScale(Perk.Sprinter), 1f);
        Assert.AreEqual(1f, RoundRules.RegenScale(Perk.Medic), 1e-4f);
    }
}
