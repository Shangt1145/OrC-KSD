using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// S1→S2 迁移：原「共享性（跨执行共享 Data 载体）」语义按 S2 形态重表述为
/// 「同一执行会话内视图与全部事件共用同一 ctx 载体（Bind 直连、不拷贝）」；跨执行改为每次执行新建 ctx。
/// 保留既有验收语义覆盖（共享 / 绑定失败 / 防御）；文件保留、内容适配。
/// 受控变更（触发器验证·契约强化）：绑定失败由「原样传播」迁移为「捕获处理＋失败标记＋安全结束」。
/// </summary>
public class TriggerSharedContextTests
{
    private static Task Nop(CounterView view, Context ctx, CancellationToken ct) => Task.CompletedTask;

    [Fact]
    public async Task Same_Context_Carrier_Shared_Within_One_Execution()
    {
        var engine = new LogicEngine();
        var marker = new object();
        Context? first = null;
        Context? second = null;
        CounterView? firstView = null;
        CounterView? secondView = null;

        var trigger = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("第一次", (view, ctx, ct) =>
            {
                first = ctx;
                firstView = view;
                Assert.Equal(0, view.Count); // [Optional] 缺省 → default
                view.Count = 1;              // 写入（创建新键）
                view.Payload = marker;
                return Task.CompletedTask;
            }),
            new TriggerEvent<CounterView>("第二次", (view, ctx, ct) =>
            {
                second = ctx;
                secondView = view;
                Assert.Equal(1, view.Count);       // 同一执行会话内：改写对全部事件可见（直连、不拷贝）
                Assert.Same(marker, view.Payload); // 同一对象引用，未经拷贝/序列化
                view.Count = 2;
                return Task.CompletedTask;
            }),
        });

        await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Same(first, second);                  // 视图与全部事件共用同一 ctx 载体
        Assert.Same(firstView, secondView);          // 一次执行（整条事件链）＝一个视图实例（与 ctx 同构）
        Assert.Equal(2, (int)second!.Get("Count")!); // 视图改写经 Context 载体可见
        Assert.Same(marker, second.Get("Payload"));
    }

    [Fact]
    public async Task Each_Execution_Uses_Fresh_Context_And_View()
    {
        var engine = new LogicEngine();
        Context? c1 = null;
        Context? c2 = null;
        CounterView? v1 = null;
        CounterView? v2 = null;

        var trigger = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("计数", (view, ctx, ct) =>
            {
                if (c1 is null)
                {
                    c1 = ctx;
                    v1 = view;
                }
                else
                {
                    c2 = ctx;
                    v2 = view;
                }

                view.Count += 1;
                return Task.CompletedTask;
            }),
        });

        await trigger.InvokeAsync(engine, new Dictionary<string, object?> { ["Count"] = 10 });
        await trigger.InvokeAsync(engine, new Dictionary<string, object?> { ["Count"] = 10 });

        Assert.NotSame(c1, c2);                   // 每次执行新建 ctx（跨执行不共享载体）
        Assert.NotSame(v1, v2);                   // 每次执行会话新建视图实例（不复用）
        Assert.Equal(11, (int)c1!.Get("Count")!); // 各自从各自入料出发，互不串扰
        Assert.Equal(11, (int)c2!.Get("Count")!);
    }

    [Fact]
    public void Context_Copies_External_Dictionary_On_Construction()
    {
        // S1 原用例保留：Context 构造「入料拷贝一次」语义不变
        var external = new Dictionary<string, object?> { ["Count"] = 1 };
        var ctx = new Context(external);

        external["Count"] = 99; // 外部后续修改不影响已创建的上下文

        Assert.Equal(1, (int)ctx.Get("Count")!);

        var view = ContextViewBinder.Create<CounterView>(ctx);
        view.Count = 5; // 上下文内改写不回写外部字典

        Assert.Equal(5, (int)ctx.Get("Count")!);
        Assert.Equal(99, (int)external["Count"]!);
    }

    [Fact]
    public async Task InvokeAsync_Null_Engine_Rejected()
    {
        var trigger = new Trigger<CounterView>(events: new[] { new TriggerEvent<CounterView>("x", Nop) });

        await Assert.ThrowsAsync<ArgumentNullException>(() => trigger.InvokeAsync(null!));
    }

    [Fact]
    public void Register_Null_Handler_Rejected()
    {
        var trigger = new Trigger<CounterView>();

        Assert.Throws<ArgumentNullException>(() => trigger.Register("x", null!));
    }

    [Fact]
    public void Event_Collection_Null_Item_Rejected_And_Duplicate_Names_Allowed()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Trigger<CounterView>(events: new TriggerEvent<CounterView>[] { null! }));

        var trigger = new Trigger<CounterView>();
        trigger.Register("同名", Nop);
        trigger.Register("同名", Nop); // 事件名允许重复：不承担唯一键职责
    }

    [Fact]
    public async Task Binding_Failure_Is_Captured_Recorded_And_Ends_Safely_With_Failure_Mark()
    {
        // 受控变更（触发器验证·契约强化）：原「绑定失败原样传播、不写记录」→
        // 新「绑定失败被捕获处理：记录（与隔离记录同构）＋失败标记＋安全结束（返回流）」。
        var engine = new LogicEngine();
        var executed = false;
        var trigger = new Trigger<SampleView>(events: new[]
        {
            new TriggerEvent<SampleView>("x", (v, c, t) => { executed = true; return Task.CompletedTask; }),
        });

        // 空数据：Target / Source / Amount 均缺失 → 绑定失败（执行前）→ 捕获处理、安全结束（不外传）
        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.False(executed);                        // 不产出视图、不执行事件
        Assert.Same(engine.RootStream, stream.Parent); // 返回流（正常挂载）

        // 契约兜底记录（与隔离记录同构）：Error 级；source＝触发器展示名（未命名 → 视图类型名退化）；
        // keywords 含 exception:{类型名}；data 含 exceptionType / message。
        var record = Assert.Single(stream.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal(LogEntryKind.Log, record.Kind);
        Assert.Contains("exception:KeyNotFoundException", record.Keywords);
        Assert.Equal("SampleView", record.Source);
        Assert.Equal(typeof(KeyNotFoundException).FullName, record.Data["exceptionType"]);

        // 失败标记：契约兜底失败
        Assert.Equal(ExecutionOutcome.ContractFailure, stream.Outcome);
    }

    [Fact]
    public async Task Empty_Trigger_Produces_And_Mounts_Empty_Stream()
    {
        var engine = new LogicEngine();
        var trigger = new Trigger<CounterView>(name: "空");

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.NotNull(stream);
        Assert.Same(engine.RootStream, stream.Parent);
        Assert.Contains(stream, engine.RootStream.Children);
        Assert.Empty(stream.Entries); // 无事件、无写入
    }

    [Fact]
    public void Context_Stop_And_Interrupt_Outside_Execution_Session_Are_NoOps()
    {
        var ctx = new Context(); // 未关联执行会话（框架执行路径之外）

        ctx.Stop();
        ctx.Interrupt();

        Assert.False(ctx.Stopped);
        Assert.False(ctx.Interrupted);
    }
}
