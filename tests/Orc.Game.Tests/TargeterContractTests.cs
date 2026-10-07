using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 新契约（真写法）覆盖：选择器族与产出类型、交互模式（点选/拖拽落空）、三态结果、
/// 重入与重试上限、FIFO 串行、多步流程与取消传播、未装配。
/// 说明：真写法＝直接 `RunAsync(flow)` + `NextAsync()` + `Submit(语义事件)`（不经 `LegacyTargetingShims` 便捷层）。
/// </summary>
public class TargeterContractTests
{
    private static (TargeterManager Manager, MockTargeterBridge Bridge) Create()
    {
        var bridge = new MockTargeterBridge();
        return (new TargeterManager(bridge), bridge);
    }

    // ---------- ① 选择器族与产出类型 ----------

    [Fact]
    public async Task Single_Reference_Selector_Produces_Reference()
    {
        var (manager, bridge) = Create();
        var target = new Entity("T");
        Ref<Entity>? produced = null;

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.TargetPoint,
                new ReferenceSetParameter(new[] { target.Ref }, 1, 1));
            produced = step.Value;
            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var selector = await bridge.WaitForNextSelectorAsync();
        Assert.Equal(SelectorNames.TargetPoint, selector.SelectorName);
        Assert.Equal(SelectorInteractionMode.Click, selector.Mode);

        Assert.True(selector.Submit(new PickEvent(references: new[] { target.Ref })));
        Assert.True((await task).IsOk);
        Assert.Same(target.Ref, produced);
    }

    [Fact]
    public async Task Option_Selector_Produces_Selected_Identifier()
    {
        var (manager, bridge) = Create();
        string? produced = null;

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.Option,
                new IdentifierSetParameter(new[] { "a", "b" }, 1, 1));
            produced = step.Value;
            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var selector = await bridge.WaitForNextSelectorAsync();
        Assert.Equal(SelectorNames.Option, selector.SelectorName);
        Assert.Equal(new[] { "a", "b" }, selector.Presentation.Identifiers);

        Assert.True(selector.Submit(new PickEvent(identifiers: new[] { "b" })));
        Assert.True((await task).IsOk);
        Assert.Equal("b", produced);
    }

    [Fact]
    public async Task Multi_Reference_Selector_Produces_List()
    {
        var (manager, bridge) = Create();
        var a = new Entity("A");
        var b = new Entity("B");
        IReadOnlyList<Ref<Entity>>? produced = null;

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.Hand,
                new ReferenceSetParameter(new[] { a.Ref, b.Ref }, min: 1, max: 2));
            produced = step.Value;
            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var selector = await bridge.WaitForNextSelectorAsync();
        Assert.Equal(SelectorNames.Hand, selector.SelectorName);

        Assert.True(selector.Submit(new PickEvent(references: new[] { a.Ref, b.Ref })));
        Assert.True((await task).IsOk);
        Assert.Equal(2, produced!.Count);
    }

    // ---------- ② 交互模式：拖拽落空＝取消；点选空提交＝非法 ----------

    [Fact]
    public async Task Drag_Selector_Drop_On_Empty_Is_Cancelled()
    {
        var (manager, bridge) = Create();
        var target = new Entity("T");

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.FieldUnit,
                new ReferenceSetParameter(new[] { target.Ref }, 1, 1));
            return step.IsCancelled ? TargeterResult.Cancelled() : TargeterResult.Ok();
        });

        var selector = await bridge.WaitForNextSelectorAsync();
        Assert.Equal(SelectorInteractionMode.Drag, selector.Mode);

        Assert.True(selector.Submit(new DropEvent(target: null))); // 落在空处
        Assert.True((await task).IsCancelled);
    }

    [Fact]
    public async Task Drag_Selector_Drop_On_Target_Is_Ok()
    {
        var (manager, bridge) = Create();
        var target = new Entity("T");
        Ref<Entity>? produced = null;

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.FieldUnit,
                new ReferenceSetParameter(new[] { target.Ref }, 1, 1));
            produced = step.Value;
            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var selector = await bridge.WaitForNextSelectorAsync();
        Assert.True(selector.Submit(new DropEvent(target.Ref)));
        Assert.True((await task).IsOk);
        Assert.Same(target.Ref, produced);
    }

    [Fact]
    public async Task Click_Selector_Empty_Pick_Is_Invalid()
    {
        var (manager, bridge) = Create();
        var target = new Entity("T");

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.TargetPoint,
                new ReferenceSetParameter(new[] { target.Ref }, 1, 1));
            return step.IsOk
                ? TargeterResult.Ok()
                : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var selector = await bridge.WaitForNextSelectorAsync();
        Assert.True(selector.Submit(new PickEvent())); // 点选空提交

        var result = await task;
        Assert.True(result.IsFailed);
        Assert.Equal(TargeterFailureReason.InvalidSelection, result.Reason);
    }

    // ---------- ③ 重入与重试上限 ----------

    [Fact]
    public async Task Reentry_Delivers_Same_Selector_Identity()
    {
        var (manager, bridge) = Create();
        var allowed = new Entity("OK");
        var outside = new Entity("NO");

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.TargetPoint,
                new ReferenceSetParameter(new[] { allowed.Ref }, 1, 1));
            if (step.IsFailed && step.Failure == SelectorFailureReason.InvalidSelection)
            {
                step = await flow.Retry<Ref<Entity>>();
            }

            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var first = await bridge.WaitForNextSelectorAsync();
        first.Submit(new PickEvent(references: new[] { outside.Ref })); // 非法

        var second = await bridge.WaitForNextSelectorAsync();
        Assert.Equal(first.Id, second.Id); // 同一选择器实例（重入）

        second.Submit(new PickEvent(references: new[] { allowed.Ref }));
        Assert.True((await task).IsOk);
    }

    [Fact]
    public async Task Retry_Limit_Exceeded_Is_Failed()
    {
        var (manager, bridge) = Create();
        var allowed = new Entity("OK");
        var outside = new Entity("NO");

        // 一直提交非法项：交互脚本自动无限应答（驱动重试直到上限）——须在发起前挂好。
        bridge.InteractionScript = (description, responder) =>
            responder.Complete(
                description.RequestId,
                TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), outside.Ref));

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.TargetPoint,
                new ReferenceSetParameter(new[] { allowed.Ref }, 1, 1));
            for (var i = 0;
                 i < 40 && step.IsFailed && step.Failure == SelectorFailureReason.InvalidSelection;
                 i++)
            {
                step = await flow.Retry<Ref<Entity>>();
            }

            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var result = await task;
        Assert.True(result.IsFailed);
        Assert.True(
            result.Reason is TargeterFailureReason.InvalidSelection or TargeterFailureReason.RetryLimitExceeded,
            $"期望非法/超限失败，实际 {result.Reason}");
    }

    // ---------- ④ FIFO 串行与多步流程 ----------

    [Fact]
    public async Task Fifo_Serializes_And_MultiStep_Keeps_Order()
    {
        var (manager, bridge) = Create();
        var s = new Entity("S");
        var u = new Entity("U");

        var task = manager.RunAsync(async flow =>
        {
            var first = await flow.Step(SelectorTemplates.Slot, new ReferenceSetParameter(new[] { s.Ref }, 1, 1));
            if (!first.IsOk)
            {
                return TargeterResult.FromSelectorFailure(first.Failure);
            }

            var second = await flow.Step(SelectorTemplates.FieldUnit, new ReferenceSetParameter(new[] { u.Ref }, 1, 1));
            return second.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(second.Failure);
        });

        var firstSelector = await bridge.WaitForNextSelectorAsync();
        Assert.Equal(SelectorNames.Slot, firstSelector.SelectorName);
        firstSelector.Submit(new PickEvent(references: new[] { s.Ref }));

        var secondSelector = await bridge.WaitForNextSelectorAsync();
        Assert.Equal(SelectorNames.FieldUnit, secondSelector.SelectorName);
        secondSelector.Submit(new DropEvent(u.Ref));

        Assert.True((await task).IsOk);
    }

    [Fact]
    public async Task MultiStep_Cancel_On_Second_Propagates()
    {
        var (manager, bridge) = Create();
        var s = new Entity("S");
        var u = new Entity("U");

        var task = manager.RunAsync(async flow =>
        {
            var first = await flow.Step(SelectorTemplates.Slot, new ReferenceSetParameter(new[] { s.Ref }, 1, 1));
            if (!first.IsOk)
            {
                return TargeterResult.FromSelectorFailure(first.Failure);
            }

            var second = await flow.Step(SelectorTemplates.AfterPlacement, new ReferenceSetParameter(new[] { u.Ref }, 1, 1));
            return second.IsCancelled ? TargeterResult.Cancelled() : TargeterResult.Ok();
        });

        var firstSelector = await bridge.WaitForNextSelectorAsync();
        firstSelector.Submit(new PickEvent(references: new[] { s.Ref }));

        var secondSelector = await bridge.WaitForNextSelectorAsync();
        secondSelector.Submit(new CancelEvent()); // 反悔撤销

        Assert.True((await task).IsCancelled);
    }

    // ---------- ⑤ 失败路径 ----------

    [Fact]
    public async Task No_Bridge_Fails_Without_Throwing()
    {
        var manager = new TargeterManager(bridge: null);
        var result = await manager.RunAsync(_ => Task.FromResult(TargeterResult.Ok()));

        Assert.True(result.IsFailed);
        Assert.Equal(TargeterFailureReason.BridgeNotAssembled, result.Reason);
    }

    [Fact]
    public async Task Flow_Exception_Becomes_Fault_Failure()
    {
        var (manager, _) = Create();

        var result = await manager.RunAsync(_ => throw new InvalidOperationException("boom"));

        Assert.True(result.IsFailed);
        Assert.Equal(TargeterFailureReason.Fault, result.Reason);
    }

    [Fact]
    public async Task Min_Zero_MultiSelect_Empty_Pick_Is_Ok_Empty()
    {
        var (manager, bridge) = Create();
        var card = new Entity("C");
        IReadOnlyList<Ref<Entity>>? produced = null;

        var task = manager.RunAsync(async flow =>
        {
            var step = await flow.Step(
                SelectorTemplates.Mulligan,
                new ReferenceSetParameter(new[] { card.Ref }, min: 0, max: 1));
            produced = step.Value;
            return step.IsOk ? TargeterResult.Ok() : TargeterResult.FromSelectorFailure(step.Failure);
        });

        var selector = await bridge.WaitForNextSelectorAsync();
        selector.Submit(new PickEvent()); // 不换牌

        Assert.True((await task).IsOk);
        Assert.Empty(produced!);
    }
}
