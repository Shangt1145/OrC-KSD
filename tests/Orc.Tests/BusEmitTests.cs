using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点①②（广播侧）：部署更新→被动激活（含载荷传递）；同更新多订阅者顺序（参考优先级+注册序）；
/// 失败记录+继续（含绑定失败——契约强化后记录由触发器本流兜底写入、经冒泡可见；不双重记录）；取消语义（入口已取消/广播中取消）；快照语义；中断空转。
/// </summary>
public class BusEmitTests
{
    // ---------- 验收①：部署更新→被动激活（含载荷） ----------

    [Fact]
    public async Task Emit_Activates_Passive_Trigger_With_Payload_In_View_And_Ctx()
    {
        var engine = new LogicEngine();
        var card = new Entity("卡牌A");
        Ref<Entity>? seenCard = null;
        var seenAmount = -1;
        Context? captured = null;

        var trigger = new Trigger<BusPayloadView>(name: "部署响应", kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<BusPayloadView>("反应", (view, ctx, ct) =>
            {
                seenCard = view.Card;
                seenAmount = view.Amount;
                captured = ctx;
                view.Amount = 42; // [Mutate] 改写回写 ctx（Bind 直连）
                return Task.CompletedTask;
            }),
        }, hooks: new[] { Updates.CardPlaced });
        engine.Bus.Mount(trigger);

        await engine.Emit(Updates.CardPlaced, new Dictionary<string, object?>
        {
            ["Card"] = card.Ref,
            ["Amount"] = 5,
            ["Marker"] = "m",
        });

        Assert.Same(card.Ref, seenCard); // 载荷引用原样入（同一 Ref 实例）
        Assert.Equal(5, seenAmount);
        Assert.NotNull(captured);
        Assert.Equal(42, (int)captured!.Get("Amount")!);
        Assert.True(captured.TryGet("Marker", out var marker));
        Assert.Equal("m", marker);
        // 框架不注入保留键、不占键名：ctx 仅含载荷键
        Assert.False(captured.TryGet("updateType", out _));
        Assert.False(captured.TryGet("UpdateType", out _));
    }

    [Fact]
    public async Task Emit_Flows_Custom_Update_Strings_Matching_By_Ordinal()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var custom = "deck.shuffled";
        var t = BusTestHelpers.RecordingPassive("T", new[] { custom }, trace);
        var trailing = BusTestHelpers.RecordingPassive("T2", new[] { custom + " " }, trace); // 尾空格 hook 同样合法
        engine.Bus.Mount(t);
        engine.Bus.Mount(trailing);

        await engine.Emit(custom);
        Assert.Equal(new[] { "T" }, trace); // 自定义字符串与常量平等（无注册/白名单）；精确匹配

        trace.Clear();
        await engine.Emit(custom + " "); // 原样比较：尾空格是不同更新
        Assert.Equal(new[] { "T2" }, trace);

        trace.Clear();
        await engine.Emit("Deck.Shuffled"); // 大小写敏感
        Assert.Empty(trace);
    }

    [Fact]
    public async Task Emit_Validates_Update_Type()
    {
        var engine = new LogicEngine();
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.Emit(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.Emit(""));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.Emit("  "));
    }

    // ---------- 验收②：多订阅者顺序（参考优先级+注册序） ----------

    [Fact]
    public async Task Emit_Orders_By_Mount_Priority_Ascending_Then_Registration_Ascending()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("低", new[] { "u" }, trace, priority: UpdatePriorities.Low));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("高1", new[] { "u" }, trace, priority: UpdatePriorities.High));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("中", new[] { "u" }, trace)); // 默认 Normal
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("高2", new[] { "u" }, trace, priority: UpdatePriorities.High));

        await engine.Emit("u");

        Assert.Equal(new[] { "高1", "高2", "中", "低" }, trace); // 数值小者先（High → Normal → Low）；同优先级先挂先执行
    }

    [Fact]
    public async Task Emit_Executes_Lower_Priority_Values_First()
    {
        // 方向锁定：升序（数值小者先）——数值大者后执行；执行序与挂载序相反，证明由优先级主导（而非注册序）
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("数值大", new[] { "u" }, trace, priority: 900));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("数值小", new[] { "u" }, trace, priority: 100));

        await engine.Emit("u");

        Assert.Equal(new[] { "数值小", "数值大" }, trace);
    }

    [Fact]
    public async Task Emit_Same_Priority_Order_Is_Registration_Stable_Across_Rounds()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("A", new[] { "u" }, trace));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("B", new[] { "u" }, trace));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("C", new[] { "u" }, trace));

        await engine.Emit("u");
        await engine.Emit("u");
        Assert.Equal(new[] { "A", "B", "C", "A", "B", "C" }, trace); // 顺序确定、可重现
    }

    [Fact]
    public async Task Emit_Accepts_Arbitrary_Priority_Values()
    {
        // 任意 int（含负值、极小极大）合法、不设界；参考常量不作校验依据
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("自定义", new[] { "u" }, trace, priority: 55));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("极小", new[] { "u" }, trace, priority: int.MinValue));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("极大", new[] { "u" }, trace, priority: int.MaxValue));

        await engine.Emit("u");
        Assert.Equal(new[] { "极小", "自定义", "极大" }, trace); // 数值小者先；任意 int（含极小/极大）均可排序
    }

    // ---------- 失败记录+继续 ----------

    [Fact]
    public async Task Emit_Event_Exception_Is_Isolated_And_Broadcast_Continues_Without_Double_Record()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();

        var a = new Trigger<CounterView>(name: "A", kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<CounterView>("内爆", (v, c, ct) =>
            {
                trace.Add("A");
                throw new InvalidOperationException("业务爆炸");
            }),
        }, hooks: new[] { "u" });
        var b = BusTestHelpers.RecordingPassive("B", new[] { "u" }, trace);
        engine.Bus.Mount(a);
        engine.Bus.Mount(b);

        await engine.Emit("u");

        Assert.Equal(new[] { "A", "B" }, trace); // 隔离后继续后续订阅者

        var root = engine.RootStream;
        // A 的事件内异常：S2 内部隔离记录（入 A 自己的流、经冒泡可达总流）；source＝"A/内爆"（触发器名/事件名）
        Assert.Equal(1, root.Entries.Count(e => e.Source == "A/内爆" && e.Level == LogLevel.Error));
        Assert.Contains(root.Entries, e => e.Keywords.Contains("exception:InvalidOperationException"));
        // 总线层不重复记录同一失败：不存在 source 恰为展示名 "A" 的错误条目（那是总线层记录的形态）
        Assert.DoesNotContain(root.Entries, e => e.Level == LogLevel.Error && e.Source == "A");
    }

    [Fact]
    public async Task Emit_Binding_Failure_Is_Recorded_With_Display_Name_And_Broadcast_Continues()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();

        // B：命名触发器，载荷缺必填 Card → 绑定失败（入口阶段）
        var b = new Trigger<BusPayloadView>(name: "B", kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<BusPayloadView>("不应执行", (v, c, ct) => { trace.Add("B"); return Task.CompletedTask; }),
        }, hooks: new[] { "u" });

        // 未命名：绑定失败 → source 退化视图类型名
        var unnamed = new Trigger<BusPayloadView>(kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<BusPayloadView>("不应执行", (v, c, ct) => { trace.Add("unnamed"); return Task.CompletedTask; }),
        }, hooks: new[] { "u" });

        var c = BusTestHelpers.RecordingPassive("C", new[] { "u" }, trace);
        engine.Bus.Mount(b);
        engine.Bus.Mount(unnamed);
        engine.Bus.Mount(c);

        await engine.Emit("u", new Dictionary<string, object?> { ["Amount"] = 1 }); // 无 Card

        Assert.Equal(new[] { "C" }, trace); // B/未命名未执行；C 继续（广播继续）

        var root = engine.RootStream;
        var errors = root.Entries.Where(e => e.Level == LogLevel.Error).ToArray();
        Assert.Equal(2, errors.Length); // 各恰一条（互不重复）

        var bRecord = errors.Single(e => e.Source == "B"); // source＝触发器展示名（纯展示名，无事件上下文）
        Assert.Contains("exception:KeyNotFoundException", bRecord.Keywords);

        var uRecord = errors.Single(e => e.Source == "BusPayloadView"); // 未命名退化标识
        Assert.Contains("exception:KeyNotFoundException", uRecord.Keywords);

        // ---- 受控变更增补（触发器验证·契约强化）：新协议断言 ----
        // ① 记录改由触发器本流写入（契约兜底）、经冒泡于 root 可见——观察锚点保持：
        //    root 本地不含 Error（该场景不再经总线捕获路径记录；“恰 2 条、不重复”由上方 errors 断言承担）。
        Assert.DoesNotContain(root.LocalEntries, e => e.Level == LogLevel.Error);

        // ② 失败子流带失败标记（契约兜底失败）：B 与未命名各恰一个；记录归属各自本流（LocalEntries）。
        var failureStreams = root.Children.Where(s => s.Outcome == ExecutionOutcome.ContractFailure).ToArray();
        Assert.Equal(2, failureStreams.Length);

        var bStream = failureStreams.Single(s => s.LocalEntries.Any(e => e.Source == "B" && e.Level == LogLevel.Error));
        Assert.Contains(bStream.LocalEntries, e => e.Source == "B" && e.Keywords.Contains("exception:KeyNotFoundException"));

        var unnamedStream = failureStreams.Single(
            s => s.LocalEntries.Any(e => e.Source == "BusPayloadView" && e.Level == LogLevel.Error));
        Assert.Contains(unnamedStream.LocalEntries, e => e.Source == "BusPayloadView" && e.Keywords.Contains("exception:KeyNotFoundException"));
    }

    // ---------- 取消语义 ----------

    [Fact]
    public async Task Emit_With_Cancelled_Token_Throws_Before_Entry_And_Broadcast()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("T", new[] { "u" }, trace));
        var baseline = engine.RootStream.Entries.Count;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.Emit("u", null, cts.Token));

        Assert.Equal(baseline, engine.RootStream.Entries.Count); // 不写条目
        Assert.Empty(trace); // 不广播
    }

    [Fact]
    public async Task Emit_Subscriber_Cancellation_Stops_Remaining_And_Propagates()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var a = new Trigger<CounterView>(name: "A", kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<CounterView>("取消", (v, c, ct) => throw new OperationCanceledException("广播中取消")),
        }, hooks: new[] { "u" });
        var b = BusTestHelpers.RecordingPassive("B", new[] { "u" }, trace);
        engine.Bus.Mount(a);
        engine.Bus.Mount(b);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.Emit("u"));

        Assert.DoesNotContain("B", trace); // 停止后续订阅者、原样向上穿透
    }

    // ---------- 快照语义与中断空转 ----------

    [Fact]
    public async Task Emit_Snapshot_Semantics_Unmount_During_Broadcast_Does_Not_Affect_Current_Round()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var ownerB = new object();
        var b = BusTestHelpers.RecordingPassive("B", new[] { "u" }, trace, owner: ownerB);
        var a = new Trigger<CounterView>(name: "A", kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<CounterView>("卸载B", (v, c, ct) =>
            {
                trace.Add("A");
                engine.Bus.UnmountOwner(ownerB);
                return Task.CompletedTask;
            }),
        }, hooks: new[] { "u" });
        engine.Bus.Mount(a);
        engine.Bus.Mount(b);
        Assert.Equal(new[] { "A", "B" }, engine.Bus.GetSubscribers("u"));

        await engine.Emit("u");
        Assert.Equal(new[] { "A", "B" }, trace); // B 在本轮快照中：虽已被卸载、本轮仍执行

        trace.Clear();
        await engine.Emit("u");
        Assert.Equal(new[] { "A" }, trace); // 下一轮：B 不复存在

        engine.Bus.Mount(b); // 重新挂载恢复
        trace.Clear();
        await engine.Emit("u");
        Assert.Equal(new[] { "A", "B" }, trace);
    }

    [Fact]
    public async Task Emit_On_Interrupted_Chain_Idles_Subscribers_But_Writes_Entry()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("T", new[] { "u" }, trace));
        var order = new List<string>();

        var killer = new Trigger<CounterView>(name: "中断者", events: new[]
        {
            new TriggerEvent<CounterView>("中断并广播", async (v, c, ct) =>
            {
                c.Interrupt();
                await engine.Emit("u");
                order.Add("after-emit");
            }),
            new TriggerEvent<CounterView>("不应执行", (v, c, ct) => { order.Add("skipped"); return Task.CompletedTask; }),
        });

        var killerStream = await killer.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Empty(trace); // 已中断链：订阅者执行空转（正常返回）
        Assert.Equal(new[] { "after-emit" }, order);
        Assert.Contains(killerStream.Entries, e => e.Kind == LogEntryKind.Update && e.Message == "u"); // 更新条目仍写（发射者流）
    }
}
