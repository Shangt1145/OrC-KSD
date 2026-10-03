using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>验收点④：共享性（同一触发器的多次执行共享同一 Data 载体）+ 最小触发器壳约束证据 + 防御与传播。</summary>
public class TriggerSharedContextTests
{
    [Fact]
    public async Task Same_Context_Shared_Across_Runs_Of_Same_Trigger()
    {
        var ctx = new Context(); // 空载体：ctx 全事件/跨执行共享同一载体
        var marker = new object();
        var run = 0;
        CounterView? firstView = null;
        CounterView? secondView = null;

        var trigger = new Trigger<CounterView>(view =>
        {
            run++;
            if (run == 1)
            {
                firstView = view;
                Assert.Equal(0, view.Count); // [Optional] 缺省 → default
                view.Count = 1;              // 写入（创建新键）
                view.Payload = marker;
            }
            else
            {
                secondView = view;
                Assert.Equal(1, view.Count);       // 跨执行可见：第一次执行的改写（共享而非拷贝）
                Assert.Same(marker, view.Payload); // 同一对象引用，未经拷贝/序列化
                view.Count = 2;
            }

            return Task.CompletedTask;
        });

        await trigger.RunAsync(ctx);
        Assert.Equal(1, (int)ctx.Get("Count")!); // 视图改写经 Context 载体可见

        await trigger.RunAsync(ctx);
        Assert.Equal(2, (int)ctx.Get("Count")!); // 第二次执行继续基于同一载体

        Assert.True(ctx.TryGet("Payload", out var payload));
        Assert.Same(marker, payload);

        Assert.NotNull(firstView);
        Assert.NotNull(secondView);
        // 两次执行均经同一视图类型（泛型参数 TView 编译期固定）绑定
        Assert.IsAssignableFrom<CounterView>(firstView);
        Assert.IsAssignableFrom<CounterView>(secondView);
        // 每次执行会话新建视图实例（不复用）
        Assert.NotSame(firstView, secondView);
    }

    [Fact]
    public void Context_Copies_External_Dictionary_On_Construction()
    {
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
    public async Task RunAsync_Null_Context_Rejected()
    {
        var trigger = new Trigger<CounterView>(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<ArgumentNullException>(() => trigger.RunAsync(null!));
    }

    [Fact]
    public void Trigger_Null_Handler_Rejected()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = new Trigger<CounterView>(null!); });
    }

    [Fact]
    public async Task Binding_Failure_Skips_Handler_And_Propagates()
    {
        var executed = false;
        var trigger = new Trigger<ShieldView>(view =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        // 空上下文：Target / Source / Amount 均缺失 → 绑定失败
        await Assert.ThrowsAsync<KeyNotFoundException>(() => trigger.RunAsync(new Context()));

        Assert.False(executed); // 不产出视图、不执行执行委托
    }

    [Fact]
    public async Task Handler_Exception_Propagates_Unchanged()
    {
        var trigger = new Trigger<CounterView>(_ => throw new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => trigger.RunAsync(new Context()));

        Assert.Equal("boom", ex.Message);
    }
}
