using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 验收锚点⑤：骨架场景"双人对局·两回合循环"端到端——
/// 初始化（2A：40 张 card.load ＋ 3 条回合开始）→ 回合 1 开始 → EndTurn → 回合 2（7 条更新：含 drawn → hand.add 连发）→ 更新序列与资源数值断言；
/// 附：回合 3（先手第二次回合）照抽（唯一例外为先手第 1 回合）、同种子＋同参数逐位一致复现。
/// </summary>
public class ScenarioTests
{
    [Fact]
    public async Task Two_Turn_Cycle_EndToEnd_Full_Sequence_And_Resource_Assertions()
    {
        var match = GameTestData.CreateStandardMatch(seed: 2026);
        using var recorder = new UpdateRecorder(match.Engine); // 订阅在 Initialize 前挂接

        // ---- 初始化＝洗牌 → 加载（40 张 card.load）→ 起手装载（静默）→ 回合 1 开始（3 条；先手第 1 回合不抽）----
        await match.Initialize();
        var expectedLoads = GameTestData.StandardDeckSize * 2;
        Assert.Equal(expectedLoads + 3, recorder.Types.Count);
        Assert.Equal(expectedLoads, recorder.Types.Count(t => t == GameUpdates.CardLoad));
        Assert.Equal(
            new[] { GameUpdates.TurnStartBefore, GameUpdates.TurnStart, GameUpdates.TurnStartAfter },
            recorder.Types.Skip(expectedLoads));

        var first = match.Players[0];
        var second = match.Players[1];
        Assert.Equal(1, match.TurnNumber);
        Assert.Same(first, match.CurrentPlayer);
        Assert.Equal(4, first.Hand.Count); // 先手 4
        Assert.Equal(5, second.Hand.Count); // 后手 5
        Assert.Equal((1, 1), (first.PointSlots, first.Points)); // 第 1 回合＝1 点

        // ---- 第一循环：回合 1 先手结束 → 回合 2 后手（7 条更新：抽牌位＝drawn → hand.add 连发）----
        recorder.Clear();
        await match.EndTurn();
        Assert.Equal(
            new[]
            {
                GameUpdates.TurnEndBefore,
                GameUpdates.TurnEnd,
                GameUpdates.TurnStartBefore,
                GameUpdates.TurnStart,
                GameUpdates.CardDrawn,
                GameUpdates.CardHandAdd,
                GameUpdates.TurnStartAfter,
            },
            recorder.Types);

        // 资源/回合数值断言（API 返回即就绪：全部结算与更新已完结）
        Assert.Equal(2, match.TurnNumber);
        Assert.Same(second, match.CurrentPlayer);
        Assert.Equal((1, 1), (second.PointSlots, second.Points)); // 后手：槽 1、点数 1（照抽）
        Assert.Equal((1, 0), (first.PointSlots, first.Points)); // 先手：点数已清零、槽保留
        Assert.Equal(6, second.Hand.Count); // 5 + 抽 1
        Assert.Equal(4, first.Hand.Count);
        Assert.Equal(GameTestData.StandardDeckSize - 5 - 1, second.Deck.Count); // 卡组同步消耗

        // ---- 回合 3（先手第二次回合）：照常抽 1（抽牌例外仅先手第 1 回合）----
        recorder.Clear();
        await match.EndTurn();
        Assert.Equal(
            new[]
            {
                GameUpdates.TurnEndBefore,
                GameUpdates.TurnEnd,
                GameUpdates.TurnStartBefore,
                GameUpdates.TurnStart,
                GameUpdates.CardDrawn,
                GameUpdates.CardHandAdd,
                GameUpdates.TurnStartAfter,
            },
            recorder.Types);
        Assert.Equal(3, match.TurnNumber);
        Assert.Same(first, match.CurrentPlayer);
        Assert.Equal((2, 2), (first.PointSlots, first.Points)); // 槽 +1 → 2；点数＝槽值
        Assert.Equal(5, first.Hand.Count); // 4 + 抽 1
        Assert.Equal((1, 0), (second.PointSlots, second.Points)); // 后手点数清零
    }

    [Fact]
    public async Task Same_Seed_And_Parameters_Produce_Identical_Shuffle_Hands_And_Draws()
    {
        // 可复现（强语义）：同种子＋同创建参数 → 洗牌序列、起手装载、后续抽牌逐位一致
        var m1 = GameTestData.CreateStandardMatch(seed: 777);
        var m2 = GameTestData.CreateStandardMatch(seed: 777);

        await m1.Initialize();
        await m2.Initialize();

        // 洗牌＋起手后：卡组剩余 id 序列逐位一致
        Assert.Equal(m1.Players[0].Deck.ToArray(), m2.Players[0].Deck.ToArray());
        Assert.Equal(m1.Players[1].Deck.ToArray(), m2.Players[1].Deck.ToArray());
        // 起手（名称序列，与 id 一一对应）逐位一致
        Assert.Equal(NamesOf(m1.Players[0]), NamesOf(m2.Players[0]));
        Assert.Equal(NamesOf(m1.Players[1]), NamesOf(m2.Players[1]));

        // 抽牌序列逐位一致（推进两回合：回合 2 后手抽 / 回合 3 先手抽）
        await m1.EndTurn();
        await m2.EndTurn();
        await m1.EndTurn();
        await m2.EndTurn();

        Assert.Equal(m1.Players[0].Deck.ToArray(), m2.Players[0].Deck.ToArray());
        Assert.Equal(m1.Players[1].Deck.ToArray(), m2.Players[1].Deck.ToArray());
        Assert.Equal(NamesOf(m1.Players[0]), NamesOf(m2.Players[0]));
        Assert.Equal(NamesOf(m1.Players[1]), NamesOf(m2.Players[1]));
    }

    private static string[] NamesOf(Orc.Game.Players.Player player)
        => player.Hand.Select(c => c.Name).ToArray();
}
