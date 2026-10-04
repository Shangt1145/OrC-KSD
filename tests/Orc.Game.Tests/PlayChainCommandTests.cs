using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2B 验收⑤②（指令）：默认空 handler 集照常打出（card.played/扣费/离手）、
/// 预打出捕获→object? 参数传递链（单引用/列表承载演练）、targeter 形态演练（传入→识别→执行产出引用并可被消费）、
/// 主动 handler 集（登记序、card.played 先于 handler、收尾在 handler 之后、异常隔离）、
/// 预打出取消（截断打出）、预打出拒绝（点数不足；handler 不执行）、复验失败（点数漂移；零副作用）。
/// </summary>
public class PlayChainCommandTests
{
    [Fact]
    public async Task Command_Plays_With_Default_Empty_Handler_Set()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await match.PlayManager.PlayCommandAsync(command);

        // 默认空集不改变流程：照发 card.played（恰一次）、照扣费（收尾）、离手。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Equal(new[] { GameUpdates.CardPlayed }, recorder.Types);
        Assert.Equal(0, player.Points);
        Assert.DoesNotContain(command, player.Hand);
    }

    [Fact]
    public async Task Command_Captures_Single_Reference_And_Passes_To_Active_Handlers()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var targetEntity = new Entity("被捕获引用");
        using var recorder = new UpdateRecorder(match.Engine);
        Ref<Entity>? observed = null;
        var playedSeenInHandler = false;
        int? pointsAtHandler = null;

        command.AddPrePlayHandler("捕获单引用", (view, ctx, ct) =>
        {
            view.CaptureBox!.Capture(targetEntity.Ref);
            return Task.CompletedTask;
        });
        command.AddActiveHandler("消费捕获", (view, ctx, ct) =>
        {
            observed = view.Argument as Ref<Entity>;
            playedSeenInHandler = recorder.Types.Contains(GameUpdates.CardPlayed);
            pointsAtHandler = player.Points;
            return Task.CompletedTask;
        });

        var result = await match.PlayManager.PlayCommandAsync(command);

        // 传递链可断言：打出触发器实际收到的 object? 参数及其承载引用＝预打出捕获结果。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Same(targetEntity.Ref, observed);
        // 固定顺序：card.played 先于主动 handler 集；收尾（扣费）在 handler 之后。
        Assert.True(playedSeenInHandler);
        Assert.Equal(1, pointsAtHandler);
        Assert.Equal(0, player.Points);
    }

    [Fact]
    public async Task Command_Captures_Multiple_Values_As_List_And_Passes_To_Active_Handlers()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var first = new Entity("甲");
        var second = new Entity("乙");
        object? observed = null;

        command.AddPrePlayHandler("捕获两个", (view, ctx, ct) =>
        {
            view.CaptureBox!.Capture(first.Ref);
            view.CaptureBox.Capture(second.Ref);
            return Task.CompletedTask;
        });
        command.AddActiveHandler("观察", (view, ctx, ct) =>
        {
            observed = view.Argument;
            return Task.CompletedTask;
        });

        var result = await match.PlayManager.PlayCommandAsync(command);

        // 多值捕获＝列表承载（object?[]；提交序）。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        var list = Assert.IsType<object?[]>(observed);
        Assert.Equal(new object?[] { first.Ref, second.Ref }, list);
    }

    [Fact]
    public async Task Command_Targeter_Request_Passed_And_Executed_In_Play_Phase()
    {
        var bridge = new MockTargeterBridge();
        var match = PlayChainTestKit.CreatePlayMatch(bridge);
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var payload = new Entity("延迟选择的产出");
        bridge.CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(payload.Ref));
        bridge.InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed();

        // 预打出阶段捕获「targeter 请求对象」本体（延迟选择；经捕获箱提交）；打出阶段识别并执行（最小闭环：执行产出引用并可被消费）。
        var targeter = match.TargeterManager.CreateTargeter();
        command.AddPrePlayHandler("捕获请求", (view, ctx, ct) =>
        {
            view.CaptureBox!.Capture(targeter);
            return Task.CompletedTask;
        });
        var identifiedAsTargeter = false;
        Ref<Entity>? consumed = null;
        command.AddActiveHandler("执行请求", async (view, ctx, ct) =>
        {
            if (view.Argument is Targeter request)
            {
                identifiedAsTargeter = true;
                var targeting = await request.Targeting();
                consumed = targeting.Outcome?.Single;
            }
        });

        var result = await match.PlayManager.PlayCommandAsync(command);

        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.True(identifiedAsTargeter);
        Assert.Same(payload.Ref, consumed);
    }

    [Fact]
    public async Task Command_Active_Handlers_Trigger_In_Registration_Order()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var order = new List<string>();

        command.AddActiveHandler("A", (view, ctx, ct) => { order.Add("A"); return Task.CompletedTask; });
        command.AddActiveHandler("B", (view, ctx, ct) => { order.Add("B"); return Task.CompletedTask; });
        command.AddActiveHandler("C", (view, ctx, ct) => { order.Add("C"); return Task.CompletedTask; });

        var result = await match.PlayManager.PlayCommandAsync(command);

        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Equal(new[] { "A", "B", "C" }, order);
    }

    [Fact]
    public async Task Command_Active_Handler_Exception_Isolated_And_Play_Completes()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var order = new List<string>();

        command.AddActiveHandler("A", (view, ctx, ct) => { order.Add("A"); return Task.CompletedTask; });
        command.AddActiveHandler("B", (view, ctx, ct) => throw new InvalidOperationException("handler 爆炸"));
        command.AddActiveHandler("C", (view, ctx, ct) => { order.Add("C"); return Task.CompletedTask; });

        var result = await match.PlayManager.PlayCommandAsync(command);

        // 异常＝沿用引擎异常隔离（记录并继续）：不阻断后续 handler 与收尾；打出照常完成。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Equal(new[] { "A", "C" }, order);
        Assert.Equal(0, player.Points);
        Assert.DoesNotContain(command, player.Hand);
        Assert.Contains(
            match.Engine.RootStream.Entries,
            entry => entry.Source.Contains("打出触发器") && entry.Keywords.Contains("exception:InvalidOperationException"));
    }

    [Fact]
    public async Task Command_PrePlay_Handler_Exception_Isolated_And_Play_Continues()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var ran = new List<string>();
        command.AddPrePlayHandler("甲", (view, ctx, ct) => { ran.Add("甲"); return Task.CompletedTask; });
        command.AddPrePlayHandler("乙", (view, ctx, ct) => throw new InvalidOperationException("预打出 handler 爆炸"));
        command.AddPrePlayHandler("丙", (view, ctx, ct) =>
        {
            ran.Add("丙");
            view.CaptureBox!.Capture("丙的捕获");
            return Task.CompletedTask;
        });
        object? observed = null;
        command.AddActiveHandler("观察", (view, ctx, ct) => { observed = view.Argument; return Task.CompletedTask; });

        var result = await match.PlayManager.PlayCommandAsync(command);

        // 预打出 handler 异常＝沿用引擎异常隔离（记录并继续）：不阻断登记序后续 handler 与打出段。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Equal(new[] { "甲", "丙" }, ran);
        Assert.Equal("丙的捕获", observed);
        Assert.Equal(0, player.Points);
        Assert.Contains(
            match.Engine.RootStream.Entries,
            entry => entry.Source.Contains("预打出触发器") && entry.Keywords.Contains("exception:InvalidOperationException"));
    }

    [Fact]
    public async Task Command_PrePlay_Cancel_Prevents_Play()
    {
        var bridge = new MockTargeterBridge();
        var match = PlayChainTestKit.CreatePlayMatch(bridge);
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var anyEntity = new Entity("任意目标");
        bridge.CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(anyEntity.Ref));
        bridge.InteractionScript = (description, responder) => responder.Cancel(description.RequestId); // 交互取消
        using var recorder = new UpdateRecorder(match.Engine);

        // 预打出 handler 显式发起交互；交互被取消 ⇒ 请求取消预打出（预打出段失败/取消 ⇒ 打出不发生）。
        var targeter = match.TargeterManager.CreateTargeter();
        command.AddPrePlayHandler("交互取消即中止", async (view, ctx, ct) =>
        {
            var targeting = await targeter.Targeting();
            if (targeting.Status == TargetingStatus.Cancelled)
            {
                view.CaptureBox!.CancelPrePlay();
            }
        });

        var result = await match.PlayManager.PlayCommandAsync(command);

        Assert.Equal(PlayResultStatus.Cancelled, result.Status);
        Assert.DoesNotContain(GameUpdates.CardPlayed, recorder.Types); // 打出不发生
        Assert.Equal(1, player.Points); // 无扣费
        Assert.Contains(command, player.Hand); // 留手
    }

    [Fact]
    public async Task Command_PrePlay_Rejected_When_Not_Enough_Points()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCostlyId); // 花费 5
        var handlerRan = false;
        command.AddPrePlayHandler("不应执行", (view, ctx, ct) => { handlerRan = true; return Task.CompletedTask; });
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await match.PlayManager.PlayCommandAsync(command);

        // 指令预打出一并执行指挥点合法性检查（不足＝拒绝）；验证拒绝＝事件不执行、零更新、留手。
        Assert.Equal(PlayResultStatus.Failed, result.Status);
        Assert.Equal(PlayFailureReason.PrePlayPointShortage, result.FailureReason);
        Assert.False(handlerRan);
        Assert.Empty(recorder.Updates);
        Assert.Equal(1, player.Points);
        Assert.Contains(command, player.Hand);
    }

    [Fact]
    public async Task Command_Verification_Rejected_On_Point_Drift_Leaves_Zero_Side_Effects()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var command = await PlayChainTestKit.InstantiateLoadedAsync<CommandCard>(match, player, PlayChainTestKit.CommandCheapId);
        var spender = await PlayChainTestKit.InstantiateLoadedAsync<UnitCard>(match, player); // 预打出段内花点的单位
        var line = match.Battlefield.PlayerASupportLine;
        using var recorder = new UpdateRecorder(match.Engine);

        // 预打出段（handler 内）把唯一 1 点花掉 → 打出段复验：点数不足 ⇒ 打出链中止（仅本次取消＋留痕）。
        command.AddPrePlayHandler("预打出段内花点", async (view, ctx, ct) =>
        {
            var spent = await match.PlayManager.PlayUnitAsync(spender, line[1]);
            Assert.Equal(PlayResultStatus.Success, spent.Status);
        });

        var result = await match.PlayManager.PlayCommandAsync(command);

        Assert.Equal(PlayResultStatus.Failed, result.Status);
        Assert.Equal(PlayFailureReason.PlayVerificationRejected, result.FailureReason);
        // 零副作用：不触发主动 handler 集/不扣费/不发更新/不离手（本测试无 handler 集；更新仅来自 spender 的部署链）。
        Assert.Equal(new[] { GameUpdates.CardPlayed, GameUpdates.UnitDeployed }, recorder.Types);
        Assert.Equal(0, player.Points);
        Assert.Contains(command, player.Hand);
        Assert.Contains(
            match.Engine.RootStream.Entries,
            entry => entry.Keywords.Contains("validation:rejected"));
    }
}
