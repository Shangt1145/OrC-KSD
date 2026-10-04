using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2B 验收⑥（反制）：单入口状态翻转（激活＝扣点＋置激活＋handler 注册；取消＝无条件退点＋取消激活＋注销）；
/// 激活点数不足＝拒绝（不改变状态、不扣点、不注册、不发更新）；仅己方回合（非己方回合拒绝；正控＝己方回合成功）；
/// 不发任何游戏更新。
/// </summary>
public class PlayChainCounterTests
{
    [Fact]
    public async Task Counter_Activation_Cycle_With_Fee_And_Registration()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0]; // 回合 1 先手（点数 1）
        var counter = await PlayChainTestKit.InstantiateLoadedAsync<CounterCard>(match, player, PlayChainTestKit.CounterCheapId);
        counter.AddEffectHandler("效果甲", (view, ctx, ct) => Task.CompletedTask);
        counter.AddEffectHandler("效果乙", (view, ctx, ct) => Task.CompletedTask);
        using var recorder = new UpdateRecorder(match.Engine);

        // 激活：扣点 → 置激活 ＋ 注册（效果）handler。
        var activate = await match.PlayManager.UseCounterAsync(counter);
        Assert.Equal(PlayResultStatus.Success, activate.Status);
        Assert.True(counter.GetData<CounterActivationData>().IsActive);
        Assert.Equal(0, player.Points);
        Assert.Equal(new[] { "效果甲", "效果乙" }, counter.RegisteredEffectHandlerNames);
        Assert.Empty(recorder.Updates); // 使用反制不发任何游戏更新

        // 取消：无条件退点（同额）→ 取消激活 ＋ 取消注册。
        var deactivate = await match.PlayManager.UseCounterAsync(counter);
        Assert.Equal(PlayResultStatus.Success, deactivate.Status);
        Assert.False(counter.GetData<CounterActivationData>().IsActive);
        Assert.Equal(1, player.Points);
        Assert.Empty(counter.RegisteredEffectHandlerNames);
        Assert.Empty(recorder.Updates);

        // 再激活：重新注册。
        var reactivate = await match.PlayManager.UseCounterAsync(counter);
        Assert.Equal(PlayResultStatus.Success, reactivate.Status);
        Assert.True(counter.GetData<CounterActivationData>().IsActive);
        Assert.Equal(0, player.Points);
        Assert.Equal(new[] { "效果甲", "效果乙" }, counter.RegisteredEffectHandlerNames);
    }

    [Fact]
    public async Task Counter_Activation_Rejected_When_Not_Enough_Points()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0]; // 点数 1
        var counter = await PlayChainTestKit.InstantiateLoadedAsync<CounterCard>(match, player, PlayChainTestKit.CounterCostlyId); // 花费 3
        counter.AddEffectHandler("效果", (view, ctx, ct) => Task.CompletedTask);
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await match.PlayManager.UseCounterAsync(counter);

        // 激活点数不足＝拒绝：不改变状态、不扣点、不注册、不发游戏更新；以明确失败结果可观测。
        Assert.Equal(PlayResultStatus.Failed, result.Status);
        Assert.Equal(PlayFailureReason.CounterPointShortage, result.FailureReason);
        Assert.False(counter.GetData<CounterActivationData>().IsActive);
        Assert.Equal(1, player.Points);
        Assert.Empty(counter.RegisteredEffectHandlerNames);
        Assert.Empty(recorder.Updates);
    }

    [Fact]
    public async Task Counter_Use_Requires_Owner_Turn()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var counterA = await PlayChainTestKit.InstantiateLoadedAsync<CounterCard>(match, playerA, PlayChainTestKit.CounterCheapId);

        await match.EndTurn(); // → 回合 2：玩家B 行动（B 结算后点数 1；A 点数保留——X3：回合结束不清零）
        Assert.Same(playerB, match.CurrentPlayer);
        var counterB = await PlayChainTestKit.InstantiateLoadedAsync<CounterCard>(match, playerB, PlayChainTestKit.CounterCheapId);

        // 非己方回合（A 的反制、当前行动方＝B）＝拒绝：状态/点数均不变。
        var rejected = await match.PlayManager.UseCounterAsync(counterA);
        Assert.Equal(PlayResultStatus.Failed, rejected.Status);
        Assert.Equal(PlayFailureReason.CounterNotOwnerTurn, rejected.FailureReason);
        Assert.False(counterA.GetData<CounterActivationData>().IsActive);
        Assert.Equal(1, playerA.Points); // A 的 1 点保留（X3）；拒绝零副作用（未扣点）

        // 正控：己方回合（B 的反制、当前行动方＝B）＝成功。
        var accepted = await match.PlayManager.UseCounterAsync(counterB);
        Assert.Equal(PlayResultStatus.Success, accepted.Status);
        Assert.True(counterB.GetData<CounterActivationData>().IsActive);
        Assert.Equal(0, playerB.Points);
    }
}
