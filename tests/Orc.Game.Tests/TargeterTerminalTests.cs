using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// Targeter 终局与失败验收（④取消＋失败路径）：取消＝失败返回不抛、队列继续；
/// 配对校验（请求标识不匹配＝拒绝＋留痕＋继续等待）；时序违规（终局后调用）＝幂等忽略＋留痕＋状态不破坏；
/// 统一失败模式（收集失败/交互异常/未装配）＝失败结局、不抛、队列继续、留痕可观测。
/// </summary>
public class TargeterTerminalTests
{
    [Fact]
    public async Task Cancel_Returns_Cancelled_Not_Throws_And_Queue_Continues()
    {
        var e1 = new Entity("E1");
        var beginCount = 0;
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };

        // 第 1 个手动取消（脚本第 1 次不动作）；第 2 个（排队中）自动完成
        bridge.InteractionScript = (description, responder) =>
        {
            beginCount++;
            if (beginCount >= 2)
            {
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[0]));
            }
        };

        var manager = new TargeterManager(bridge);
        var targeter1 = manager.CreateTargeter();
        var targeter2 = manager.CreateTargeter();

        var task1 = targeter1.Targeting();
        var task2 = targeter2.Targeting(); // 排队中

        var (description1, responder1) = await bridge.WaitForNextBeginAsync();
        Assert.Single(bridge.Begins);
        Assert.False(task2.IsCompleted);

        // 取消：失败返回不抛（结果对象表达取消、携带原因）
        Assert.True(responder1.Cancel(description1.RequestId));
        var result1 = await task1;
        Assert.Equal(TargetingStatus.Cancelled, result1.Status);
        Assert.Equal(TargetingEndReason.PlayerCancelled, result1.Reason);
        Assert.Null(result1.Outcome);

        // 队列继续：排队中的第 2 条出队并完整走完
        var result2 = await task2;
        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Same(e1.Ref, result2.Outcome!.Single);
    }

    [Fact]
    public async Task Cancel_With_Wrong_Request_Id_Is_Rejected_Then_Valid_Cancel_Succeeds()
    {
        var e1 = new Entity("E1");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var task = manager.CreateTargeter().Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 标识不匹配＝违规处理：拒绝＋留痕＋继续等待（不构成终局）
        Assert.False(responder.Cancel("wrong-request-id"));
        Assert.False(task.IsCompleted);
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:RequestIdMismatch"));

        // 合法取消照常构成终局
        Assert.True(responder.Cancel(description.RequestId));
        var result = await task;
        Assert.Equal(TargetingStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Complete_With_Wrong_Request_Id_Is_Rejected_Then_Valid_Complete_Succeeds()
    {
        var e1 = new Entity("E1");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var task = manager.CreateTargeter().Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        Assert.False(responder.Complete("wrong-request-id", TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        Assert.False(task.IsCompleted);
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:RequestIdMismatch"));

        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        var result = await task;
        Assert.Same(e1.Ref, result.Outcome!.Single);
    }

    [Fact]
    public async Task Late_Complete_After_Success_Is_Idempotently_Ignored_And_State_Unbroken()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var beginCount = 0;
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = (description, responder) =>
            {
                beginCount++;
                if (beginCount >= 2)
                {
                    responder.Complete(
                        description.RequestId,
                        TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[0]));
                }
            },
        };
        var manager = new TargeterManager(bridge, trace);

        var task1 = manager.CreateTargeter().Targeting();
        var (description1, responder1) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder1.Complete(description1.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e2.Ref)));
        var result1 = await task1;
        Assert.Same(e2.Ref, result1.Outcome!.Single);

        // 终局后调用（重复 Complete / Cancel）＝幂等忽略＋留痕；底線＝不破坏队列与后续请求状态
        Assert.False(responder1.Complete(description1.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        Assert.False(responder1.Cancel(description1.RequestId));
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Terminal"));

        // 结果对象不被污染（仍为原成功产出）
        Assert.Equal(TargetingStatus.Success, result1.Status);
        Assert.Same(e2.Ref, result1.Outcome!.Single);

        // 后续请求照常（队列未破坏）
        var result2 = await manager.CreateTargeter().Targeting();
        Assert.Equal(TargetingStatus.Success, result2.Status);
    }

    [Fact]
    public async Task Complete_After_Cancel_Is_Idempotently_Ignored()
    {
        var e1 = new Entity("E1");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var task = manager.CreateTargeter().Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        Assert.True(responder.Cancel(description.RequestId));
        // 取消后再 Complete：幂等忽略（不构成新终局）
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Terminal"));

        var result = await task;
        Assert.Equal(TargetingStatus.Cancelled, result.Status);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public async Task Candidate_Collection_Failure_Fails_Without_Throwing_And_Queue_Continues()
    {
        var e1 = new Entity("E1");
        var calls = 0;
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("收集爆炸");
                }

                return Task.FromResult(TargeterTestKit.Candidates(e1.Ref));
            },
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge, trace);

        var failed = await manager.CreateTargeter().Targeting();

        Assert.Equal(TargetingStatus.Failed, failed.Status);
        Assert.Equal(TargetingEndReason.CandidateCollectionFailed, failed.Reason);
        Assert.Null(failed.Outcome);
        Assert.Empty(bridge.Begins);
        Assert.True(TargeterTestKit.HasKeyword(trace, "reason:CandidateCollectionFailed"));

        // 队列继续
        var ok = await manager.CreateTargeter().Targeting();
        Assert.Equal(TargetingStatus.Success, ok.Status);
    }

    [Fact]
    public async Task Interaction_Exception_Fails_Without_Throwing_And_Queue_Continues()
    {
        var e1 = new Entity("E1");
        var calls = 0;
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        bridge.InteractionScript = (description, responder) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("交互爆炸");
            }

            responder.Complete(
                description.RequestId,
                TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[0]));
        };
        var manager = new TargeterManager(bridge, trace);

        var failed = await manager.CreateTargeter().Targeting();

        Assert.Equal(TargetingStatus.Failed, failed.Status);
        Assert.Equal(TargetingEndReason.InteractionFault, failed.Reason);
        Assert.True(TargeterTestKit.HasKeyword(trace, "reason:InteractionFault"));

        var ok = await manager.CreateTargeter().Targeting();
        Assert.Equal(TargetingStatus.Success, ok.Status);
    }

    [Fact]
    public async Task Manager_Without_Bridge_Fails_BridgeNotAssembled_Without_Throwing()
    {
        var trace = new InMemoryTargetingTrace();
        var manager = new TargeterManager(bridge: null, traceSink: trace);

        // 允许无桥接装配；Targeting 被调用时以失败结局暴露（不抛）＋留痕
        var result = await manager.CreateTargeter().Targeting();

        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.BridgeNotAssembled, result.Reason);
        Assert.Null(result.Outcome);
        Assert.True(TargeterTestKit.HasKeyword(trace, "reason:BridgeNotAssembled"));
    }

    [Fact]
    public async Task Independent_Construction_With_Injected_Trace_Sink_Receives_Violation_Traces()
    {
        var e1 = new Entity("E1");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        // 独立构造：注入留痕目标（注入优先）
        var manager = new TargeterManager(bridge, trace);

        var task = manager.CreateTargeter().Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName)));
        Assert.True(trace.Entries.Count > 0);
        Assert.All(trace.Entries, entry => Assert.Equal("targeting", entry.Source));

        Assert.True(responder.Cancel(description.RequestId));
        await task;
    }
}
