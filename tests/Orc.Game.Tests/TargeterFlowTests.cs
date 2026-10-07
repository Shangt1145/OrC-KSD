using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 新契约流程测试：逐个取选择器（拉取式）、语义事件提交、取消传播、非法选择重试（同一选择器重入）、
/// 多步流程、换牌 min=0 空选。
/// </summary>
public class TargeterFlowTests
{
    private static (TargeterManager Manager, MockTargeterBridge Bridge) Create()
    {
        var bridge = new MockTargeterBridge();
        return (new TargeterManager(bridge), bridge);
    }

    [Fact]
    public async Task Step_Produces_Selector_And_Pick_Completes()
    {
        var (manager, bridge) = Create();
        var e1 = new Entity("E1");
        Ref<Entity>? picked = null;

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.TargetPoint,
                new ReferenceSetParameter(new[] { e1.Ref }, 1, 1));

            if (step.IsOk)
            {
                picked = step.Value;
                return TargeterResult.Ok();
            }

            return step.IsCancelled
                ? TargeterResult.Cancelled()
                : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(SelectorNames.TargetPoint, description.Slots[0].Name);
        Assert.Equal(1, description.Slots[0].Min);

        Assert.True(responder.Complete(
            description.RequestId,
            TargeterTestKit.Selection(SelectorNames.TargetPoint, e1.Ref)));

        var result = await task;
        Assert.True(result.IsOk);
        Assert.Same(e1.Ref, picked);
    }

    [Fact]
    public async Task Cancel_Propagates_To_Cancelled_Result()
    {
        var (manager, bridge) = Create();
        var e1 = new Entity("E1");

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.TargetPoint,
                new ReferenceSetParameter(new[] { e1.Ref }, 1, 1));

            return step.IsCancelled
                ? TargeterResult.Cancelled()
                : TargeterResult.Ok();
        });

        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder.Cancel(description.RequestId));

        Assert.True((await task).IsCancelled);
    }

    [Fact]
    public async Task Invalid_Selection_Then_Retry_Reenters_Same_Selector()
    {
        var (manager, bridge) = Create();
        var allowed = new Entity("ALLOWED");
        var outside = new Entity("OUTSIDE");

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.TargetPoint,
                new ReferenceSetParameter(new[] { allowed.Ref }, 1, 1));

            if (step.IsFailed && step.Failure == SelectorFailureReason.InvalidSelection)
            {
                step = await flow.Retry<Ref<Entity>>();
            }

            return step.IsOk
                ? TargeterResult.Ok()
                : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var (first, firstResponder) = await bridge.WaitForNextBeginAsync();
        firstResponder.Complete(first.RequestId, TargeterTestKit.Selection(SelectorNames.TargetPoint, outside.Ref));

        var (second, secondResponder) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(first.RequestId, second.RequestId); // 同一选择器实例重入
        secondResponder.Complete(second.RequestId, TargeterTestKit.Selection(SelectorNames.TargetPoint, allowed.Ref));

        Assert.True((await task).IsOk);
    }

    [Fact]
    public async Task Multi_Step_Flow_Produces_Two_Selectors_In_Order()
    {
        var (manager, bridge) = Create();
        var slotTarget = new Entity("SLOT");
        var unitTarget = new Entity("UNIT");

        var task = manager.RunAsync(async flow =>
        {
            var first = await flow.Step(
                SelectorTemplates.Slot,
                new ReferenceSetParameter(new[] { slotTarget.Ref }, 1, 1));
            if (!first.IsOk)
            {
                return TargeterResult.FromSelectorFailure(first.Failure);
            }

            var second = await flow.Step(
                SelectorTemplates.FieldUnit,
                new ReferenceSetParameter(new[] { unitTarget.Ref }, 1, 1));
            return second.IsOk
                ? TargeterResult.Ok()
                : TargeterResult.FromSelectorFailure(second.Failure);
        });

        var (d1, r1) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(SelectorNames.Slot, d1.Slots[0].Name);
        r1.Complete(d1.RequestId, TargeterTestKit.Selection(SelectorNames.Slot, slotTarget.Ref));

        var (d2, r2) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(SelectorNames.FieldUnit, d2.Slots[0].Name);
        r2.Complete(d2.RequestId, TargeterTestKit.Selection(SelectorNames.FieldUnit, unitTarget.Ref));

        Assert.True((await task).IsOk);
    }

    [Fact]
    public async Task Cancel_On_Second_Step_Returns_Cancelled()
    {
        var (manager, bridge) = Create();
        var slotTarget = new Entity("SLOT");
        var unitTarget = new Entity("UNIT");

        var task = manager.RunAsync(async flow =>
        {
            var first = await flow.Step(
                SelectorTemplates.Slot,
                new ReferenceSetParameter(new[] { slotTarget.Ref }, 1, 1));
            if (!first.IsOk)
            {
                return TargeterResult.FromSelectorFailure(first.Failure);
            }

            var second = await flow.Step(
                SelectorTemplates.FieldUnit,
                new ReferenceSetParameter(new[] { unitTarget.Ref }, 1, 1));
            return second.IsCancelled
                ? TargeterResult.Cancelled() // 反悔撤销（第二步取消传播）
                : TargeterResult.Ok();
        });

        var (d1, r1) = await bridge.WaitForNextBeginAsync();
        r1.Complete(d1.RequestId, TargeterTestKit.Selection(SelectorNames.Slot, slotTarget.Ref));

        var (d2, r2) = await bridge.WaitForNextBeginAsync();
        r2.Cancel(d2.RequestId);

        Assert.True((await task).IsCancelled);
    }

    [Fact]
    public async Task Min_Zero_Empty_Pick_Is_Ok()
    {
        var (manager, bridge) = Create();
        var card = new Entity("CARD");
        IReadOnlyList<Ref<Entity>>? selected = null;

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.Mulligan,
                new ReferenceSetParameter(new[] { card.Ref }, min: 0, max: 1));

            if (!step.IsOk)
            {
                return TargeterResult.FromSelectorFailure(step.Failure);
            }

            selected = step.Value;
            return TargeterResult.Ok();
        });

        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(0, description.Slots[0].Min);

        // 空提交（不换牌）
        responder.Complete(description.RequestId, TargeterTestKit.Selection(SelectorNames.Mulligan));

        var result = await task;
        Assert.True(result.IsOk);
        Assert.NotNull(selected);
        Assert.Empty(selected!);
    }

    [Fact]
    public async Task Fifo_Serializes_Concurrent_Runs()
    {
        var (manager, bridge) = Create();
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");

        var first = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(SelectorTemplates.TargetPoint, new ReferenceSetParameter(new[] { e1.Ref }, 1, 1));
            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var second = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(SelectorTemplates.TargetPoint, new ReferenceSetParameter(new[] { e2.Ref }, 1, 1));
            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        // 只有第一个请求进入交互（FIFO 串行）
        var (d1, r1) = await bridge.WaitForNextBeginAsync();
        r1.Complete(d1.RequestId, TargeterTestKit.Selection(SelectorNames.TargetPoint, e1.Ref));
        Assert.True((await first).IsOk);

        var (d2, r2) = await bridge.WaitForNextBeginAsync();
        r2.Complete(d2.RequestId, TargeterTestKit.Selection(SelectorNames.TargetPoint, e2.Ref));
        Assert.True((await second).IsOk);
    }

    [Fact]
    public async Task Without_Bridge_Fails()
    {
        var manager = new TargeterManager(bridge: null);

        var result = await manager.RunAsync(_ => Task.FromResult(TargeterResult.Ok()));

        Assert.True(result.IsFailed);
        Assert.Equal(TargeterFailureReason.BridgeNotAssembled, result.Reason);
    }
}
