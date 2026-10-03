using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点③⑥（入流与引擎壳）：更新条目位置与冒泡（边界/帧内/嵌套）；
/// 引擎壳（Bus 暴露稳定、Emit 转发等价、多实例独立、总流可达性）。
/// </summary>
public class BusStreamAndEngineTests
{
    // ---------- 验收③：更新写入事件流（条目位置与冒泡） ----------

    [Fact]
    public async Task Boundary_Emit_Writes_Update_Entry_To_Root_Before_Subscriber_Attach()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("T", new[] { "u" }, trace));
        var card = new Entity("卡");
        var payload = new Dictionary<string, object?> { ["Card"] = card.Ref, ["Amount"] = 3, ["Marker"] = "x" };

        await engine.Emit("u", payload);

        var root = engine.RootStream;
        var updateEntry = root.Entries.Single(e => e.Kind == LogEntryKind.Update);
        Assert.Equal("bus", updateEntry.Source);                     // source 固定 "bus"
        Assert.Equal("u", updateEntry.Message);                      // message＝更新类型字面值
        Assert.Equal(new[] { "u" }, updateEntry.Keywords);           // keywords 恰单元素（无附加词）
        Assert.Equal(LogLevel.Info, updateEntry.Level);
        Assert.Equal(card.Ref, updateEntry.Data["Card"]);            // 载荷键值原样
        Assert.Equal(3, (int)updateEntry.Data["Amount"]!);
        Assert.Equal("x", updateEntry.Data["Marker"]);

        // 订阅者执行流挂总流（引擎边界：无执行帧）
        var subscriberStream = root.Children.Single();
        Assert.Same(root, subscriberStream.Parent);

        // 顺序：总流上先 update 条目、再订阅者流 attach（时间序；mount 留痕在最前）
        Assert.Equal(
            new[] { LogEntryKind.Log, LogEntryKind.Update, LogEntryKind.Attach },
            root.Entries.Select(e => e.Kind).ToArray());

        Assert.Equal(new[] { "T" }, trace);
    }

    [Fact]
    public async Task In_Frame_Emit_Writes_Update_Entry_And_Attaches_Subscriber_Stream_To_Emitters_Stream()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("订阅", new[] { "inner" }, trace));

        var emitter = new Trigger<CounterView>(name: "发射者", events: new[]
        {
            new TriggerEvent<CounterView>("发射", async (v, c, ct) =>
            {
                await engine.Emit("inner", new Dictionary<string, object?> { ["Count"] = 1 });
            }),
        });

        var emitterStream = await emitter.InvokeAsync(engine, new Dictionary<string, object?>());

        // update 条目落发射者流（本地写入；非总流）
        var updateEntry = emitterStream.Entries.Single(e => e.Kind == LogEntryKind.Update);
        Assert.Equal("bus", updateEntry.Source);
        Assert.Equal("inner", updateEntry.Message);

        // 被动执行流挂发射者流（发射时的当前执行者流）
        var subscriberStream = emitterStream.Children.Single();
        Assert.Same(emitterStream, subscriberStream.Parent);

        // 发射者流顺序：update 先于 attach
        Assert.Equal(
            new[] { LogEntryKind.Update, LogEntryKind.Attach },
            emitterStream.Entries.Select(e => e.Kind).ToArray());

        // 冒泡：update 条目与执行流均可达总流
        Assert.Contains(engine.RootStream.Entries, e => ReferenceEquals(e, updateEntry));
        Assert.Same(emitterStream, engine.RootStream.Children.Single());
        Assert.Equal(new[] { "订阅" }, trace);
    }

    [Fact]
    public async Task Nested_Emit_Forms_Child_Tree_With_Bubbling()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();

        var p1 = new Trigger<CounterView>(name: "P1", kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<CounterView>("转发", async (v, c, ct) =>
            {
                trace.Add("P1");
                await engine.Emit("inner");
            }),
        }, hooks: new[] { "outer" });
        var p2 = BusTestHelpers.RecordingPassive("P2", new[] { "inner" }, trace);
        engine.Bus.Mount(p1);
        engine.Bus.Mount(p2);

        var emitter = new Trigger<CounterView>(name: "发射者", events: new[]
        {
            new TriggerEvent<CounterView>("发射", async (v, c, ct) =>
            {
                trace.Add("E");
                await engine.Emit("outer");
            }),
        });

        var emitterStream = await emitter.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "E", "P1", "P2" }, trace); // 嵌套 Emit 沿执行帧链自动成树

        // emitter → Emit(outer)：update(outer) 落发射者流 + P1 流挂发射者流
        var p1Stream = emitterStream.Children.Single();
        Assert.Contains(emitterStream.Entries, e => e.Kind == LogEntryKind.Update && e.Message == "outer");

        // P1 → Emit(inner)：update(inner) 落 P1 流 + P2 流挂 P1 流（各自按各自发射点捕获）
        var p2Stream = p1Stream.Children.Single();
        Assert.Same(p1Stream, p2Stream.Parent);
        var innerUpdate = p1Stream.Entries.Single(e => e.Kind == LogEntryKind.Update);
        Assert.Equal("inner", innerUpdate.Message);

        // 全部条目经冒泡可达总流
        var root = engine.RootStream;
        Assert.Contains(root.Entries, e => e.Kind == LogEntryKind.Update && e.Message == "outer");
        Assert.Contains(root.Entries, e => e.Kind == LogEntryKind.Update && e.Message == "inner");
    }

    [Fact]
    public async Task Update_Entry_Data_Is_Empty_When_Payload_Is_Null_And_No_Subscribers()
    {
        var engine = new LogicEngine();
        await engine.Emit("nothing.happened");        // 无订阅者：仍写条目（因果记录完整）
        await engine.Emit("nothing.happened", null);

        var updates = engine.RootStream.Entries.Where(e => e.Kind == LogEntryKind.Update).ToArray();
        Assert.Equal(2, updates.Length);
        Assert.All(updates, e => Assert.Empty(e.Data));                        // payload=null → 空字典
        Assert.All(updates, e => Assert.Equal(new[] { "nothing.happened" }, e.Keywords));
        Assert.DoesNotContain(engine.RootStream.Entries, e => e.Kind == LogEntryKind.Attach); // 无订阅者流
    }

    // ---------- 验收⑥：引擎壳（总线暴露、总流联动） ----------

    [Fact]
    public void Engine_Bus_Is_Reachable_Stable_And_Multi_Instance()
    {
        var engine = new LogicEngine();
        Assert.NotNull(engine.Bus);
        Assert.Same(engine.Bus, engine.Bus); // 同一引擎两次访问同实例（公开只读、不可替换）

        var other = new LogicEngine();
        Assert.NotSame(engine.Bus, other.Bus); // 多实例、无单例
        Assert.NotSame(engine.RootStream, other.RootStream);
    }

    [Fact]
    public async Task Engine_Emit_Is_Equivalent_To_Bus_Emit()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("T1", new[] { "a" }, trace));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("T2", new[] { "b" }, trace));

        await engine.Emit("a");     // 经引擎便捷转发
        await engine.Bus.Emit("b"); // 经总线直调

        Assert.Equal(new[] { "T1", "T2" }, trace);

        var updates = engine.RootStream.Entries
            .Where(e => e.Kind == LogEntryKind.Update)
            .Select(e => e.Message)
            .ToArray();
        Assert.Equal(new[] { "a", "b" }, updates); // 两路径均写总流（边界发射的入流目标流判定一致）
        Assert.Equal(2, engine.RootStream.Children.Count);
        Assert.All(engine.RootStream.Children, s => Assert.Same(engine.RootStream, s.Parent));
    }

    [Fact]
    public async Task Multi_Engine_Instances_Are_Independent()
    {
        var a = new LogicEngine();
        var b = new LogicEngine();
        var traceA = new List<string>();
        var traceB = new List<string>();
        a.Bus.Mount(BusTestHelpers.RecordingPassive("A1", new[] { "u" }, traceA));
        b.Bus.Mount(BusTestHelpers.RecordingPassive("B1", new[] { "u" }, traceB));

        await a.Emit("u");
        Assert.Equal(new[] { "A1" }, traceA);
        Assert.Empty(traceB); // A 的广播不影响 B（订阅表/广播/总流均不共享）

        await b.Emit("u");
        Assert.Equal(new[] { "B1" }, traceB);

        Assert.Equal(new[] { "A1" }, a.Bus.GetSubscribers("u"));
        Assert.Equal(new[] { "B1" }, b.Bus.GetSubscribers("u"));
        Assert.Equal(1, a.RootStream.Entries.Count(e => e.Kind == LogEntryKind.Update));
        Assert.Equal(1, b.RootStream.Entries.Count(e => e.Kind == LogEntryKind.Update));
    }

    [Fact]
    public async Task Root_Stream_Reaches_All_Entries_And_Streams_By_Bubbling()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var t = new Trigger<CounterView>(name: "T", kind: TriggerKind.Passive, events: new[]
        {
            new TriggerEvent<CounterView>("再发射", async (v, c, ct) => await engine.Emit("inner")),
        }, hooks: new[] { "outer" });
        var innerSub = BusTestHelpers.RecordingPassive("Inner", new[] { "inner" }, trace);
        engine.Bus.Mount(t);
        engine.Bus.Mount(innerSub);

        await engine.Emit("outer");

        var root = engine.RootStream;
        // 条目完备：update(outer)（本地）与 update(inner)（经 T 流冒泡）均可达
        Assert.Contains(root.Entries, e => e.Kind == LogEntryKind.Update && e.Message == "outer");
        Assert.Contains(root.Entries, e => e.Kind == LogEntryKind.Update && e.Message == "inner");
        Assert.Equal(2, root.Entries.Count(e => e.Kind == LogEntryKind.Update));

        // 子流完备：T 流挂总流；Inner 流挂 T 流（完整枚举）
        var tStream = root.Children.Single();
        var innerStream = tStream.Children.Single();
        Assert.Same(root, tStream.Parent);
        Assert.Same(tStream, innerStream.Parent);

        // attach 条目父子 ID 链可核对
        var attachT = root.Entries.Single(e => e.Kind == LogEntryKind.Attach && Equals(e.Data["childId"], tStream.Id));
        Assert.Equal(root.Id, attachT.Data["parentId"]);
        var attachInner = tStream.Entries.Single(e => e.Kind == LogEntryKind.Attach && Equals(e.Data["childId"], innerStream.Id));
        Assert.Equal(tStream.Id, attachInner.Data["parentId"]);

        Assert.Equal(new[] { "Inner" }, trace);
    }
}
