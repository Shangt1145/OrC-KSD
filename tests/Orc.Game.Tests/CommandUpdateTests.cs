using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2C 验收⑤（更新发射）：unit.position.changed（移动时，恰一次；载荷 {Unit, OldPosition, NewPosition}——见 CommandSystemTests）；
/// card.died（死亡时，恰一次；死亡状态就绪后发射——槽位释放 / IsDestroyed 置位 / Position 置空先于发射）；
/// 死亡不发 unit.position.changed（该更新专属移动语义）；与引擎内置 card.destroyed 不同义（不混用）；
/// 攻击（无死亡）与移动（无死亡）的更新纯净性（不产生额外更新）。
/// </summary>
public class CommandUpdateTests
{
    [Fact]
    public async Task Attack_Without_Deaths_Emits_No_Updates()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attacker = await CommandTestKit.PrepareOnFrontAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var target = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.InfantryId, 2);
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target.Ref);

        // 攻击互伤但无死亡：不产生任何更新（更新仅位置变化与死亡两类；结算数值静默变更）。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.Empty(recorder.Updates);
    }

    [Fact]
    public async Task Card_Died_Arrives_With_Death_State_Ready()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attacker = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var target = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.WeakId, 0);
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        var targetSlot = match.Battlefield.FrontLine[0];

        // 发射时点探针：card.died 到达时死亡状态已就绪（消费者查询不到半死状态）。
        var probes = new List<(bool SlotFreed, bool Destroyed, bool PositionCleared)>();
        using var probe = match.Engine.Subscribe((type, payload, _) =>
        {
            if (type == GameUpdates.CardDied)
            {
                var card = (UnitCard)payload![GameUpdates.PayloadCard]!;
                var state = card.GetData<UnitStateData>();
                probes.Add((targetSlot.IsEmpty, state.IsDestroyed, state.Position is null));
            }

            return Task.CompletedTask;
        });

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target.Ref);

        Assert.Equal(CommandResultStatus.Success, result.Status);
        var state = Assert.Single(probes);
        Assert.True(state.SlotFreed);      // 槽位释放先于发射
        Assert.True(state.Destroyed);      // IsDestroyed 置位先于发射
        Assert.True(state.PositionCleared); // Position 置空先于发射
    }

    [Fact]
    public async Task Death_Does_Not_Emit_Position_Changed_Nor_Engine_CardDestroyed()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attacker = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var target = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.WeakId, 0);
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target.Ref);

        // 死亡不发 unit.position.changed（专属移动语义）；与引擎内置 card.destroyed 不同义（游戏层死亡＝card.died）。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.Equal(new[] { GameUpdates.CardDied }, recorder.Types);
        Assert.DoesNotContain(GameUpdates.UnitPositionChanged, recorder.Types);
        Assert.DoesNotContain(Updates.CardDestroyed, recorder.Types);
        Assert.NotEqual(Updates.CardDestroyed, GameUpdates.CardDied);
    }

    [Fact]
    public async Task Move_Emits_Only_Position_Changed()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var player = match.Players[0];
        var unit = await CommandTestKit.PrepareOnSupportAsync(match, player, CommandTestKit.InfantryId, 1);
        CommandTestKit.Activate(unit);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, unit, match.Battlefield.FrontLine[3].Ref);

        // 移动纯净性：仅 unit.position.changed 一条（不发 card.died / card.played / turn 类）。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.Equal(new[] { GameUpdates.UnitPositionChanged }, recorder.Types);
        Assert.DoesNotContain(GameUpdates.CardDied, recorder.Types);
    }
}
