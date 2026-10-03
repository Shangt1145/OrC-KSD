using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点⑦：异常隔离——业务异常/越权/失效被捕获、记录（写入事件流）、继续后续事件；
/// 取消类异常（OperationCanceledException 及派生）不隔离、穿透上抛；已写入条目保留。
/// </summary>
public class ExceptionIsolationTests
{
    [Fact]
    public async Task Business_Exception_Is_Isolated_Recorded_And_Execution_Continues()
    {
        var engine = new LogicEngine();
        var order = new List<string>();

        var trigger = new Trigger<CounterView>(name: "隔离", events: new[]
        {
            new TriggerEvent<CounterView>("炸", (v, c, t) => { order.Add("boom"); throw new InvalidOperationException("业务故障"); }),
            new TriggerEvent<CounterView>("续", (v, c, t) => { order.Add("after"); return Task.CompletedTask; }),
        });

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "boom", "after" }, order); // 捕获后继续后续事件
        Assert.NotNull(stream);

        var record = stream.Entries.Single(e => e.Keywords.Contains("exception:InvalidOperationException"));
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(LogEntryKind.Log, record.Kind);
        Assert.Contains("业务故障", record.Message, StringComparison.Ordinal); // 异常可读摘要
        Assert.Equal("隔离/炸", record.Source);                                 // 触发器名/事件名
        Assert.Equal(typeof(InvalidOperationException).FullName, record.Data["exceptionType"]);
    }

    [Fact]
    public async Task Listed_Exception_Types_PermissionDenied_And_StaleReference_Are_Isolated()
    {
        var engine = new LogicEngine();
        var order = new List<string>();

        var trigger = new Trigger<CounterView>(name: "列举", events: new[]
        {
            new TriggerEvent<CounterView>("越权", (v, c, t) => throw new PermissionDeniedException("无权访问")),
            new TriggerEvent<CounterView>("失效", (v, c, t) => throw new StaleReferenceException("引用已死")),
            new TriggerEvent<CounterView>("续", (v, c, t) => { order.Add("done"); return Task.CompletedTask; }),
        });

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "done" }, order);
        Assert.Contains(stream.Entries, e => e.Keywords.Contains("exception:PermissionDeniedException"));
        Assert.Contains(stream.Entries, e => e.Keywords.Contains("exception:StaleReferenceException"));
        Assert.Equal(2, stream.Entries.Count(e => e.Level == LogLevel.Error));
    }

    [Fact]
    public async Task Cancellation_Exception_Pierces_Without_Isolation_And_Remainder_Is_Skipped()
    {
        var engine = new LogicEngine();
        var order = new List<string>();

        var trigger = new Trigger<CounterView>(name: "取消", events: new[]
        {
            new TriggerEvent<CounterView>("执行到", (v, c, t) =>
            {
                order.Add("executed");
                throw new TaskCanceledException("取消");
            }),
            new TriggerEvent<CounterView>("不应执行", (v, c, t) => { order.Add("skipped"); return Task.CompletedTask; }),
        });

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => trigger.InvokeAsync(engine, new Dictionary<string, object?>()));

        Assert.IsType<TaskCanceledException>(ex); // 派生取消类同样不隔离（is OperationCanceledException 判定）
        Assert.Equal(new[] { "executed" }, order); // 剩余事件终止

        // 已写入条目保留（取消上抛不要求封口/补全记录）；且不被隔离记录：
        Assert.Contains(engine.RootStream.Entries, e => e.Kind == LogEntryKind.Attach);
        Assert.DoesNotContain(engine.RootStream.Entries, e => e.Level == LogLevel.Error);
        Assert.DoesNotContain(
            engine.RootStream.Entries,
            e => e.Keywords.Any(k => k.StartsWith("exception:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Cancellation_In_Nested_Execution_Pierces_All_Layers()
    {
        var engine = new LogicEngine();
        var order = new List<string>();

        var child = new Trigger<CounterView>(name: "子", events: new[]
        {
            new TriggerEvent<CounterView>("子炸", (v, c, t) => throw new OperationCanceledException("取消令牌")),
        });
        var parent = new Trigger<CounterView>(name: "父", events: new[]
        {
            new TriggerEvent<CounterView>("父1", async (v, c, t) =>
            {
                await child.InvokeAsync(engine, new Dictionary<string, object?>());
                order.Add("父1续");
            }),
            new TriggerEvent<CounterView>("父2", (v, c, t) => { order.Add("父2"); return Task.CompletedTask; }),
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => parent.InvokeAsync(engine, new Dictionary<string, object?>()));

        Assert.Empty(order); // 子层 OCE 经父层调用点直接穿透：父层剩余事件同样不执行
    }
}
