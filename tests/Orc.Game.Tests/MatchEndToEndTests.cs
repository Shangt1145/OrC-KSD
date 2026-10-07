using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Managers;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 01 完整流程 S5（DoD 证据）：脚本化桥接驱动一条端到端路径——
/// 建局 → Initialize（换牌相位）→ 双方换牌/确认 → 出牌（预行为＋确认）→ 独立移动 → 独立攻击 HQ 归零 → 终局。
/// 断言：相位迁移、信号序列、门禁拒绝与终局冻结。
/// </summary>
public class MatchEndToEndTests
{
    [Fact]
    public async Task Full_Match_From_Initialize_Through_Mulligan_To_HqZero_End()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge, skipMulligan: false);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];

        // ① 换牌相位（Initialize 完成装配与起手装载后置"换牌"、先手回合延后）
        Assert.Equal(MatchPhase.Mulligan, match.Phase);
        Assert.Equal(MatchState.Mulligan, match.State);

        var replaced = playerA.Hand[0];
        var replaceTask = match.BeginMulliganAsync(playerA);
        var (mulliganRequest, mulliganResponder) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(TargetSlotKind.MulliganSelect, Assert.Single(mulliganRequest.Slots).Kind);
        Assert.True(mulliganResponder.Complete(
            mulliganRequest.RequestId,
            TargeterTestKit.Selection(MulliganManager.SlotName, replaced.Ref)));
        Assert.Equal(MulliganResultStatus.Success, (await replaceTask).Status);

        using var recorder = new UpdateRecorder(match.Engine);

        // 后手不换牌（空选确认）
        var secondTask = match.BeginMulliganAsync(playerB);
        var (secondRequest, secondResponder) = await bridge.WaitForNextBeginAsync();
        Assert.True(secondResponder.Complete(
            secondRequest.RequestId, TargeterTestKit.Selection(MulliganManager.SlotName)));
        Assert.Equal(MulliganResultStatus.Success, (await secondTask).Status);

        // ② 双方确认＝进对局：先手第 1 回合序列恰一次
        Assert.Equal(MatchPhase.Play, match.Phase);
        Assert.Equal(MatchState.InProgress, match.State);
        Assert.Equal(
            new[] { GameUpdates.TurnStartBefore, GameUpdates.TurnStart, GameUpdates.TurnStartAfter },
            recorder.Types.Where(type => type.StartsWith("turn.", StringComparison.Ordinal)).ToArray());

        // ③ 出牌：步兵（部署费 1）经统一预行为入口＋桥接确认（落到 HQ 邻位空槽）
        var infantry = (UnitCard)match.CardLibrary.Instantiate(CommandTestKit.InfantryId);
        await infantry.LoadAsync(playerA);
        playerA.Hand.Add(infantry);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        var playTask = match.PlayManager.BeginUnitPrePlayAsync(infantry);
        var (playRequest, playResponder) = await bridge.WaitForNextBeginAsync();
        Assert.Single(playRequest.Slots); // 单位预打出的交互槽位（单位部署落位选择）
        Assert.True(playResponder.Complete(
            playRequest.RequestId,
            TargeterTestKit.Selection(playRequest.Slots[0].Name, playRequest.AllowedTargets[0])));
        Assert.Equal(PlayResultStatus.Success, (await playTask).Status);
        Assert.Contains(infantry, CommandTestKit.AllUnitsOf(match));
        Assert.Contains(GameUpdates.CardPlayed, recorder.Types);
        Assert.Contains(GameUpdates.UnitDeployed, recorder.Types);

        // ④ 回合循环：A → B → A（回到先手第二回合；指挥点结算至槽 2）
        await match.EndTurn();
        await match.EndTurn();
        Assert.Same(playerA, match.CurrentPlayer);
        Assert.Equal(3, match.TurnNumber);

        // ⑤ 准备巨炮（加入路径——不扣费）并激活；随后独立移动步兵、由巨炮一击 HQ 归零
        var striker = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.MegaId, 2);
        CommandTestKit.Activate(striker);

        CommandTestKit.Activate(infantry, canMove: true, canAttack: false);
        var moveTask = match.CommandManager.BeginMoveAsync(infantry);
        var (moveRequest, moveResponder) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(SelectorNames.FieldUnit, Assert.Single(moveRequest.Slots).Name);
        Assert.True(moveResponder.Complete(
            moveRequest.RequestId,
            TargeterTestKit.Selection(SelectorNames.FieldUnit, moveRequest.AllowedTargets[0])));
        Assert.Equal(CommandResultStatus.Success, (await moveTask).Status);
        Assert.Contains(GameUpdates.UnitPositionChanged, recorder.Types);

        var strikeTask = match.CommandManager.BeginAttackAsync(striker);
        var (strikeRequest, strikeResponder) = await bridge.WaitForNextBeginAsync();
        Assert.True(strikeResponder.Complete(
            strikeRequest.RequestId,
            TargeterTestKit.Selection(SelectorNames.FieldUnit, playerB.Hq.Ref)));
        Assert.Equal(CommandResultStatus.Success, (await strikeTask).Status);

        // ⑥ 终局：状态／相位／胜者／原因（HQ 归零＝与认输共用"置结束"单源路径）＋终局冻结
        Assert.Equal(MatchState.Ended, match.State);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Same(playerA, match.Winner);
        Assert.Equal(MatchEndReason.HqZero, match.EndReason);
        Assert.Equal(0, playerB.HqHealth);
        await Assert.ThrowsAsync<InvalidOperationException>(() => match.EndTurn());
        Assert.Equal(ConcedeFailureReason.GameEnded, match.Concede(playerB).FailureReason);
    }

    [Fact]
    public async Task Full_Match_Turn_Cycle_Then_Concede_Ends_Match()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge, skipMulligan: false);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];

        foreach (var player in match.Players)
        {
            Assert.Equal(MulliganResultStatus.Success, (await match.MulliganDone(player)).Status);
        }

        using var recorder = new UpdateRecorder(match.Engine);
        Assert.Same(playerA, match.CurrentPlayer);
        Assert.Equal(1, match.TurnNumber);

        // 回合循环：A → B → A（turn 信号成对出现；当前方与回合数随动）
        await match.EndTurn();
        Assert.Same(playerB, match.CurrentPlayer);
        Assert.Equal(2, match.TurnNumber);
        await match.EndTurn();
        Assert.Same(playerA, match.CurrentPlayer);
        Assert.Equal(3, match.TurnNumber);
        Assert.Equal(2, recorder.Types.Count(type => type == GameUpdates.TurnEnd));
        Assert.Equal(2, recorder.Types.Count(type => type == GameUpdates.TurnStart));

        // 认输终局
        Assert.True(match.Concede(playerB).IsAccepted);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Same(playerA, match.Winner);
        Assert.Equal(MatchEndReason.Concede, match.EndReason);
    }
}
