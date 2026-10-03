using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// Targeter 队列验收（①排队）：FIFO 串行——一次只执行一个 targeting；并发请求（含跨实例）统一排队、不拒绝；
/// 完成/取消后出队下一条；多 Manager 实例互不干扰；复用与结果独立；候选收集发生在出队执行时（新鲜度、先于 Begin）。
/// </summary>
public class TargeterQueueTests
{
    [Fact]
    public async Task Concurrent_Requests_Are_Serialized_In_Fifo_Order_Across_Targeter_Instances()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref)),
        };
        var manager = new TargeterManager(bridge);

        var taskA = manager.CreateTargeter().Targeting();
        var taskB = manager.CreateTargeter().Targeting();
        var taskC = manager.CreateTargeter().Targeting();

        // 仅第一个请求进入交互；其余排队中（不拒绝）、尚未收集（执行时收集）
        var (beginA, responderA) = await bridge.WaitForNextBeginAsync();
        Assert.Single(bridge.Begins);
        Assert.Single(bridge.CollectCalls);
        Assert.False(taskA.IsCompleted);
        Assert.False(taskB.IsCompleted);
        Assert.False(taskC.IsCompleted);

        Assert.True(responderA.Complete(beginA.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(beginA), e1.Ref)));
        var resultA = await taskA;
        Assert.Equal(TargetingStatus.Success, resultA.Status);
        Assert.Same(e1.Ref, resultA.Outcome!.Single);

        // 第一个终局后出队下一条：B 开始（收集＋Begin），C 仍排队
        var (beginB, responderB) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(2, bridge.Begins.Count);
        Assert.Equal(2, bridge.CollectCalls.Count);
        Assert.True(
            IndexOfEvent(bridge, $"collect:{beginB.RequestId}") < IndexOfEvent(bridge, $"begin:{beginB.RequestId}"),
            "收集须先于 Begin（先收集、后交互）。");
        Assert.False(taskC.IsCompleted);

        Assert.True(responderB.Complete(beginB.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(beginB), e2.Ref)));
        var resultB = await taskB;
        Assert.Same(e2.Ref, resultB.Outcome!.Single);
        Assert.False(taskC.IsCompleted);

        var (beginC, responderC) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(3, bridge.Begins.Count);
        Assert.True(responderC.Complete(beginC.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(beginC), e3.Ref)));
        var resultC = await taskC;
        Assert.Same(e3.Ref, resultC.Outcome!.Single);

        // FIFO：收集顺序＝发起顺序（跨实例统一排队；requestId 一一对应）
        Assert.Equal(beginA.RequestId, bridge.CollectCalls[0].RequestId);
        Assert.Equal(beginB.RequestId, bridge.CollectCalls[1].RequestId);
        Assert.Equal(beginC.RequestId, bridge.CollectCalls[2].RequestId);
    }

    [Fact]
    public async Task Same_Targeter_Concurrent_Requests_Queue_And_Results_Are_Independent()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter();

        // 同一请求对象未完成时再发＝排队（不拒绝）；各自独立结果
        var task1 = targeter.Targeting();
        var task2 = targeter.Targeting();

        var (begin1, responder1) = await bridge.WaitForNextBeginAsync();
        Assert.Single(bridge.Begins);
        Assert.False(task2.IsCompleted);

        Assert.True(responder1.Complete(begin1.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(begin1), e1.Ref)));
        var result1 = await task1;

        var (begin2, responder2) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder2.Complete(begin2.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(begin2), e2.Ref)));
        var result2 = await task2;

        Assert.Same(e1.Ref, result1.Outcome!.Single);
        Assert.Same(e2.Ref, result2.Outcome!.Single);
        Assert.NotSame(result1, result2);
    }

    [Fact]
    public async Task Separate_Managers_Do_Not_Interfere_Each_With_Own_Queue_And_Bridge()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");

        // Manager1：挂起中的请求（手动驱动）
        var bridge1 = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager1 = new TargeterManager(bridge1);

        // Manager2：自动完成（独立桥接）
        var bridge2 = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e2.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager2 = new TargeterManager(bridge2);

        var task1 = manager1.CreateTargeter().Targeting();
        var (begin1, responder1) = await bridge1.WaitForNextBeginAsync();
        Assert.False(task1.IsCompleted);

        // Manager2 不受 Manager1 的挂起影响：独立队列、独立桥接、立即完整完成
        var result2 = await manager2.CreateTargeter().Targeting();
        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Same(e2.Ref, result2.Outcome!.Single);
        Assert.Single(bridge2.Begins);
        Assert.Empty(bridge1.Begins.Skip(1)); // bridge1 仍只有那一次 Begin

        // 完成 Manager1 的请求：互不干扰、各自结局
        Assert.True(responder1.Complete(begin1.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(begin1), e1.Ref)));
        var result1 = await task1;
        Assert.Equal(TargetingStatus.Success, result1.Status);
        Assert.Same(e1.Ref, result1.Outcome!.Single);
    }

    [Fact]
    public async Task Targeter_Reuse_Sequential_Executions_Are_Independent()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var pickIndex = 0;
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = (description, responder) =>
            {
                var pick = description.AllowedTargets[pickIndex % 2];
                pickIndex++;
                responder.Complete(description.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), pick));
            },
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter();

        // 串行复用：两次独立执行、独立结果（请求对象无跨执行状态残留）
        var result1 = await targeter.Targeting();
        var result2 = await targeter.Targeting();

        Assert.Equal(TargetingStatus.Success, result1.Status);
        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Same(e1.Ref, result1.Outcome!.Single);
        Assert.Same(e2.Ref, result2.Outcome!.Single);
        Assert.Equal(2, bridge.Begins.Count);
        Assert.Equal(2, bridge.CollectCalls.Count);
    }

    [Fact]
    public async Task Candidate_Collection_Happens_At_Execution_Time_Fresh_And_Once_Per_Request()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var collectCount = 0;
        var bridge = new MockTargeterBridge
        {
            // 每次收集返回"当时"的候选：第 1 次 [E1]，第 2 次 [E1, E2]
            CollectScript = _ => Task.FromResult(
                collectCount++ == 0
                    ? TargeterTestKit.Candidates(e1.Ref)
                    : TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
        };
        var manager = new TargeterManager(bridge);

        var task1 = manager.CreateTargeter().Targeting();
        var task2 = manager.CreateTargeter().Targeting();

        // 请求 1 执行期：恰调用一次收集；请求 2 尚未收集（若在入队前/构造期收集则此处为 2）
        var (begin1, responder1) = await bridge.WaitForNextBeginAsync();
        Assert.Single(bridge.CollectCalls);
        Assert.Single(begin1.AllowedTargets);
        Assert.True(responder1.Complete(begin1.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(begin1), e1.Ref)));
        await task1;

        // 请求 2 出队执行时收集（新鲜数据生效）：恰第 2 次收集、收集先于 Begin
        var (begin2, responder2) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(2, bridge.CollectCalls.Count);
        Assert.Equal(2, begin2.AllowedTargets.Count);
        Assert.True(IndexOfEvent(bridge, $"collect:{begin2.RequestId}") < IndexOfEvent(bridge, $"begin:{begin2.RequestId}"));

        Assert.True(responder2.Complete(begin2.RequestId, TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(begin2), e2.Ref)));
        var result2 = await task2;
        Assert.Same(e2.Ref, result2.Outcome!.Single);
    }

    private static int IndexOfEvent(MockTargeterBridge bridge, string entry)
        => bridge.Events.ToList().IndexOf(entry);
}
