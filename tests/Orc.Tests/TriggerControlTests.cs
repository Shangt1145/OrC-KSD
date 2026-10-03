using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点⑤：Stop（仅本级：停本触发器剩余事件、不影响父层、已触发子执行照常完成）与
/// Interrupt（链级：沿嵌套链传播、事件边界检查、已中断状态下新执行空转、最外层正常返回并可查状态）。
/// </summary>
public class TriggerControlTests
{
    [Fact]
    public async Task Stop_Stops_Current_Trigger_Remainder_But_Not_Parent()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        Context? ctxX = null;
        Context? ctxP = null;
        EventStream? xStream = null;

        var x = new Trigger<CounterView>(name: "X", events: new[]
        {
            new TriggerEvent<CounterView>("x1", (v, c, t) =>
            {
                ctxX = c;
                order.Add("x1");
                c.Stop();
                order.Add("x1-after");
                return Task.CompletedTask;
            }),
            new TriggerEvent<CounterView>("x2", (v, c, t) => { order.Add("x2"); return Task.CompletedTask; }),
        });

        var p = new Trigger<CounterView>(name: "P", events: new[]
        {
            new TriggerEvent<CounterView>("p1", async (v, c, t) =>
            {
                ctxP = c;
                xStream = await x.InvokeAsync(engine, new Dictionary<string, object?>());
                order.Add("p1-after");
            }),
            new TriggerEvent<CounterView>("p2", (v, c, t) => { order.Add("p2"); return Task.CompletedTask; }),
        });

        await p.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "x1", "x1-after", "p1-after", "p2" }, order); // x2 未启动（事件边界检查）；父层照常
        Assert.True(ctxX!.Stopped);           // 状态可查（与检查点同一状态源）
        Assert.False(ctxX.Interrupted);
        Assert.False(ctxP!.Stopped);          // 仅本级：不影响父层
        Assert.False(ctxP.Interrupted);
        Assert.NotNull(xStream);              // 已触发的子执行正常完成并返回流
        Assert.Contains(xStream!.Entries, e => e.Keywords.Contains("stop")); // 停止行为写入事件流（本级流）
    }

    [Fact]
    public async Task Stop_Does_Not_Affect_Already_Triggered_Sub_Execution()
    {
        var engine = new LogicEngine();
        var order = new List<string>();

        var y = new Trigger<CounterView>(name: "Y", events: new[]
        {
            new TriggerEvent<CounterView>("y1", async (v, c, t) => { await Task.Yield(); order.Add("y1"); }),
            new TriggerEvent<CounterView>("y2", (v, c, t) => { order.Add("y2"); return Task.CompletedTask; }),
        });

        var x = new Trigger<CounterView>(name: "X", events: new[]
        {
            new TriggerEvent<CounterView>("x1", async (v, c, t) =>
            {
                var yTask = y.InvokeAsync(engine, new Dictionary<string, object?>()); // Y 已触发（in-flight）
                c.Stop();                                                             // 仅停 X 本级
                await yTask;                                                          // Y 照常完成
                order.Add("x1-after");
            }),
            new TriggerEvent<CounterView>("x2", (v, c, t) => { order.Add("x2"); return Task.CompletedTask; }),
        });

        await x.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "y1", "y2", "x1-after" }, order); // Y 完整执行；X 剩余事件 x2 未执行
    }

    [Fact]
    public async Task Interrupt_Propagates_Along_Chain_And_Stops_All_Layers_At_Event_Boundaries()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        Context? ctxR = null;
        Context? ctxS = null;
        Context? ctxT = null;

        var t = new Trigger<CounterView>(name: "T", events: new[]
        {
            new TriggerEvent<CounterView>("t1", (v, c, tt) => { ctxT = c; c.Interrupt(); order.Add("t1-after"); return Task.CompletedTask; }),
            new TriggerEvent<CounterView>("t2", (v, c, tt) => { order.Add("t2"); return Task.CompletedTask; }),
        });
        var s = new Trigger<CounterView>(name: "S", events: new[]
        {
            new TriggerEvent<CounterView>("s1", async (v, c, tt) => { ctxS = c; await t.InvokeAsync(engine, new Dictionary<string, object?>()); order.Add("s1-after"); }),
            new TriggerEvent<CounterView>("s2", (v, c, tt) => { order.Add("s2"); return Task.CompletedTask; }),
        });
        var r = new Trigger<CounterView>(name: "R", events: new[]
        {
            new TriggerEvent<CounterView>("r1", async (v, c, tt) => { ctxR = c; await s.InvokeAsync(engine, new Dictionary<string, object?>()); order.Add("r1-after"); }),
            new TriggerEvent<CounterView>("r2", (v, c, tt) => { order.Add("r2"); return Task.CompletedTask; }),
        });

        var streamR = await r.InvokeAsync(engine, new Dictionary<string, object?>()); // 最外层正常返回流（不外抛）

        // ①执行到哪：in-flight 事件跑完（t1-after / s1-after / r1-after）
        Assert.Equal(new[] { "t1-after", "s1-after", "r1-after" }, order);
        // ②状态：链上各层 Interrupted 可见/可查（含最外层根执行 ctx）
        Assert.True(ctxT!.Interrupted);
        Assert.True(ctxS!.Interrupted);
        Assert.True(ctxR!.Interrupted);
        // ③剩余事件未执行（副作用缺失）
        Assert.DoesNotContain("t2", order);
        Assert.DoesNotContain("s2", order);
        Assert.DoesNotContain("r2", order);

        Assert.NotNull(streamR);
        // 中断行为写入事件流并冒泡至总流：
        Assert.Contains(engine.RootStream.Entries, e => e.Keywords.Contains("interrupt") && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Interrupt_From_Deeper_Running_Sub_Layer_Stops_Sub_Layers_Too()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        Context? ctxC = null;

        var d = new Trigger<CounterView>(name: "D", events: new[]
        {
            new TriggerEvent<CounterView>("d1", (v, c, tt) =>
            {
                ctxC!.Interrupt(); // 起点＝C；D 为 C 之下正在运行的子层——各层含子层，同样停止
                order.Add("d1-after");
                return Task.CompletedTask;
            }),
            new TriggerEvent<CounterView>("d2", (v, c, tt) => { order.Add("d2"); return Task.CompletedTask; }),
        });
        var c = new Trigger<CounterView>(name: "C", events: new[]
        {
            new TriggerEvent<CounterView>("c1", async (v, cc, tt) =>
            {
                ctxC = cc;
                await d.InvokeAsync(engine, new Dictionary<string, object?>());
                order.Add("c1-after");
            }),
            new TriggerEvent<CounterView>("c2", (v, cc, tt) => { order.Add("c2"); return Task.CompletedTask; }),
        });

        var streamC = await c.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "d1-after", "c1-after" }, order); // in-flight 完成；D 与 C 剩余事件均在事件边界停止
        Assert.True(ctxC!.Interrupted);                        // 链上各层（含起点之下的运行中子层）状态可查
        Assert.NotNull(streamC);
    }

    [Fact]
    public async Task Sub_Execution_Started_After_Interrupt_Idles_With_Empty_Stream_Mounted()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        EventStream? idleStream = null;

        var u = new Trigger<CounterView>(name: "U", events: new[]
        {
            new TriggerEvent<CounterView>("u1", (v, c, t) => { order.Add("u1"); return Task.CompletedTask; }),
        });

        var t = new Trigger<CounterView>(name: "T", events: new[]
        {
            new TriggerEvent<CounterView>("t1", async (v, c, tt) =>
            {
                c.Interrupt();
                idleStream = await u.InvokeAsync(engine, new Dictionary<string, object?>()); // 已中断：新执行空转
                order.Add("t1-after");
            }),
            new TriggerEvent<CounterView>("t2", (v, c, tt) => { order.Add("t2"); return Task.CompletedTask; }),
        });

        var streamT = await t.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "t1-after" }, order); // in-flight 完成；U 未执行任何事件、T 剩余事件未启动
        Assert.NotNull(idleStream);
        Assert.Empty(idleStream!.Entries);         // 空流
        Assert.Same(streamT, idleStream.Parent);   // 仍遵守挂载统一规则（自动挂载到触发者流）
        Assert.Contains(idleStream, streamT.Children);
        // 空转的 attach 记录（含父子流 ID）：
        var attach = streamT.Entries.Single(
            e => e.Kind == LogEntryKind.Attach && Equals(e.Data["childId"], idleStream.Id));
        Assert.Equal(streamT.Id, attach.Data["parentId"]);
    }
}
