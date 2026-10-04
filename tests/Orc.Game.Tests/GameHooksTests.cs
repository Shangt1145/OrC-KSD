using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2A 验收锚点⑤（hooks 六项）：常量与载荷键（冻结字面值）、发射入口就绪（零调用点四项经测试发射）、
/// drawn → hand.add 并存（粒度不同；硬性位于 turn.start 与 turn.start.after 之间）、
/// card.load 真实调用（初始化逐张；排序＝玩家索引升序；逐位稳定）、起手静默。
/// </summary>
public class GameHooksTests
{
    [Fact]
    public void Hook_Constants_Have_Exact_Frozen_Literals()
    {
        Assert.Equal("card.load", GameUpdates.CardLoad);
        Assert.Equal("card.hand.add", GameUpdates.CardHandAdd);
        Assert.Equal("card.died", GameUpdates.CardDied);
        Assert.Equal("unit.joined", GameUpdates.UnitJoined);
        Assert.Equal("unit.deployed", GameUpdates.UnitDeployed);
        Assert.Equal("unit.position.changed", GameUpdates.UnitPositionChanged);
    }

    [Fact]
    public void Hook_Payload_Keys_Have_Exact_Frozen_Literals()
    {
        Assert.Equal("Unit", GameUpdates.PayloadUnit);
        Assert.Equal("Position", GameUpdates.PayloadPosition);
        Assert.Equal("OldPosition", GameUpdates.PayloadOldPosition);
        Assert.Equal("NewPosition", GameUpdates.PayloadNewPosition);
        // card.load / card.hand.add 复用既有载荷键（Card / Player）
        Assert.Equal("Card", GameUpdates.PayloadCard);
        Assert.Equal("Player", GameUpdates.PayloadPlayer);
    }

    [Fact]
    public void Hook_Constants_Do_Not_Collide_With_Existing_Or_Builtin_Constant_Sets()
    {
        var hooks = new[]
        {
            GameUpdates.CardLoad, GameUpdates.CardHandAdd, GameUpdates.CardDied,
            GameUpdates.UnitJoined, GameUpdates.UnitDeployed, GameUpdates.UnitPositionChanged,
        };
        var firstBatch = new[]
        {
            GameUpdates.TurnStartBefore, GameUpdates.TurnStart, GameUpdates.TurnStartAfter,
            GameUpdates.TurnEndBefore, GameUpdates.TurnEnd,
            GameUpdates.CardPlayed, GameUpdates.CardDrawn, GameUpdates.CardStatChanged,
        };
        var builtin = new[] { Updates.CardPlaced, Updates.CardDestroyed, Updates.CardData, Updates.EffectRemoved };

        Assert.Equal(6, hooks.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(hooks, firstBatch.Contains);
        Assert.DoesNotContain(hooks, builtin.Contains);
        // card.died 与引擎内置 card.destroyed 并存（近似名不同义）
        Assert.NotEqual(Updates.CardDestroyed, GameUpdates.CardDied);
    }

    [Fact]
    public async Task Zero_Callpoint_Hooks_Are_Emittable_With_Frozen_Payloads()
    {
        var engine = new LogicEngine();
        using var recorder = new UpdateRecorder(engine);
        var unit = new UnitCard(engine, new CardDefinition("单位", 1, 2, 3, 4));
        var line = new BattleLine(4);
        var oldPosition = line[0];
        var newPosition = line[1];

        await GameUpdates.EmitCardDied(engine, unit);
        await GameUpdates.EmitUnitJoined(engine, unit, oldPosition);
        await GameUpdates.EmitUnitDeployed(engine, unit, newPosition);
        await GameUpdates.EmitUnitPositionChanged(engine, unit, oldPosition, newPosition);

        Assert.Equal(
            new[] { GameUpdates.CardDied, GameUpdates.UnitJoined, GameUpdates.UnitDeployed, GameUpdates.UnitPositionChanged },
            recorder.Types);
        // card.died 载荷 {Card}
        Assert.Same(unit, recorder.Updates[0].Payload![GameUpdates.PayloadCard]);
        // unit.joined / unit.deployed 载荷 {Unit, Position}
        Assert.Same(unit, recorder.Updates[1].Payload![GameUpdates.PayloadUnit]);
        Assert.Same(oldPosition, recorder.Updates[1].Payload![GameUpdates.PayloadPosition]);
        Assert.Same(unit, recorder.Updates[2].Payload![GameUpdates.PayloadUnit]);
        Assert.Same(newPosition, recorder.Updates[2].Payload![GameUpdates.PayloadPosition]);
        // unit.position.changed 载荷 {Unit, OldPosition, NewPosition}
        Assert.Same(unit, recorder.Updates[3].Payload![GameUpdates.PayloadUnit]);
        Assert.Same(oldPosition, recorder.Updates[3].Payload![GameUpdates.PayloadOldPosition]);
        Assert.Same(newPosition, recorder.Updates[3].Payload![GameUpdates.PayloadNewPosition]);
    }

    [Fact]
    public async Task Draw_Chain_Emits_Drawn_Then_HandAdd_Between_TurnStart_And_After()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();
        recorder.Clear();

        await match.EndTurn(); // 回合 2：后手抽牌

        var types = recorder.Types.ToList();
        var drawnIndex = types.IndexOf(GameUpdates.CardDrawn);
        Assert.True(drawnIndex > types.IndexOf(GameUpdates.TurnStart)); // 硬性位置：turn.start 之后
        Assert.Equal(GameUpdates.CardHandAdd, types[drawnIndex + 1]); // 顺序：drawn 先、hand.add 后
        Assert.True(types.IndexOf(GameUpdates.CardHandAdd) < types.IndexOf(GameUpdates.TurnStartAfter)); // 且先于 after

        // 并存语义、粒度不同：drawn（抽牌动作）与 hand.add（进手牌归属）载荷同卡同玩家
        var drawn = recorder.Updates[drawnIndex];
        var handAdd = recorder.Updates[drawnIndex + 1];
        Assert.Same(drawn.Payload![GameUpdates.PayloadCard], handAdd.Payload![GameUpdates.PayloadCard]);
        Assert.Same(match.Players[1], drawn.Payload[GameUpdates.PayloadPlayer]);
        Assert.Same(match.Players[1], handAdd.Payload[GameUpdates.PayloadPlayer]);
    }

    [Fact]
    public async Task Card_Load_Is_Emitted_Per_Card_Grouped_By_Player_Index()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();

        var loads = recorder.Updates.Where(u => u.Type == GameUpdates.CardLoad).ToList();
        Assert.Equal(GameTestData.StandardDeckSize * 2, loads.Count); // 数量＝双方卡组总数
        Assert.All(loads.Take(GameTestData.StandardDeckSize),
            u => Assert.Same(match.Players[0], u.Payload![GameUpdates.PayloadPlayer])); // A 组在前
        Assert.All(loads.Skip(GameTestData.StandardDeckSize),
            u => Assert.Same(match.Players[1], u.Payload![GameUpdates.PayloadPlayer])); // B 组在后
        // 起手静默：装载不发 drawn / hand.add
        Assert.DoesNotContain(GameUpdates.CardDrawn, recorder.Types);
        Assert.DoesNotContain(GameUpdates.CardHandAdd, recorder.Types);
    }

    [Fact]
    public async Task Card_Load_Order_Is_Bitwise_Stable_For_Same_Seed()
    {
        var m1 = GameTestData.CreateStandardMatch(seed: 777);
        var m2 = GameTestData.CreateStandardMatch(seed: 777);
        using var r1 = new UpdateRecorder(m1.Engine);
        using var r2 = new UpdateRecorder(m2.Engine);

        await m1.Initialize();
        await m2.Initialize();

        // 同种子＋同参 → 加载顺序与内容逐位一致（可复现）
        Assert.Equal(LoadNamesOf(r1), LoadNamesOf(r2));

        static string[] LoadNamesOf(UpdateRecorder recorder) => recorder.Updates
            .Where(u => u.Type == GameUpdates.CardLoad)
            .Select(u => ((Orc.Cards.Card)u.Payload![GameUpdates.PayloadCard]!).Name)
            .ToArray();
    }

    [Fact]
    public async Task Hook_Emission_Helpers_Validate_Arguments()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();
        var engine = match.Engine;
        var player = match.Players[0];
        var unit = new UnitCard(engine, new CardDefinition("单位", 1, 2, 3, 4));
        var line = new BattleLine(4);

        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardLoad(null!, player, unit));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardLoad(engine, null!, unit));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardLoad(engine, player, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardDied(engine, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitUnitJoined(engine, unit, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitUnitPositionChanged(engine, unit, null!, line[1]));
    }
}
