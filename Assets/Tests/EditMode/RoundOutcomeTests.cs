using NUnit.Framework;

public class RoundOutcomeTests
{
    [Test] public void RoundContinuesWhileSurvivorsRemain() =>
        Assert.AreEqual(RoundOutcome.None, RoundRules.Evaluate(alive: 2, escaped: 1, total: 4, timeUp: false));

    [Test] public void EveryoneCaughtIsAHunterWin() =>
        Assert.AreEqual(RoundOutcome.HunterCaughtAll, RoundRules.Evaluate(0, 0, 3, false));

    [Test] public void AnyEscapeWithNobodyLeftIsASurvivorWin() =>
        Assert.AreEqual(RoundOutcome.SurvivorsEscaped, RoundRules.Evaluate(0, 1, 3, false));

    [Test] public void TimerExpiryWithSurvivorsLeftIsAHunterWin() =>
        Assert.AreEqual(RoundOutcome.HunterTimeout, RoundRules.Evaluate(2, 0, 3, true));

    // The old bug in miniature: one escapee must not end the round for the players still in it.
    [Test] public void OneEscapeeDoesNotEndTheRoundForEveryoneElse() =>
        Assert.AreEqual(RoundOutcome.None, RoundRules.Evaluate(alive: 3, escaped: 1, total: 4, timeUp: false));

    // Solo host who is the hunter: zero survivors must not read as "all caught" on frame one.
    [Test] public void SoloHunterRoundOnlyEndsOnTimer()
    {
        Assert.AreEqual(RoundOutcome.None, RoundRules.Evaluate(0, 0, 0, false));
        Assert.AreEqual(RoundOutcome.HunterTimeout, RoundRules.Evaluate(0, 0, 0, true));
    }
}
