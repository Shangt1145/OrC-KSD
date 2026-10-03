using Orc.Core;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 更新常量与发射 API（R2 最低满足形态）：
/// ①8 条常量齐备且字面值断言通过（对外订阅契约、一旦定稿即冻结）；
/// ②turn 五连与 card.drawn 在场景/循环测试中被实际发射与断言（见 TurnCycleTests / ScenarioTests）；
/// ③card.played / card.stat.changed 存在性断言 ＋"可发射"冒烟（零调用点——调用点后续批次）。
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
        var engine = new LogicEngine();
        using var recorder = new UpdateRecorder(engine);

        await GameUpdates.EmitCardPlayed(engine); // 空载荷（载荷随调用点批次定型）
        await GameUpdates.EmitCardStatChanged(engine); // 无载荷＝硬约定

        Assert.Equal(new[] { GameUpdates.CardPlayed, GameUpdates.CardStatChanged }, recorder.Types);
        Assert.Null(recorder.Updates[0].Payload);
        Assert.Null(recorder.Updates[1].Payload);
        // 更新条目照常写入总流（发射路径经总线 Emit 的既有通路）
        Assert.Equal(2, engine.RootStream.Entries.Count(e => e.Kind == LogEntryKind.Update));
    }

    [Fact]
    public async Task Emission_Helper_Validates_Arguments()
    {
        var engine = new LogicEngine();
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardPlayed(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardStatChanged(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GameUpdates.EmitCardDrawn(engine, null!, null!));
    }
}
