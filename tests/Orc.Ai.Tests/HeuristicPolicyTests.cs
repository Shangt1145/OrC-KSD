using Orc.UI.Contracts;
using Xunit;

namespace Orc.Ai.Tests;

public sealed class HeuristicPolicyTests
{
    [Fact]
    public void PlaysHighestCostUnitFirst()
    {
        var view = Match(
            kredits: 5,
            hand:
            [
                Unit("cheap", cost: 1, attack: 3, defense: 3),
                Unit("expensive", cost: 3, attack: 2, defense: 2),
            ]);

        var intent = HeuristicPolicy.DecidePlay(view);

        Assert.NotNull(intent);
        Assert.Equal(HeuristicPolicy.PlayUnit, intent!.Kind);
        Assert.Equal("expensive", intent.Ref);
    }

    [Fact]
    public void FreeSupportSlot_SkipsUnitAndHqOccupiedSlots()
    {
        var view = Match(
            kredits: 3,
            hand: [Unit("u", cost: 1, attack: 1, defense: 1)],
            line:
            [
                Unit("on-support-0", cost: 1, attack: 1, defense: 1, zone: "support", slot: 0),
                Unit("on-support-1", cost: 1, attack: 1, defense: 1, zone: "support", slot: 1),
            ],
            hqSlot: 2);

        Assert.Equal(3, HeuristicPolicy.FreeSupportSlot(view));
    }

    [Fact]
    public void AttacksStrongestUnitFirst()
    {
        var view = Match(
            kredits: 2,
            line:
            [
                Unit("weak", cost: 0, attack: 2, defense: 2, zone: "frontline", slot: 0, canAttack: true),
                Unit("strong", cost: 0, attack: 5, defense: 1, zone: "frontline", slot: 1, canAttack: true),
            ]);

        var intent = HeuristicPolicy.Decide(view, attackable: new HashSet<string>(["weak", "strong"], StringComparer.Ordinal));

        Assert.NotNull(intent);
        Assert.Equal(HeuristicPolicy.Attack, intent!.Kind);
        Assert.Equal("strong", intent.Ref);
        Assert.Equal(5, intent.AttackPower);
    }

    [Fact]
    public void ReturnsNull_WhenNothingToDo()
    {
        var view = Match(kredits: 0);
        Assert.Null(HeuristicPolicy.Decide(view));
    }

    [Fact]
    public void DoesNotReuseActiveCounter()
    {
        var view = Match(
            kredits: 5,
            hand: [Card("counter", "counter", cost: 1)],
            counterCount: 1);

        Assert.Null(HeuristicPolicy.DecidePlay(view));
    }

    private static UiMatchView Match(
        int kredits,
        IReadOnlyList<UiCardView>? hand = null,
        IReadOnlyList<UiCardView>? line = null,
        int hqSlot = 2,
        int counterCount = 0)
        => new()
        {
            MatchId = "test",
            Phase = "play",
            ActivePlayerSide = "self",
            Turn = 1,
            SelfKredits = kredits,
            SelfMaxKredits = kredits,
            SupportLineSlotCount = 5,
            FrontLineSlotCount = 5,
            SelfHand = hand ?? Array.Empty<UiCardView>(),
            SelfLine = line ?? Array.Empty<UiCardView>(),
            SelfCounterCount = counterCount,
            SelfHq = new UiCardView
            {
                Uid = "hq",
                IsHq = true,
                Zone = "support",
                SlotIndex = hqSlot,
                Definition = new UiCardDefinition { CardId = "hq", CardType = "hq" },
            },
        };

    private static UiCardView Unit(
        string uid,
        int cost,
        int attack,
        int defense,
        string zone = "support",
        int slot = -1,
        bool canAttack = false)
        => new()
        {
            Uid = uid,
            Zone = zone,
            SlotIndex = slot,
            EffectiveCost = cost,
            EffectiveAttack = attack,
            EffectiveDefense = defense,
            EffectiveOpCost = 1,
            CanAttack = canAttack,
            CanMoveAndAttack = attack > 0,
            Definition = new UiCardDefinition
            {
                CardId = uid,
                CardType = "unit",
                Cost = cost,
                BaseAttack = attack,
                BaseDefense = defense,
            },
        };

    private static UiCardView Card(string uid, string cardType, int cost)
        => new()
        {
            Uid = uid,
            Zone = "hand",
            EffectiveCost = cost,
            Definition = new UiCardDefinition { CardId = uid, CardType = cardType, Cost = cost },
        };
}
