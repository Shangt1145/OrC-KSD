using Orc.Core;
using Orc.Output;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// UI 消费桥接验收（S1–S5、S8）：动作作用域产段、嵌套合并、异常仍产段、零发射；
/// 段内容（含报错）、段号单调、取段语义；即时更新口（非阻塞、异常隔离、取消）；两通道并存。
/// </summary>
public class SegmentBridgeTests
{
    // ---------- S1/S2/S3/S5：段产出 ----------

    [Fact]
    public async Task ActionScope_Produces_Segment_Containing_Update()
    {
        var engine = new LogicEngine();

        {
            await using var scope = engine.BeginAction();
            await engine.Emit("demo.acted");
        }

        var segment = Assert.Single(engine.TakeSegments());
        Assert.Equal(1, segment.Sequence);
        Assert.Contains(segment.Entries, e => e.Kind == LogEntryKind.Update && e.Keywords.Contains("demo.acted"));
    }

    [Fact]
    public async Task Nested_Scopes_Produce_Single_Segment()
    {
        var engine = new LogicEngine();

        {
            await using var outer = engine.BeginAction();
            await engine.Emit("outer.before");
            {
                await using var inner = engine.BeginAction();
                await engine.Emit("inner");
            }

            await engine.Emit("outer.after");
        }

        var segment = Assert.Single(engine.TakeSegments());
        Assert.Contains(segment.Entries, e => e.Keywords.Contains("outer.before"));
        Assert.Contains(segment.Entries, e => e.Keywords.Contains("inner"));
        Assert.Contains(segment.Entries, e => e.Keywords.Contains("outer.after"));
    }

    [Fact]
    public async Task Exception_Still_Produces_Segment_With_What_Happened()
    {
        var engine = new LogicEngine();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var scope = engine.BeginAction();
            await engine.Emit("acted.before.fail");
            throw new InvalidOperationException("boom");
        });

        var segment = Assert.Single(engine.TakeSegments()); // 异常仍产出"已发生部分"
        Assert.Contains(segment.Entries, e => e.Keywords.Contains("acted.before.fail"));
    }

    [Fact]
    public async Task Emit_Without_Scope_Produces_No_Segment()
    {
        var engine = new LogicEngine();
        await engine.Emit("loose");
        Assert.Empty(engine.TakeSegments());
    }

    [Fact]
    public async Task Empty_Scope_Produces_No_Segment()
    {
        var engine = new LogicEngine();

        {
            await using var scope = engine.BeginAction(); // 无任何信号（零发射）
        }

        Assert.Empty(engine.TakeSegments());
    }

    [Fact]
    public async Task Segment_Sequence_Is_Monotonic_Per_Engine()
    {
        var engine = new LogicEngine();

        {
            await using var s1 = engine.BeginAction();
            await engine.Emit("a");
        }

        {
            await using var s2 = engine.BeginAction();
            await engine.Emit("b");
        }

        var segments = engine.TakeSegments();
        Assert.Equal(2, segments.Count);
        Assert.Equal(1, segments[0].Sequence);
        Assert.Equal(2, segments[1].Sequence);
    }

    [Fact]
    public async Task Scope_Flows_Across_Await()
    {
        var engine = new LogicEngine();

        {
            await using var scope = engine.BeginAction();
            await Task.Yield();
            await engine.Emit("after.yield");
        }

        var segment = Assert.Single(engine.TakeSegments());
        Assert.Contains(segment.Entries, e => e.Keywords.Contains("after.yield"));
    }

    [Fact]
    public async Task Segment_Includes_Error_Entries()
    {
        var engine = new LogicEngine();
        using var failing = engine.Subscribe((t, p, c) => throw new InvalidOperationException("boom"));

        {
            await using var scope = engine.BeginAction();
            await engine.Emit("will.fail"); // 订阅者异常被隔离记录（Error）到发射者流
        }

        var segment = Assert.Single(engine.TakeSegments());
        Assert.Contains(segment.Entries, e => e.Level == LogLevel.Error); // 报错信息随段交付
    }

    // ---------- S4：取段语义 ----------

    [Fact]
    public async Task TakeSegments_Is_Fifo_And_Removes()
    {
        var engine = new LogicEngine();

        {
            await using var s1 = engine.BeginAction();
            await engine.Emit("a");
        }

        {
            await using var s2 = engine.BeginAction();
            await engine.Emit("b");
        }

        var taken = engine.TakeSegments();
        Assert.Equal(2, taken.Count);
        Assert.Contains(taken[0].Entries, e => e.Keywords.Contains("a"));
        Assert.Contains(taken[1].Entries, e => e.Keywords.Contains("b"));

        Assert.Empty(engine.TakeSegments()); // 取走即移除
    }

    [Fact]
    public async Task TryTakeSegment_Dequeues_One_At_A_Time()
    {
        var engine = new LogicEngine();

        {
            await using var scope = engine.BeginAction();
            await engine.Emit("a");
        }

        Assert.True(engine.TryTakeSegment(out var segment));
        Assert.Contains(segment.Entries, e => e.Keywords.Contains("a"));
        Assert.False(engine.TryTakeSegment(out _));
    }

    // ---------- S8：即时更新口 ----------

    [Fact]
    public async Task ImmediateUpdate_Receives_All_Updates_In_Order_With_Payload()
    {
        var engine = new LogicEngine();
        var seen = new List<string>();
        IReadOnlyDictionary<string, object?>? lastPayload = null;
        using var subscription = engine.OnImmediateUpdate((t, p) =>
        {
            seen.Add(t);
            lastPayload = p;
        });

        await engine.Emit("u1");
        await engine.Emit("u2", new Dictionary<string, object?> { ["k"] = 42 });

        Assert.Equal(new[] { "u1", "u2" }, seen);
        Assert.Equal(42, lastPayload!["k"]);
    }

    [Fact]
    public async Task ImmediateUpdate_Exception_Is_Isolated_And_Others_Continue()
    {
        var engine = new LogicEngine();
        var reached = 0;
        using var throwing = engine.OnImmediateUpdate((t, p) => throw new InvalidOperationException("x"));
        using var counting = engine.OnImmediateUpdate((t, p) => reached++);

        await engine.Emit("u"); // 不向调用方抛

        Assert.Equal(1, reached);
        Assert.Contains(
            engine.RootStream.Entries,
            e => e.Keywords.Contains("exception:InvalidOperationException")); // 隔离记录
    }

    [Fact]
    public async Task ImmediateUpdate_Dispose_Stops_And_Is_Idempotent()
    {
        var engine = new LogicEngine();
        var count = 0;
        var subscription = engine.OnImmediateUpdate((t, p) => count++);

        await engine.Emit("u1");
        subscription.Dispose();
        subscription.Dispose();

        await engine.Emit("u2");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ImmediateUpdate_And_Segment_Channels_Coexist()
    {
        var engine = new LogicEngine();
        var immediate = 0;
        using var subscription = engine.OnImmediateUpdate((t, p) => immediate++);

        {
            await using var scope = engine.BeginAction();
            await engine.Emit("acted");
        }

        Assert.Equal(1, immediate);           // 即时口收到
        Assert.Single(engine.TakeSegments()); // 段通道亦产出，互不干扰
    }
}
