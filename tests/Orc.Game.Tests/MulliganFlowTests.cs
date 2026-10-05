using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Managers;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 01 完整流程（A1）换牌相位与终局单源：Initialize 置"换牌"、**特制槽位**换牌（M1=b：Kind/呈现＝MulliganSelect）、
/// 双方确认进对局（先手第 1 回合恰一次）、换牌静默口径、相位门禁（换牌期间动作入口一律拒绝）、
/// 认输（与 HQ≤0 共用"置结束"单源路径＋终局原因）。
/// </summary>
public class MulliganFlowTests
{
    private static async Task<Match> CreateMulliganMatchAsync(MockTargeterBridge bridge)
        => await InitializeAsync(CommandTestKit.CreateCommandMatch(bridge, skipMulligan: false));

    private static async Task<Match> InitializeAsync(Match match)
    {
        await match.Initialize();
        return match;
    }

    [Fact]
    public async Task Initialize_Enters_Mulligan_Phase()
    {
        var match = await CreateMulliganMatchAsync(new MockTargeterBridge());

        Assert.Equal(MatchState.Mulligan, match.State);
        Assert.Equal(MatchPhase.Mulligan, match.Phase);
        Assert.False(match.MulliganManager.AllConfirmed);
        Assert.Null(match.Winner);
        Assert.Null(match.EndReason);
    }

    [Fact]
    public async Task BeginMulliganAsync_Uses_Special_Slot_And_Replaces_Selected_Cards()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        var player = match.Players[0];
        var handBefore = player.Hand.ToArray();
        using var recorder = new UpdateRecorder(match.Engine);

        var task = match.BeginMulliganAsync(player);
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 特制槽位（M1=b）：专用槽位名／Kind／呈现标注（前端据此播放专属动画）／槽位参数＝该玩家
        var slot = Assert.Single(description.Slots);
        Assert.Equal(MulliganManager.SlotName, slot.Name);
        Assert.Equal(TargetSlotKind.MulliganSelect, slot.Kind);
        Assert.Equal(TargetSlotPresentation.MulliganSelect, slot.Presentation);
        Assert.Equal(0, slot.Min);
        Assert.Equal(handBefore.Length, slot.Max);
        Assert.True(slot.HasParameter);
        Assert.Same(player, slot.Parameter);
        // 候选＝该方手牌快照（新引用类——不经前端收集通道）
        Assert.Empty(bridge.CollectCalls);
        Assert.Equal(handBefore.Length, slot.AllowedReferences!.Count);

        Assert.True(responder.Complete(
            description.RequestId,
            TargeterTestKit.Selection(MulliganManager.SlotName, handBefore[0].Ref, handBefore[1].Ref)));
        var result = await task;

        // 换牌：手牌数不变、被换两张已不在手牌；洗切恰好一条 deck.shuffled、无 card.drawn / card.hand.add（静默）
        Assert.Equal(MulliganResultStatus.Success, result.Status);
        Assert.True(match.MulliganManager.IsConfirmed(player));
        Assert.Equal(handBefore.Length, player.Hand.Count);
        Assert.DoesNotContain(handBefore[0], player.Hand);
        Assert.DoesNotContain(handBefore[1], player.Hand);
        Assert.Equal(new[] { GameUpdates.DeckShuffled }, recorder.Types);
    }

    [Fact]
    public async Task BeginMulliganAsync_Empty_Selection_Is_No_Replace_But_Confirms()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        var player = match.Players[0];
        var handBefore = player.Hand.ToArray();
        using var recorder = new UpdateRecorder(match.Engine);

        var task = match.BeginMulliganAsync(player);
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder.Complete(
            description.RequestId, TargeterTestKit.Selection(MulliganManager.SlotName)));
        var result = await task;

        // 空选＝不换牌（零洗切、零信号）但构成确认
        Assert.Equal(MulliganResultStatus.Success, result.Status);
        Assert.True(match.MulliganManager.IsConfirmed(player));
        Assert.Equal(handBefore, player.Hand.ToArray());
        Assert.Empty(recorder.Types);
    }

    [Fact]
    public async Task BeginMulliganAsync_Cancel_Leaves_Zero_Side_Effect()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        var player = match.Players[0];
        var handBefore = player.Hand.ToArray();
        using var recorder = new UpdateRecorder(match.Engine);

        var task = match.BeginMulliganAsync(player);
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder.Cancel(description.RequestId));
        var result = await task;

        // 取消＝零副作用（不换、不确认；可重发或改调确认入口）
        Assert.Equal(MulliganResultStatus.Cancelled, result.Status);
        Assert.False(match.MulliganManager.IsConfirmed(player));
        Assert.Equal(handBefore, player.Hand.ToArray());
        Assert.Empty(recorder.Types);
    }

    [Fact]
    public async Task Both_Confirmed_Enters_Play_And_Starts_First_Turn_Exactly_Once()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        using var recorder = new UpdateRecorder(match.Engine);

        foreach (var player in match.Players)
        {
            var result = await match.MulliganDone(player);
            Assert.Equal(MulliganResultStatus.Success, result.Status);
        }

        Assert.True(match.MulliganManager.AllConfirmed);
        Assert.Equal(MatchState.InProgress, match.State);
        Assert.Equal(MatchPhase.Play, match.Phase);
        Assert.Equal(
            new[] { GameUpdates.TurnStartBefore, GameUpdates.TurnStart, GameUpdates.TurnStartAfter },
            recorder.Types.Where(type => type.StartsWith("turn.", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public async Task Confirmed_Player_Cannot_Replace_Again()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        var player = match.Players[0];

        Assert.Equal(MulliganResultStatus.Success, (await match.MulliganDone(player)).Status);

        var again = await match.MulliganDone(player);
        Assert.Equal(MulliganResultStatus.Failed, again.Status);
        Assert.Equal(MulliganFailureReason.AlreadyConfirmed, again.FailureReason);

        var replace = await match.BeginMulliganAsync(player);
        Assert.Equal(MulliganFailureReason.AlreadyConfirmed, replace.FailureReason);
        Assert.Empty(bridge.Begins);
    }

    [Fact]
    public async Task Action_Entrances_Are_Rejected_During_Mulligan()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        var player = match.Players[0];
        var card = (UnitCard)match.CardLibrary.Instantiate(CommandTestKit.InfantryId);
        await card.LoadAsync(player);
        player.Hand.Add(card);

        // ① 出牌（统一预行为入口）：相位拒绝、零副作用、不发起交互
        var play = await match.PlayManager.BeginUnitPrePlayAsync(card);
        Assert.Equal(PlayResultStatus.Failed, play.Status);
        Assert.Equal(PlayFailureReason.PhaseBlocked, play.FailureReason);

        // ② 结束回合：拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(() => match.EndTurn());

        // ③ 认输：换牌相位拒绝
        var concede = match.Concede(player);
        Assert.Equal(ConcedeStatus.Rejected, concede.Status);
        Assert.Equal(ConcedeFailureReason.PhaseBlocked, concede.FailureReason);

        Assert.Empty(bridge.Begins);
        Assert.Equal(MatchState.Mulligan, match.State);
    }

    [Fact]
    public async Task Concede_Ends_Match_With_Opponent_Winner_And_Reason()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        await match.MulliganDone(playerA);
        await match.MulliganDone(playerB);
        Assert.Equal(MatchPhase.Play, match.Phase);

        var result = match.Concede(playerA);

        // 认输＝与 HQ≤0 共用"置结束"单源路径；终局记录＝对手为胜者＋原因＝认输
        Assert.True(result.IsAccepted);
        Assert.Equal(MatchState.Ended, match.State);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Same(playerB, match.Winner);
        Assert.Equal(MatchEndReason.Concede, match.EndReason);

        // 终局后：重复认输拒绝（终局类别）、回合推进拒绝
        Assert.Equal(ConcedeFailureReason.GameEnded, match.Concede(playerB).FailureReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => match.EndTurn());
    }

    [Fact]
    public async Task Mulligan_Entrances_Are_Rejected_After_End()
    {
        var bridge = new MockTargeterBridge();
        var match = await CreateMulliganMatchAsync(bridge);
        foreach (var player in match.Players)
        {
            await match.MulliganDone(player);
        }

        Assert.True(match.Concede(match.Players[0]).IsAccepted);

        var afterEnd = await match.MulliganDone(match.Players[1]);
        Assert.Equal(MulliganFailureReason.GameEnded, afterEnd.FailureReason);
    }
}
