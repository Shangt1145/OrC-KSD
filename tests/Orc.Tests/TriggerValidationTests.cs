using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 触发器合法性验证（加性扩展）验收覆盖：
/// ①验证暴露调用（合法／不合法）；②触发时验证（合法继续；不合法：不绑视图、不跑事件、留痕）；
/// ③空转不调用验证；④契约兜底（绑定失败→捕获＋标记＋安全结束；含视图声明非法与验证自身异常）；
/// ⑤失败标记三态（正常／验证拒绝／契约兜底失败）；附：嵌套不传染、取消类穿透。
/// </summary>
public class TriggerValidationTests
{
    // ---------- ①暴露调用 ----------

    [Fact]
    public void Validate_Is_Publicly_Callable_Default_Legal_And_Overridable()
    {
        var a = new Entity("A");

        var plain = new Trigger<CounterView>(name: "默认");
        Assert.True(plain.Validate(Array.Empty<Ref<Entity>>())); // 基类默认恒合法（空列表）
        Assert.True(plain.Validate(new[] { a.Ref }));            // 基类默认恒合法（任意列表）；公开可直接调用

        var custom = new RecordingTrigger<CounterView>(name: "自定义", behavior: refs => refs.Count > 0);
        Assert.False(custom.Validate(Array.Empty<Ref<Entity>>())); // 覆写生效（外部调用与触发内调用为同一方法）
        Assert.True(custom.Validate(new[] { a.Ref }));

        var state = false;
        var dynamicResult = new RecordingTrigger<CounterView>(name: "动态", behavior: _ => state);
        Assert.False(dynamicResult.Validate(Array.Empty<Ref<Entity>>()));
        state = true;
        Assert.True(dynamicResult.Validate(Array.Empty<Ref<Entity>>())); // 无缓存：每次完整调用、反映最新判定
    }

    // ---------- ②触发时验证：合法继续 ----------

    [Fact]
    public async Task InvokeAsync_Validates_With_Collected_First_Level_Refs_Dedup_Insertion_Order()
    {
        var engine = new LogicEngine();
        var a = new Entity("A");
        var b = new Entity("B");
        var nestedEntity = new Entity("Nested");
        var listEntity = new Entity("InList");
        var order = new List<string>();

        var trigger = new RecordingTrigger<CounterView>(name: "收集", events: new[]
        {
            new TriggerEvent<CounterView>("执行", (v, c, ct) => { order.Add("run"); return Task.CompletedTask; }),
        });

        var data = new Dictionary<string, object?>
        {
            ["A"] = a.Ref,
            ["N"] = 1,                       // 非引用值：忽略
            ["B"] = b.Ref,
            ["A2"] = a.Ref,                  // 重复引用：按引用相等去重
            ["Nested"] = new Dictionary<string, object?> { ["X"] = nestedEntity.Ref }, // 嵌套容器：不递归收集
            ["List"] = new List<object> { listEntity.Ref },                            // 集合：不递归收集
            ["Null"] = null,
            ["Text"] = "x",
        };

        var stream = await trigger.InvokeAsync(engine, data);

        Assert.Equal(1, trigger.ValidateCalls); // 每次触发固定先调用验证
        Assert.Equal(new[] { "A", "B" }, trigger.LastRefs!.Select(r => r.Name)); // 仅第一层、去重、保持插入序
        Assert.Same(a.Ref, trigger.LastRefs![0]); // 原始引用实例（未包装、同一对象）
        Assert.Same(b.Ref, trigger.LastRefs![1]);
        Assert.Equal(new[] { "run" }, order);                          // 合法：继续执行事件
        Assert.Equal(ExecutionOutcome.Normal, stream.Outcome);
    }

    [Fact]
    public async Task InvokeAsync_No_Refs_Passes_Empty_List_To_Validate()
    {
        var engine = new LogicEngine();
        var trigger = new RecordingTrigger<CounterView>(name: "无引用");

        await trigger.InvokeAsync(engine, new Dictionary<string, object?> { ["N"] = 1 });
        Assert.Empty(trigger.LastRefs!); // 无引用＝空列表

        await trigger.InvokeAsync(engine, null); // data 为 null＝空载体
        Assert.Empty(trigger.LastRefs!);

        Assert.Equal(2, trigger.ValidateCalls); // 每次触发均完整调用（无缓存/无短路）
    }

    // ---------- ②触发时验证：不合法 ----------

    [Fact]
    public async Task InvokeAsync_Rejected_Skips_Binding_And_Events_Writes_Trace_And_Marks()
    {
        var engine = new LogicEngine();
        var executed = false;
        var trigger = new RecordingTrigger<ShieldView>(
            name: "拒绝者",
            events: new[] { new TriggerEvent<ShieldView>("不应执行", (v, c, ct) => { executed = true; return Task.CompletedTask; }) },
            behavior: _ => false);

        // 空数据（ShieldView 必填缺失）：若执行绑定会以 KeyNotFound 契约兜底；验证拒绝发生在绑定之前 → 不应有绑定错误记录。
        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.False(executed);                        // 不跑任何事件
        Assert.Same(engine.RootStream, stream.Parent); // 返回流（正常挂载）

        var trace = Assert.Single(stream.Entries);     // 恰一条留痕（无 Error 记录＝未尝试绑定）
        Assert.Equal(LogEntryKind.Log, trace.Kind);
        Assert.Equal(LogLevel.Warning, trace.Level);
        Assert.Contains("validation:rejected", trace.Keywords);
        Assert.Equal("拒绝者", trace.Source);          // source＝触发器展示名

        Assert.Equal(ExecutionOutcome.ValidationRejected, stream.Outcome); // 失败标记：验证拒绝
    }

    // ---------- ③空转不调用验证 ----------

    [Fact]
    public async Task InvokeAsync_On_Interrupted_Chain_Does_Not_Call_Validate()
    {
        var engine = new LogicEngine();
        var sub = new RecordingTrigger<CounterView>(name: "子");
        EventStream? subStream = null;

        var killer = new Trigger<CounterView>(name: "中断者", events: new[]
        {
            new TriggerEvent<CounterView>("中断并调用", async (v, c, ct) =>
            {
                c.Interrupt();
                subStream = await sub.InvokeAsync(engine, new Dictionary<string, object?>());
            }),
        });

        await killer.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(0, sub.ValidateCalls);                        // 空转：不调用验证
        Assert.NotNull(subStream);
        Assert.Equal(ExecutionOutcome.Normal, subStream!.Outcome); // 空转结局＝正常
        Assert.Empty(subStream.LocalEntries);                      // 无留痕、无写入
    }

    // ---------- ④契约兜底 ----------

    [Fact]
    public async Task InvokeAsync_Validate_Throws_Is_Treated_As_Contract_Fallback()
    {
        var engine = new LogicEngine();
        var executed = false;
        var trigger = new RecordingTrigger<CounterView>(
            name: "异常验证",
            events: new[] { new TriggerEvent<CounterView>("不应执行", (v, c, ct) => { executed = true; return Task.CompletedTask; }) },
            behavior: _ => throw new InvalidOperationException("验证爆炸"));

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>()); // 不外传、安全结束

        Assert.False(executed); // 不继续执行、不外传
        Assert.Equal(ExecutionOutcome.ContractFailure, stream.Outcome);

        var record = Assert.Single(stream.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("exception:InvalidOperationException", record.Keywords);
        Assert.Equal("异常验证", record.Source); // source＝触发器展示名（纯展示名、无事件上下文）
        Assert.Equal(typeof(InvalidOperationException).FullName, record.Data["exceptionType"]);
        Assert.Equal("验证爆炸", record.Data["message"]);
    }

    [Fact]
    public async Task InvokeAsync_Validate_Throws_Cancellation_Pierces()
    {
        var engine = new LogicEngine();
        var trigger = new RecordingTrigger<CounterView>(
            name: "取消验证", behavior: _ => throw new OperationCanceledException("取消"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => trigger.InvokeAsync(engine, new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task InvokeAsync_Invalid_View_Declaration_Is_Captured_As_Contract_Fallback()
    {
        var engine = new LogicEngine();
        var executed = false;
        var trigger = new RecordingTrigger<ConflictView>(
            name: "非法声明",
            events: new[] { new TriggerEvent<ConflictView>("不应执行", (v, c, ct) => { executed = true; return Task.CompletedTask; }) });

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>()); // 视图声明非法：捕获处理、安全结束

        Assert.False(executed);
        Assert.Equal(ExecutionOutcome.ContractFailure, stream.Outcome);

        var record = Assert.Single(stream.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("exception:ArgumentException", record.Keywords);
        Assert.Equal("非法声明", record.Source);
    }

    // ---------- ⑤失败标记三态 ----------

    [Fact]
    public async Task Outcome_Distinguishes_Three_Endings_And_Event_Tolerance_Stays_Normal()
    {
        var engine = new LogicEngine();

        var normalStream = await new Trigger<CounterView>(name: "正常").InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.Equal(ExecutionOutcome.Normal, normalStream.Outcome); // ①正常（含空执行）

        var rejectedStream = await new RecordingTrigger<CounterView>(name: "拒绝", behavior: _ => false)
            .InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.Equal(ExecutionOutcome.ValidationRejected, rejectedStream.Outcome); // ②验证拒绝

        var brokenStream = await new Trigger<ShieldView>(name: "绑定失败")
            .InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.Equal(ExecutionOutcome.ContractFailure, brokenStream.Outcome); // ③契约兜底失败

        // 事件容错路径：异常被隔离记录、执行完整完成 → 结局仍为正常（记录与标记分离）
        var tolerant = new Trigger<CounterView>(name: "容错", events: new[]
        {
            new TriggerEvent<CounterView>("炸", (v, c, t) => throw new InvalidOperationException("业务故障")),
        });
        var tolerantStream = await tolerant.InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.Equal(ExecutionOutcome.Normal, tolerantStream.Outcome);
        Assert.Contains(tolerantStream.Entries, e => e.Keywords.Contains("exception:InvalidOperationException")); // 记录照常
    }

    // ---------- 附：嵌套不传染 ----------

    [Fact]
    public async Task Outcome_Does_Not_Propagate_To_Ancestors()
    {
        var engine = new LogicEngine();
        var rejectedChild = new RecordingTrigger<CounterView>(name: "子拒绝", behavior: _ => false);
        var failingChild = new Trigger<ShieldView>(name: "子失败");

        EventStream? rejectedStream = null;
        EventStream? failingStream = null;
        var order = new List<string>();
        var parent = new Trigger<CounterView>(name: "父", events: new[]
        {
            new TriggerEvent<CounterView>("调拒绝子", async (v, c, t) => { rejectedStream = await rejectedChild.InvokeAsync(engine, new Dictionary<string, object?>()); }),
            new TriggerEvent<CounterView>("调失败子", async (v, c, t) => { failingStream = await failingChild.InvokeAsync(engine, new Dictionary<string, object?>()); }),
            new TriggerEvent<CounterView>("父继续", (v, c, t) => { order.Add("parent-after"); return Task.CompletedTask; }),
        });

        var parentStream = await parent.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "parent-after" }, order); // 父层事件全部照常执行（仅本次取消/兜底，不传染嵌套链）
        Assert.NotNull(rejectedStream);
        Assert.NotNull(failingStream);
        Assert.Equal(ExecutionOutcome.ValidationRejected, rejectedStream!.Outcome); // 子流带各自标记
        Assert.Equal(ExecutionOutcome.ContractFailure, failingStream!.Outcome);
        Assert.Equal(ExecutionOutcome.Normal, parentStream.Outcome);                // 祖先流标记不变（标记不传播）
        Assert.Contains(parentStream.Entries, e => e.Keywords.Contains("validation:rejected"));            // 记录照常冒泡
        Assert.Contains(parentStream.Entries, e => e.Keywords.Contains("exception:KeyNotFoundException"));
    }

    // ---------- 测试辅助 ----------

    /// <summary>验证覆写探针：可记录调用与输入、可定制判定行为（含抛异常路径）。</summary>
    private sealed class RecordingTrigger<TView> : Trigger<TView>
        where TView : class
    {
        public RecordingTrigger(
            string? name = null,
            IEnumerable<TriggerEvent<TView>>? events = null,
            Func<IReadOnlyList<Ref<Entity>>, bool>? behavior = null)
            : base(name, events: events)
        {
            ValidateBehavior = behavior;
        }

        public Func<IReadOnlyList<Ref<Entity>>, bool>? ValidateBehavior { get; set; }

        public int ValidateCalls { get; private set; }

        public IReadOnlyList<Ref<Entity>>? LastRefs { get; private set; }

        public override bool Validate(IReadOnlyList<Ref<Entity>> refs)
        {
            ValidateCalls++;
            LastRefs = refs;
            return ValidateBehavior?.Invoke(refs) ?? true;
        }
    }
}
