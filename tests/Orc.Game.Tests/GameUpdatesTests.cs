using Orc.Core;
using Orc.Game.Cards;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 更新常量与发射 API（R2 最低满足形态）：
/// ①8 条常量齐备且字面值断言通过（对外订阅契约、一旦定稿即冻结）；
/// ②turn 五连与 card.drawn 在场景/循环测试中被实际发射与断言（见 TurnCycleTests / ScenarioTests）；
/// ③card.played（W4-1 升级）：载荷化契约（{ Card, Player }——被使用卡实例＋使用方）的发射冒烟与逐参校验
/// （旧"空载荷"断言随升级同步；实际发射调用点＝部署/指令打出链，见 PlayChain*）；
/// ④card.stat.changed（W2a 升级）：载荷化契约（{ Card, ChangedFields }——目标卡＋变化字段集合）的发射冒烟与参数校验
/// （旧"无载荷＝硬约定"断言随升级同步；实际发射调用点＝修饰机制集中触发，见 ModifierSystemTests / StatUpdatePipelineTests）。
/// </summary>
public class GameUpdatesTests
{
    [Fact]
    public void Constants_Have_Exact_Contract_Literals()
    {
        Assert.Equal("turn.start.before", GameUpdates.TurnStartBefore);
        Assert.Equal("turn.start", GameUpdates.TurnStart);
        Assert.Equal("turn.start.after", GameUpdates.TurnStartAfter);
        Assert.Equal("turn.end.before", GameUpdates.TurnEndBefore);
        Assert.Equal("turn.end", GameUpdates.TurnEnd);
        Assert.Equal("card.played", GameUpdates.CardPlayed);
        Assert.Equal("card.drawn", GameUpdates.CardDrawn);
        Assert.Equal("card.stat.changed", GameUpdates.CardStatChanged);
    }

    [Fact]
    public void Constants_Do_Not_Collide_With_Orc_Builtin_Updates()
    {
        // 均新定义、与引擎既有常量集无重叠
        var gameUpdates = new[]
        {
            GameUpdates.TurnStartBefore,
            GameUpdates.TurnStart,
            GameUpdates.TurnStartAfter,
            GameUpdates.TurnEndBefore,
            GameUpdates.TurnEnd,
            GameUpdates.CardPlayed,
            GameUpdates.CardDrawn,
            GameUpdates.CardStatChanged,
        };
        var builtin = new[] { Updates.CardPlaced, Updates.CardDestroyed, Updates.CardData, Updates.EffectRemoved };
        Assert.DoesNotContain(gameUpdates, builtin.Contains);
        Assert.Equal(8, gameUpdates.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Emission_Paths_Of_CardPlayed_And_CardStatChanged_Are_Ready()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();
        var engine = match.Engine;
        var player = match.Players[0];
        using var recorder = new UpdateRecorder(engine);
        var unit = new UnitCard(engine, new CardDefinition("单位", 1, 2, 3, 4, faction: Faction.Germany, rarity: Rarity.Standard));
        var changedFields = new[] { CardStatFields.Attack };
        var updatesBefore = engine.RootStream.Entries.Count(e => e.Kind == LogEntryKind.Update);

        // W4-1 升级：card.played 载荷化（{ Card, Player }——被使用卡实例＋使用方）
        await GameUpdates.EmitCardPlayed(engine, unit, player);
        // W2a 升级：card.stat.changed 载荷化（{ Card, ChangedFields }——目标卡＋本轮变化字段集合）
        await GameUpdates.EmitCardStatChanged(engine, unit, changedFields);

        Assert.Equal(new[] { GameUpdates.CardPlayed, GameUpdates.CardStatChanged }, recorder.Types);
        var playedPayload = recorder.Updates[0].Payload;
        Assert.NotNull(playedPayload);
        Assert.Same(unit, playedPayload![GameUpdates.PayloadCard]);
        Assert.Same(player, playedPayload[GameUpdates.PayloadPlayer]);
        var payload = recorder.Updates[1].Payload;
        Assert.NotNull(payload);
        Assert.Same(unit, payload![GameUpdates.PayloadCard]);
        var recordedFields = Assert.IsAssignableFrom<IReadOnlyList<string>>(payload[GameUpdates.PayloadChangedFields]);
        Assert.Equal(changedFields, recordedFields);
        // 更新条目照常写入总流（发射路径经总线 Emit 的既有通路）
        Assert.Equal(updatesBefore + 2, engine.RootStream.Entries.Count(e => e.Kind == LogEntryKind.Update));
    }

    [Fact]
    public async Task Emission_Helper_Validates_Arguments()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();
        var engine = match.Engine;
        var player = match.Players[0];
        var unit = new UnitCard(engine, new CardDefinition("单位", 1, 2, 3, 4, faction: Faction.Germany, rarity: Rarity.Standard));
        var fields = new[] { CardStatFields.Attack };

        // W4-1：card.played 升级为三参（engine / card / player）——逐参校验
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardPlayed(null!, unit, player));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardPlayed(engine, null!, player));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardPlayed(engine, unit, null!));
        // W4-1：deck.shuffled 发射助手逐参校验
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitDeckShuffled(null!, player, player.Deck));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitDeckShuffled(engine, null!, player.Deck));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitDeckShuffled(engine, player, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardStatChanged(null!, unit, fields));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardStatChanged(engine, null!, fields));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardStatChanged(engine, unit, null!));
        // 空集合＝无变更不发（逻辑错误——fail-fast 拒绝）
        await Assert.ThrowsAsync<ArgumentException>(() => GameUpdates.EmitCardStatChanged(engine, unit, Array.Empty<string>()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardDrawn(engine, null!, null!));
        // W3-A3：card.placed 发射助手逐参校验
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardPlaced(null!, unit));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardPlaced(engine, null!));
    }
}
