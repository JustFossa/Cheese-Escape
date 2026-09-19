using System;
using System.Collections.Generic;

public enum RoundState : byte { Waiting, Playing, Ending }

public enum RoundOutcome : byte { None, SurvivorsEscaped, HunterCaughtAll, HunterTimeout }

// Downed = caught but revivable; Caught/Escaped are out of the round (spectating).
public enum LifeState : byte { Alive, Downed, Caught, Escaped }

// Picked in the lobby by survivors; a hunter's perk is ignored.
public enum Perk : byte { None, Sprinter, Medic, Scout }

// Lives in its own asmdef with no engine references so an EditMode test can reference it.
// Everything a designer would tune (speeds, cooldowns, points) is here, next to its test.
public static class RoundRules
{
    // alive counts survivors still in play (Alive or Downed). total == 0 means a solo host who is
    // the hunter - only the timer may end that round, not "everyone is gone".
    public static RoundOutcome Evaluate(int alive, int escaped, int total, bool timeUp)
    {
        if (total > 0 && alive == 0)
            return escaped > 0 ? RoundOutcome.SurvivorsEscaped : RoundOutcome.HunterCaughtAll;
        return timeUp ? RoundOutcome.HunterTimeout : RoundOutcome.None;
    }

    // ---- hunter rage: the hunter gets stronger as the round drags on or the cheese piles up ----

    // ponytail: capped so a raged hunter's sprint (18 * 1.10 = 19.8) still trails a fresh survivor
    // sprint (20) - Tools/check_balance.py asserts it. Raise both together or not at all.
    public const float MaxRageSpeedBoost = 0.10f;
    public const float SenseCooldownCalm = 25f;
    public const float SenseCooldownRaged = 15f;

    // 0..1: half from how much of the door's cheese is banked, half from how much of the timer is gone.
    public static float Rage(int cheese, int needed, float elapsed, float duration)
    {
        float c = needed > 0 ? Clamp01((float)cheese / needed) : 0f;
        float t = duration > 0f ? Clamp01(elapsed / duration) : 0f;
        return 0.5f * c + 0.5f * t;
    }

    public static float RageSpeedScale(float rage) => 1f + MaxRageSpeedBoost * Clamp01(rage);

    public static float SenseCooldown(float rage) =>
        SenseCooldownCalm + (SenseCooldownRaged - SenseCooldownCalm) * Clamp01(rage);

    // ---- who hunts ----

    public static int HunterCount(int players) => players >= 6 ? 2 : 1;

    // Least-hunted clients first so the role rotates; random among ties. next(n) returns [0, n).
    public static List<ulong> PickHunters(IReadOnlyList<ulong> clients, Func<ulong, int> huntsOf, int count, Func<int, int> next)
    {
        List<ulong> pool = new List<ulong>(clients);
        List<ulong> picked = new List<ulong>();

        while (picked.Count < count && pool.Count > 0)
        {
            int fewest = int.MaxValue;
            foreach (ulong c in pool) fewest = Math.Min(fewest, huntsOf(c));

            List<ulong> ties = pool.FindAll(c => huntsOf(c) == fewest);
            ulong pick = ties[next(ties.Count)];
            picked.Add(pick);
            pool.Remove(pick);
        }
        return picked;
    }

    // ---- match score, kept across rounds until the session ends ----

    // Escaped 3, still in play when it ended 1, out 0. Downed at the end is out.
    public static int SurvivorPoints(LifeState final) =>
        final == LifeState.Escaped ? 3 : final == LifeState.Alive ? 1 : 0;

    // caught = survivors Caught or Downed at the end. Shared by every hunter that round.
    public static int HunterPoints(int caught) => 2 * caught;

    // ---- carrying cheese ----

    public const int CarryCap = 2;

    public static float CarrySpeedScale(int carried) =>
        (float)Math.Pow(0.93, Math.Max(0, Math.Min(carried, CarryCap)));

    // ---- perks ----

    public const float BaseReviveSeconds = 5f;

    public static float RegenScale(Perk p) => p == Perk.Sprinter ? 1.5f : 1f;
    public static float DrainScale(Perk p) => p == Perk.Sprinter ? 0.75f : 1f;
    public static float ReviveSeconds(Perk p) => p == Perk.Medic ? 3f : BaseReviveSeconds;

    // ---- tags: what a trap does to the survivor who steps on it ----

    public const float SlowScale = 0.7f;

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
