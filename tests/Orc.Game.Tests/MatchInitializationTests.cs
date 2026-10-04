using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Players;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 验收锚点①：对局初始化完整（双玩家＋5 管理器＋战场三线就绪）。
/// 覆盖：状态迁移（准备 → 进行）、五管理器就绪与访问、双玩家创建与初始值（资源/手牌/卡组/HQ）、
/// 战场三线与 HQ 占位（2A 槽位化：支援线含 HQ 占位〔槽 0〕、其余为空；前线全空）与容量可读、按玩家查询支援线、
/// 初始化更新序列（2A：40 张 card.load ＋ 3 条 turn.start.*；排序＝玩家索引升序；同一性＝加载实例即起手实例）、
/// 入总流、起手装载（先手 4 / 后手 5；静默）。
/// </summary>
public class MatchInitializationTests
{
    [Fact]
    public async Task Initialize_Assembles_Two_Players_Five_Managers_And_Battlefield()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        Assert.Equal(MatchState.Preparing, match.State); // 创建后＝准备态

        await match.Initialize();

        Assert.Equal(MatchState.InProgress, match.State);
        // 5 管理器就绪且可访问
        Assert.NotNull(match.TurnManager);
        Assert.NotNull(match.PlayerManager);
        Assert.NotNull(match.BattlefieldManager);
        Assert.NotNull(match.ResourceManager);
        Assert.NotNull(match.CardLibrary);
        // 双玩家就绪（转发不复制状态：同一列表实例）
        Assert.Equal(2, match.Players.Count);
        Assert.Same(match.PlayerManager.Players, match.Players);
        Assert.Equal(0, match.Players[0].Index);
        Assert.Equal(1, match.Players[1].Index);

        // 战场三线就绪（2A 槽位化受控变更：支援线各含 HQ 占位〔槽 0〕＋其余为空；前线全空）
        var lineA = match.Battlefield.PlayerASupportLine;
        var frontLine = match.Battlefield.FrontLine;
        var lineB = match.Battlefield.PlayerBSupportLine;
        Assert.Same(match.Players[0], lineA[0].Occupant); // HQ 占位＝对应玩家引用（玩家纯数据、无独立 HQ 实体）
        Assert.Same(match.Players[1], lineB[0].Occupant);
        for (var i = 1; i < lineA.Count; i++)
        {
            Assert.True(lineA[i].IsEmpty); // 支援线余格初始为空
        }

        for (var i = 1; i < lineB.Count; i++)
        {
            Assert.True(lineB[i].IsEmpty);
        }

        Assert.All(frontLine, slot => Assert.True(slot.IsEmpty)); // 前线 0 占位
        // 容量 4/5/4 可读
        Assert.Equal(4, match.Battlefield.PlayerASupportLineCapacity);
        Assert.Equal(5, match.Battlefield.FrontLineCapacity);
        Assert.Equal(4, match.Battlefield.PlayerBSupportLineCapacity);
        // 战场按玩家查询支援线（固定归属）
        Assert.Same(match.Battlefield.PlayerASupportLine, match.Battlefield.GetSupportLine(0));
        Assert.Same(match.Battlefield.PlayerBSupportLine, match.Battlefield.GetSupportLine(1));
        Assert.Same(match.Battlefield.PlayerASupportLine, match.Battlefield.GetSupportLine(match.Players[0]));
        Assert.Same(match.Battlefield.PlayerBSupportLine, match.Battlefield.GetSupportLine(match.Players[1]));
    }

    [Fact]
    public async Task Initialize_Emits_Card_Loads_Then_Turn_Start_Sequence_And_Loads_Opening_Hands_Silently()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        using var recorder = new UpdateRecorder(match.Engine); // 订阅须在 Initialize 前挂接（两步式设计保证可行）

        await match.Initialize();

        // 初始化期更新序列（2A 固定链）：双方卡组总数（40）张 card.load ＋ 3 条回合开始序列（先手第 1 回合不抽牌 → 无 card.drawn）
        var expectedLoads = GameTestData.StandardDeckSize * 2;
        Assert.Equal(expectedLoads + 3, recorder.Types.Count);
        Assert.Equal(expectedLoads, recorder.Types.Count(t => t == GameUpdates.CardLoad));
        Assert.Equal(
            new[] { GameUpdates.TurnStartBefore, GameUpdates.TurnStart, GameUpdates.TurnStartAfter },
            recorder.Types.Skip(expectedLoads));
        // 起手装载静默（装载不产生 drawn / hand.add 更新）
        Assert.DoesNotContain(GameUpdates.CardDrawn, recorder.Types);
        Assert.DoesNotContain(GameUpdates.CardHandAdd, recorder.Types);

        // card.load 载荷定型：{ Card, Player }——排序＝玩家索引升序（A 组在前、B 组在后；Player＝所属卡组玩家）
        Assert.All(recorder.Updates.Take(GameTestData.StandardDeckSize),
            u => Assert.Same(match.Players[0], u.Payload![GameUpdates.PayloadPlayer]));
        Assert.All(recorder.Updates.Skip(GameTestData.StandardDeckSize).Take(GameTestData.StandardDeckSize),
            u => Assert.Same(match.Players[1], u.Payload![GameUpdates.PayloadPlayer]));
        Assert.All(recorder.Updates.Take(expectedLoads),
            u => Assert.IsAssignableFrom<CardBase>(u.Payload![GameUpdates.PayloadCard]));

        // 同一性：加载实例＝起手所得实例（先手起手 4 张＝A 组加载序列前 4 张，引用相等）
        for (var i = 0; i < 4; i++)
        {
            Assert.Same(match.Players[0].Hand[i], recorder.Updates[i].Payload![GameUpdates.PayloadCard]);
        }

        // turn 系列载荷定型：{ 玩家, 回合数 }
        var firstTurnUpdate = recorder.Updates[expectedLoads];
        Assert.Equal(GameUpdates.TurnStartBefore, firstTurnUpdate.Type);
        Assert.Same(match.Players[0], firstTurnUpdate.Payload![GameUpdates.PayloadPlayer]);
        Assert.Equal(1, firstTurnUpdate.Payload[GameUpdates.PayloadTurnNumber]);

        // 起手装载：先手 4 / 后手 5（静默——装载不产生更新，见上断言）
        var first = match.Players[0];
        var second = match.Players[1];
        Assert.Equal(4, first.Hand.Count);
        Assert.Equal(5, second.Hand.Count);
        Assert.Equal(GameTestData.StandardDeckSize - 4, first.Deck.Count);
        Assert.Equal(GameTestData.StandardDeckSize - 5, second.Deck.Count);

        // 手牌来自卡组（装载链路：取首张并移除 → 实例化 → 入手牌）
        Assert.All(first.Hand, card => Assert.StartsWith("卡", card.Name));
        Assert.All(second.Hand, card => Assert.StartsWith("卡", card.Name));
    }

    [Fact]
    public async Task Initialize_First_Turn_State_Settles_Resources_For_First_Player()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();

        var first = match.Players[0];
        var second = match.Players[1];

        // 第 1 回合：先手槽 1、点数 1（统一 +1 结算、无特例分支）；后手未结算
        Assert.Equal(1, first.PointSlots);
        Assert.Equal(1, first.Points);
        Assert.Equal(0, second.PointSlots);
        Assert.Equal(0, second.Points);

        // 回合序（真源＝回合管理器）：回合数 1、当前行动方＝先手
        Assert.Equal(1, match.TurnNumber);
        Assert.Same(first, match.CurrentPlayer);
        Assert.Same(first, match.TurnManager.CurrentPlayer);

        // HQ 初始 20（纯数据、直接可读）
        Assert.Equal(20, first.HqHealth);
        Assert.Equal(20, second.HqHealth);
        Assert.Equal(Player.InitialHqHealth, first.HqHealth);

        // 手牌上限（9；仅数据、可读）
        Assert.Equal(9, Player.HandLimit);
    }

    [Fact]
    public async Task Initialize_Updates_Are_Written_To_Root_Stream()
    {
        // 初始化期无执行帧：43 条更新写入引擎总事件流（与"边界 Emit→总流"语义一致）
        var match = GameTestData.CreateStandardMatch(seed: 42);

        await match.Initialize();

        var updates = match.Engine.RootStream.Entries
            .Where(e => e.Kind == LogEntryKind.Update)
            .Select(e => e.Message)
            .ToArray();
        var expectedLoads = GameTestData.StandardDeckSize * 2;
        Assert.Equal(expectedLoads + 3, updates.Length);
        Assert.Equal(expectedLoads, updates.Count(u => u == GameUpdates.CardLoad));
        Assert.Equal(
            new[] { GameUpdates.TurnStartBefore, GameUpdates.TurnStart, GameUpdates.TurnStartAfter },
            updates.Skip(expectedLoads));
    }
}
