using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 触发器合法性验证（J2：判定器承载）验收覆盖——原覆写形态逐条保留锚定（受控适配：形态重写、语义不降级）：
/// ①默认恒合法＋公开入口可直接调用（未绑定）；②绑定判定器生效（外部调用与触发内调用同一判定源、无缓存/无短路）；
/// ③触发时验证（合法继续；不合法：不绑视图、不跑事件、留痕）；④空转不调用验证；
/// ⑤契约兜底（判定器自身异常→捕获＋标记＋安全结束；含视图声明非法与输出契约不符）；
/// ⑥失败标记三态（正常／验证拒绝／契约兜底失败）；附：嵌套不传染、取消类穿透；
/// J2 新增：⑦重复绑定＝拒绝（fail-fast；绑定＝装配期一次建立、无解绑/重绑通道——结构面）；
/// ⑧取数形态（EvaluateValidation 返回拒绝类别）；⑨收集面与 subject provider 输入。
/// </summary>
public class TriggerValidationTests
{
    // ---------- ①默认恒合法＋公开入口＋绑定生效 ----------

    [Fact]
    public void Validate_Is_Publicly_Callable_Default_Legal_And_Bound_Judicator_Overrides()
    {
        var a = new Entity("A");

        var plain = new Trigger<CounterView>(name: "默认");
        Assert.True(plain.Validate(Array.Empty<Ref<Entity>>())); // 未绑定＝基类默认恒合法（空列表）；公开可直接调用
        Assert.True(plain.Validate(new[] { a.Ref }));            // 未绑定＝恒合法（任意列表）

        var probe = new ProbeValidationJudicator(refs => refs.Count > 0);
        var bound = new Trigger<CounterView>(name: "绑定");
        bound.BindValidation(JudicatorBinding.FromStandalone("test.probe", probe));
        Assert.False(bound.Validate(Array.Empty<Ref<Entity>>())); // 绑定判定器生效（外部调用）
        Assert.True(bound.Validate(new[] { a.Ref }));

        var state = false;
        var dynamicProbe = new ProbeValidationJudicator(_ => state);
        var dynamicResult = new Trigger<CounterView>(name: "动态");
        dynamicResult.BindValidation(JudicatorBinding.FromStandalone("test.dynamic", dynamicProbe));
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

        var probe = new ProbeValidationJudicator(_ => true);
        var trigger = new Trigger<CounterView>(name: "收集", events: new[]
        {
            new TriggerEvent<CounterView>("执行", (v, c, ct) => { order.Add("run"); return Task.CompletedTask; }),
        });
        trigger.BindValidation(
            JudicatorBinding.FromStandalone("test.collect", probe),
            refs => refs.Count > 0 ? refs[0] : null);

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

        Assert.Equal(1, probe.Calls); // 每次触发固定先调用验证
        Assert.Equal(new[] { "A", "B" }, probe.LastRefs!.Select(r => r.Name)); // 仅第一层、去重、保持插入序
        Assert.Same(a.Ref, probe.LastRefs![0]); // 原始引用实例（未包装、同一对象）
        Assert.Same(b.Ref, probe.LastRefs![1]);
        Assert.Same(a.Ref, probe.LastSubject);  // 被判定对象＝subjectProvider（refs 首位）按次提供
        Assert.Equal(new[] { "run" }, order);                          // 合法：继续执行事件
        Assert.Equal(ExecutionOutcome.Normal, stream.Outcome);
    }

    [Fact]
    public async Task InvokeAsync_No_Refs_Passes_Empty_List_To_Validate()
    {
        var engine = new LogicEngine();
        var probe = new ProbeValidationJudicator(_ => true);
        var trigger = new Trigger<CounterView>(name: "无引用");
        trigger.BindValidation(
            JudicatorBinding.FromStandalone("test.empty", probe),
            refs => refs.Count > 0 ? refs[0] : null);

        await trigger.InvokeAsync(engine, new Dictionary<string, object?> { ["N"] = 1 });
        Assert.Empty(probe.LastRefs!); // 无引用＝空列表
        Assert.Null(probe.LastSubject); // 无引用＝无被判定对象

        await trigger.InvokeAsync(engine, null); // data 为 null＝空载体
        Assert.Empty(probe.LastRefs!);

        Assert.Equal(2, probe.Calls); // 每次触发均完整调用（无缓存/无短路）
    }

    // ---------- ②触发时验证：不合法 ----------

    [Fact]
    public async Task InvokeAsync_Rejected_Skips_Binding_And_Events_Writes_Trace_And_Marks()
    {
        var engine = new LogicEngine();
        var executed = false;
        var probe = new ProbeValidationJudicator(_ => false);
        var trigger = new Trigger<SampleView>(
            name: "拒绝者",
            events: new[] { new TriggerEvent<SampleView>("不应执行", (v, c, ct) => { executed = true; return Task.CompletedTask; }) });
        trigger.BindValidation(JudicatorBinding.FromStandalone("test.reject", probe));

        // 空数据（SampleView 必填缺失）：若执行绑定会以 KeyNotFound 契约兜底；验证拒绝发生在绑定之前 → 不应有绑定错误记录。
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
        var probe = new ProbeValidationJudicator(_ => true);
        var sub = new Trigger<CounterView>(name: "子");
        sub.BindValidation(JudicatorBinding.FromStandalone("test.sub", probe));
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

        Assert.Equal(0, probe.Calls);                              // 空转：不调用验证
        Assert.NotNull(subStream);
        Assert.Equal(ExecutionOutcome.Normal, subStream!.Outcome); // 空转结局＝正常
        Assert.Empty(subStream.LocalEntries);                      // 无留痕、无写入
    }

    // ---------- ④契约兜底 ----------

    [Fact]
    public async Task InvokeAsync_Judicator_Throws_Is_Treated_As_Contract_Fallback()
    {
        var engine = new LogicEngine();
        var executed = false;
        var probe = new ProbeValidationJudicator((_, _) => throw new InvalidOperationException("验证爆炸"));
        var trigger = new Trigger<CounterView>(
            name: "异常验证",
            events: new[] { new TriggerEvent<CounterView>("不应执行", (v, c, ct) => { executed = true; return Task.CompletedTask; }) });
        trigger.BindValidation(JudicatorBinding.FromStandalone("test.boom", probe));

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
    public async Task InvokeAsync_Judicator_Throws_Cancellation_Pierces()
    {
        var engine = new LogicEngine();
        var probe = new ProbeValidationJudicator((_, _) => throw new OperationCanceledException("取消"));
        var trigger = new Trigger<CounterView>(name: "取消验证");
        trigger.BindValidation(JudicatorBinding.FromStandalone("test.cancel", probe));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => trigger.InvokeAsync(engine, new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task InvokeAsync_Invalid_View_Declaration_Is_Captured_As_Contract_Fallback()
    {
        var engine = new LogicEngine();
        var executed = false;
        var trigger = new Trigger<ConflictView>(
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
        Assert.Equal(ExecutionOutcome.Normal, normalStream.Outcome); // ①正常（含空执行；未绑定＝恒合法）

        var rejectProbe = new ProbeValidationJudicator(_ => false);
        var rejected = new Trigger<CounterView>(name: "拒绝");
        rejected.BindValidation(JudicatorBinding.FromStandalone("test.rejected", rejectProbe));
        var rejectedStream = await rejected.InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.Equal(ExecutionOutcome.ValidationRejected, rejectedStream.Outcome); // ②验证拒绝

        var brokenStream = await new Trigger<SampleView>(name: "绑定失败")
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
        var rejectProbe = new ProbeValidationJudicator(_ => false);
        var rejectedChild = new Trigger<CounterView>(name: "子拒绝");
        rejectedChild.BindValidation(JudicatorBinding.FromStandalone("test.child-reject", rejectProbe));
        var failingChild = new Trigger<SampleView>(name: "子失败");

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

    // ---------- J2 新增：绑定生命周期（重复绑定＝拒绝） ----------

    [Fact]
    public void Bind_Duplicate_Is_Rejected_And_First_Binding_Stays_Effective()
    {
        var first = new ProbeValidationJudicator(_ => true);
        var second = new ProbeValidationJudicator(_ => false);
        var trigger = new Trigger<CounterView>(name: "重复绑定");
        trigger.BindValidation(JudicatorBinding.FromStandalone("test.first", first));

        // 重复绑定＝fail-fast（绑定＝装配期一次性声明动作、不幂等宽容）；无解绑/重绑/运行期换绑通道（结构面——不提供对应 API）。
        Assert.Throws<InvalidOperationException>(
            () => trigger.BindValidation(JudicatorBinding.FromStandalone("test.second", second)));

        Assert.True(trigger.Validate(Array.Empty<Ref<Entity>>())); // 首次绑定保持有效（重复被拒绝、不生效）
        Assert.Equal(1, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    // ---------- J2 新增：取数形态（合法性＋拒绝类别） ----------

    [Fact]
    public void EvaluateValidation_Returns_Verdict_With_Rejection_Reason()
    {
        var category = new object();
        var probe = new ProbeValidationJudicator((_, _) => ValidationVerdict.Invalid(category));
        var trigger = new Trigger<CounterView>(name: "取数");
        trigger.BindValidation(JudicatorBinding.FromStandalone("test.verdict", probe));

        var verdict = trigger.EvaluateValidation(Array.Empty<Ref<Entity>>());
        Assert.False(verdict.IsValid);
        Assert.Same(category, verdict.RejectionReason); // 拒绝类别经取数面传递（单个判定源）

        var probeNoReason = new ProbeValidationJudicator((_, _) => ValidationVerdict.Invalid()); // 无类别
        var triggerNoReason = new Trigger<CounterView>(name: "取数无类别");
        triggerNoReason.BindValidation(JudicatorBinding.FromStandalone("test.verdict2", probeNoReason));
        var verdict2 = triggerNoReason.EvaluateValidation(Array.Empty<Ref<Entity>>());
        Assert.False(verdict2.IsValid);
        Assert.Null(verdict2.RejectionReason); // 缺类别＝null（消费侧降级为一般性失败原因）

        var unbound = new Trigger<CounterView>(name: "未绑定").EvaluateValidation(Array.Empty<Ref<Entity>>());
        Assert.True(unbound.IsValid);          // 未绑定＝恒合法
        Assert.Null(unbound.RejectionReason);
    }

    // ---------- J2 新增：同一判定源（公开入口与触发内调用） ----------

    [Fact]
    public async Task Public_Entry_And_Trigger_Invocation_Share_Same_Judicator_Source()
    {
        var engine = new LogicEngine();
        var probe = new ProbeValidationJudicator(_ => true);
        var trigger = new Trigger<CounterView>(name: "同源");
        trigger.BindValidation(JudicatorBinding.FromStandalone("test.shared-source", probe));

        Assert.True(trigger.Validate(Array.Empty<Ref<Entity>>())); // 外部预检（公开入口）
        Assert.Equal(1, probe.Calls);

        await trigger.InvokeAsync(engine, new Dictionary<string, object?>()); // 触发内调用
        Assert.Equal(2, probe.Calls); // 同一判定器、每次实时执行（无缓存/无短路）
    }

    // ---------- J2 新增：输出契约不符＝契约兜底 ----------

    [Fact]
    public async Task InvokeAsync_Output_Contract_Violation_Is_Contract_Fallback()
    {
        var engine = new LogicEngine();
        var trigger = new Trigger<CounterView>(name: "契约不符");
        trigger.BindValidation(
            JudicatorBinding.FromStandalone("test.wrong-output", new RawJudicator(_ => new object[] { false })));

        // 统一面输出非 [ValidationVerdict]＝契约不符（fail-fast 族）→ 触发路径契约兜底。
        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(ExecutionOutcome.ContractFailure, stream.Outcome);
        var record = Assert.Single(stream.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("exception:InvalidOperationException", record.Keywords);
        Assert.Equal("契约不符", record.Source);
    }

    // ---------- 测试辅助 ----------

    /// <summary>验证判定器探针（测试夹具）：可记录调用与输入、可定制判定行为（含抛异常路径）。
    /// 注：计数/记录为测试观测面（等价原覆写探针 ValidateCalls 语义），不改变判定器「无跨调用可变状态」的机制约束。</summary>
    private sealed class ProbeValidationJudicator : ValidationJudicator
    {
        private readonly Func<IReadOnlyList<Ref<Entity>>, Ref<Entity>?, ValidationVerdict> _behavior;

        public ProbeValidationJudicator(Func<IReadOnlyList<Ref<Entity>>, Ref<Entity>?, ValidationVerdict> behavior)
            => _behavior = behavior;

        public ProbeValidationJudicator(Func<IReadOnlyList<Ref<Entity>>, bool> behavior)
            : this((refs, _) => behavior(refs) ? ValidationVerdict.Valid : ValidationVerdict.Invalid())
        {
        }

        public int Calls { get; private set; }

        public IReadOnlyList<Ref<Entity>>? LastRefs { get; private set; }

        public Ref<Entity>? LastSubject { get; private set; }

        protected override ValidationVerdict ValidateLogic(IReadOnlyList<Ref<Entity>> refs, Ref<Entity>? subject)
        {
            Calls++;
            LastRefs = refs;
            LastSubject = subject;
            return _behavior(refs, subject);
        }
    }

    /// <summary>统一形态判定器（测试夹具：模拟输出契约不符的改写——统一面直接返回非法输出）。</summary>
    private sealed class RawJudicator : Judicator
    {
        private readonly Func<object[]?, object[]?> _logic;

        public RawJudicator(Func<object[]?, object[]?> logic) => _logic = logic;

        public override object[]? Invoke(object[]? args) => _logic(args);
    }
}
