using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 验收锚点②：回合循环——5 个 turn 系列更新按序广播（可订阅断言）。
/// 覆盖：EndTurn 完整观测序列（含 card.drawn 硬性插入位置：turn.start 之后、turn.start.after 之前）、
/// 仅 turn 系列的相对顺序、抽牌例外（先手第 1 回合不抽；其余照抽）、card.drawn 载荷（{ 玩家, 卡牌实例 }）。
/// </summary>
public class TurnCycleTests
{
    [Fact]
    public async Task EndTurn_Broadcasts_Full_Sequence_With_CardDrawn_Between_Start_And_After()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();
        recorder.Clear();

        await match.EndTurn(); // 回合 1 先手 → 回合 2 后手（后手照抽 1）

        Assert.Equal(
            new[]
            {
                GameUpdates.TurnEndBefore,
                GameUpdates.TurnEnd,
                GameUpdates.TurnStartBefore,
                GameUpdates.TurnStart,
                GameUpdates.CardDrawn, // 硬性位置：turn.start 之后、turn.start.after 之前
                GameUpdates.TurnStartAfter,
            },
            recorder.Types);
    }

    [Fact]
    public async Task EndTurn_Broadcasts_Five_Turn_Series_Updates_In_Fixed_Order()
    {
        var match = GameTestData.CreateStandardMatch(seed: 7);
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();
        recorder.Clear();

        await match.EndTurn();

        // 过滤出 turn 系列：5 条按序（end.before → end → start.before → start → start.after）
        var turnSeries = recorder.Types
            .Where(t => t.StartsWith("turn.", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            new[]
            {
                GameUpdates.TurnEndBefore,
                GameUpdates.TurnEnd,
                GameUpdates.TurnStartBefore,
                GameUpdates.TurnStart,
                GameUpdates.TurnStartAfter,
            },
            turnSeries);
    }

    [Fact]
    public async Task First_Turn_Of_First_Player_Skips_Draw_While_Subsequent_Turns_Draw()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        using var recorder = new UpdateRecorder(match.Engine);

        await match.Initialize(); // 回合 1（先手首回合）：唯一抽牌例外——不抽、无 card.drawn
        Assert.DoesNotContain(GameUpdates.CardDrawn, recorder.Types);
        Assert.Equal(4, match.Players[0].Hand.Count);
        Assert.Equal(GameTestData.StandardDeckSize - 4, match.Players[0].Deck.Count);

        recorder.Clear();
        await match.EndTurn(); // 回合 2（后手首回合）：照抽 1
        Assert.Contains(GameUpdates.CardDrawn, recorder.Types);
        Assert.Equal(6, match.Players[1].Hand.Count);

        recorder.Clear();
        await match.EndTurn(); // 回合 3（先手第二次回合）：照抽 1
        Assert.Contains(GameUpdates.CardDrawn, recorder.Types);
        Assert.Equal(5, match.Players[0].Hand.Count);
    }

    [Fact]
    public async Task CardDrawn_Payload_Carries_Player_And_Card_Instance()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();
        recorder.Clear();

        await match.EndTurn();

        var drawnUpdate = recorder.Updates.Single(u => u.Type == GameUpdates.CardDrawn);
        var payload = drawnUpdate.Payload!;
        Assert.Same(match.Players[1], payload[GameUpdates.PayloadPlayer]); // 抽牌方＝后手（当前回合方）
        var card = Assert.IsType<Orc.Cards.Card>(payload[GameUpdates.PayloadCard]);
        Assert.Contains(card, match.Players[1].Hand); // 抽到的卡已入手牌
        Assert.StartsWith("卡", card.Name);
    }

    [Fact]
    public async Task Turn_Series_Payload_Carries_Player_And_TurnNumber()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();
        recorder.Clear();

        await match.EndTurn();

        var endBefore = recorder.Updates[0];
        Assert.Equal(GameUpdates.TurnEndBefore, endBefore.Type);
        Assert.Same(match.Players[0], endBefore.Payload![GameUpdates.PayloadPlayer]); // 结束方＝先手（回合 1）
        Assert.Equal(1, endBefore.Payload[GameUpdates.PayloadTurnNumber]);

        var startAfter = recorder.Updates[^1];
        Assert.Equal(GameUpdates.TurnStartAfter, startAfter.Type);
        Assert.Same(match.Players[1], startAfter.Payload![GameUpdates.PayloadPlayer]); // 新回合方＝后手（回合 2）
        Assert.Equal(2, startAfter.Payload[GameUpdates.PayloadTurnNumber]);
    }
}
