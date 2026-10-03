using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// Targeter 写入双模式与用法验收（⑤⑥）：扁平→视图 [Mutate] 属性写入/读取；非扁平→产出结构整体写入、按槽位读取；
/// 用法 a（独立：产出作参数、链路证明）与用法 b（请求对象传参＋结果传参）分别有测试证明（允许与位置范式合并覆盖）；
/// 位置范式：ctxv 内 targeting（主路径）与 handler 内调用并写入触发器上下文（写入后本触发器后续事件可读取）。
/// </summary>
public class TargeterUsageTests
{
    [Fact]
    public async Task CtxView_Main_Path_Targeting_Writes_Flat_Single_Into_Mutate_Property()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = (description, responder) =>
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[1])),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter();

        Ref<Entity>? written = null;
        Ref<Entity>? readBack = null;

        // 位置范式①（主路径）：ctxv 内 targeting——事件逻辑内 await Targeting()，结果经视图 [Mutate] 属性写入 ctx
        var trigger = new Trigger<TargetingSingleView>(name: "ctxv范式", events: new[]
        {
            new TriggerEvent<TargetingSingleView>("选择目标", async (view, ctx, ct) =>
            {
                var result = await targeter.Targeting();
                written = TargetOutcomeWriter.WriteSingle(result, r => view.Target = r);
                readBack = view.Target; // 写入后经视图属性读回
            }),
        });

        var engine = new LogicEngine();
        var stream = await trigger.InvokeAsync(engine);

        Assert.Equal(ExecutionOutcome.Normal, stream.Outcome);
        Assert.Same(e2.Ref, written);   // 目标正确
        Assert.Same(e2.Ref, readBack);  // 写入可读（行为断言）
    }

    [Fact]
    public async Task Flat_List_Write_And_Read_Back_Through_Mutate_Property()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = (description, responder) =>
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), e1.Ref, e2.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new[] { new MultiSelectSlot(1, 2) });

        IReadOnlyList<Ref<Entity>>? readBack = null;
        var trigger = new Trigger<TargetingListView>(name: "ctxv列表范式", events: new[]
        {
            new TriggerEvent<TargetingListView>("选择目标", async (view, ctx, ct) =>
            {
                var result = await targeter.Targeting();
                TargetOutcomeWriter.WriteList(result, list => view.Targets = list);
                readBack = view.Targets;
            }),
        });

        var stream = await trigger.InvokeAsync(new LogicEngine());

        Assert.Equal(ExecutionOutcome.Normal, stream.Outcome);
        Assert.NotNull(readBack);
        Assert.Equal(new[] { e1.Ref, e2.Ref }, readBack!.ToArray());
    }

    [Fact]
    public async Task Slotted_Outcome_Written_As_Whole_And_Read_By_Slot_Name()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref)),
            InteractionScript = (description, responder) => responder.Complete(
                description.RequestId,
                new Dictionary<string, IReadOnlyList<Ref<Entity>>>
                {
                    ["units"] = new[] { e1.Ref, e2.Ref },
                    ["hero"] = new[] { e3.Ref },
                }),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new MultiSelectSlot(1, 2, "units"),
            new SingleSelectSlot("hero"),
        });

        TargetOutcome? written = null;
        TargetOutcome? readBack = null;
        var trigger = new Trigger<TargetingSlottedView>(name: "ctxv非扁平范式", events: new[]
        {
            new TriggerEvent<TargetingSlottedView>("选择目标", async (view, ctx, ct) =>
            {
                var result = await targeter.Targeting();
                // 非扁平：产出结构整体写入 ctx（[Mutate] 属性承载）；读取时按槽位取强类型引用
                written = TargetOutcomeWriter.WriteOutcome(result, outcome => view.SlotOutcome = outcome);
                readBack = view.SlotOutcome;
            }),
        });

        var stream = await trigger.InvokeAsync(new LogicEngine());

        Assert.Equal(ExecutionOutcome.Normal, stream.Outcome);
        Assert.NotNull(written);
        Assert.Same(written, readBack); // 同一产出结构实例往返
        Assert.Equal(new[] { e1.Ref, e2.Ref }, readBack!.GetSelection("units").ToArray());
        Assert.Equal(new[] { e3.Ref }, readBack!.GetSelection("hero").ToArray());
        Assert.False(readBack!.IsFlat);
    }

    [Fact]
    public async Task Usage_A_Outcome_As_Parameter_Flows_To_Consumer()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(); // 用法 a：独立 targeting（非 ctxv 内亦可用）

        // 消费方（另一调用方）：接收产出引用并实际使用
        var consumed = new List<Ref<Entity>>();
        void Consumer(Ref<Entity> reference) => consumed.Add(reference);

        var result = await targeter.Targeting();
        Assert.Equal(TargetingStatus.Success, result.Status);

        // 链路：targeting 成功 → 产出作为参数 → 被消费方实际使用
        TargetOutcomeWriter.WriteSingle(result, Consumer);

        Assert.Single(consumed);
        Assert.Same(e1.Ref, consumed[0]);
    }

    [Fact]
    public async Task Usage_B_Request_Object_Passed_To_Other_Caller_Who_Executes_It()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge);

        // 用法 b（请求对象传参）：请求对象作为参数交给另一调用方；接收方持有并使用该请求对象发起 targeting
        var result = await ExecuteForOtherCaller(manager.CreateTargeter());

        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.Same(e1.Ref, result.Outcome!.Single);
    }

    [Fact]
    public async Task Usage_B_Result_Object_Passed_To_Other_Caller()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge);

        var result = await manager.CreateTargeter().Targeting();

        // 用法 b（结果传参）：结果对象交其他调用方使用
        var used = ConsumeResult(result);

        Assert.Same(e1.Ref, used);
    }

    [Fact]
    public async Task Handler_Path_Writes_Trigger_Context_And_Later_Event_Reads_It()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter();

        Ref<Entity>? readInSecondEvent = null;
        Ref<Entity>? forwardedToOtherFlow = null;

        // 位置范式②：handler 内调用 targeter（执行期间动态获取）并写入触发器上下文；
        // 写入后本触发器后续事件可读取（ctx 共享范围内可见）
        var trigger = new Trigger<TargetingHandlerView>(name: "handler范式", events: new[]
        {
            new TriggerEvent<TargetingHandlerView>("获取目标", async (view, ctx, ct) =>
            {
                var result = await targeter.Targeting();
                if (result.Status == TargetingStatus.Success)
                {
                    view.AssignedTarget = result.Outcome!.Single;
                }
            }),
            new TriggerEvent<TargetingHandlerView>("使用目标", (view, ctx, ct) =>
            {
                readInSecondEvent = view.AssignedTarget; // 后续事件读取前事件写入的上下文数据
                forwardedToOtherFlow = ConsumeResultValue(readInSecondEvent); // 产出/结果再传其他流程
                return Task.CompletedTask;
            }),
        });

        var stream = await trigger.InvokeAsync(new LogicEngine());

        Assert.Equal(ExecutionOutcome.Normal, stream.Outcome);
        Assert.Same(e1.Ref, readInSecondEvent);
        Assert.Same(e1.Ref, forwardedToOtherFlow);
    }

    [Fact]
    public async Task Write_Helpers_Guard_Against_NonSuccess_Results_And_Wrong_Shape()
    {
        // 非成功结果：辅助抛明确异常（辅助为成功路径便捷；调用方应先判 Status）
        var failed = await new TargeterManager().CreateTargeter().Targeting();
        Assert.Equal(TargetingStatus.Failed, failed.Status);
        Assert.Throws<InvalidOperationException>(() => TargetOutcomeWriter.WriteOutcome(failed, _ => { }));
        Assert.Throws<InvalidOperationException>(() => TargetOutcomeWriter.WriteSingle(failed, _ => { }));
        Assert.Throws<InvalidOperationException>(() => TargetOutcomeWriter.WriteList(failed, _ => { }));

        // 多选（列表）形态：不按单引用写
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var task = manager.CreateTargeter(slots: new[] { new MultiSelectSlot(1, 2) }).Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder.Complete(
            description.RequestId,
            TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref, e2.Ref)));
        var multi = await task;
        Assert.Throws<InvalidOperationException>(() => TargetOutcomeWriter.WriteSingle(multi, _ => { }));

        // 非扁平（多槽位）形态：不按扁平列表写
        var task2 = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new SingleSelectSlot("a"),
            new SingleSelectSlot("b"),
        }).Targeting();
        var (description2, responder2) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder2.Complete(description2.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>
        {
            ["a"] = new[] { e1.Ref },
            ["b"] = new[] { e2.Ref },
        }));
        var slotted = await task2;
        Assert.Throws<InvalidOperationException>(() => TargetOutcomeWriter.WriteList(slotted, _ => { }));
    }

    private static async Task<TargetingResult> ExecuteForOtherCaller(Targeter request)
        => await request.Targeting();

    private static Ref<Entity> ConsumeResult(TargetingResult result)
        => result.Outcome!.Single!;

    private static Ref<Entity>? ConsumeResultValue(Ref<Entity>? reference) => reference;
}

/// <summary>扁平单引用承载视图（[Mutate] 属性＝读写数据面；Optional＝首次写入创建键）。</summary>
[ContextView]
public class TargetingSingleView
{
    [Optional]
    [Mutate]
    public virtual Ref<Entity>? Target { get; set; }
}

/// <summary>扁平列表承载视图。</summary>
[ContextView]
public class TargetingListView
{
    [Optional]
    [Mutate]
    public virtual IReadOnlyList<Ref<Entity>>? Targets { get; set; }
}

/// <summary>多槽位产出结构承载视图（非扁平）。</summary>
[ContextView]
public class TargetingSlottedView
{
    [Optional]
    [Mutate]
    public virtual TargetOutcome? SlotOutcome { get; set; }
}

/// <summary>handler 路径承载视图（写入后由后续事件读取）。</summary>
[ContextView]
public class TargetingHandlerView
{
    [Optional]
    [Mutate]
    public virtual Ref<Entity>? AssignedTarget { get; set; }
}
