using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点⑥：事件流——唯一 ID、自动挂载（顶层挂总流/嵌套挂触发者流）、attach 日志含父子 ID、
/// 写入向祖先冒泡（同一条目、写入时序）、条目含关键词、读面便于后续 JSON 序列化、update 写入 API 就绪。
/// </summary>
public class EventStreamTests
{
    [Fact]
    public async Task Top_Level_Execution_Mounts_To_Root_With_Attach_Entry_And_Unique_Ids()
    {
        var engine = new LogicEngine();
        var trigger = new Trigger<CounterView>(name: "顶层", events: new[]
        {
            new TriggerEvent<CounterView>("仅", (v, c, t) => Task.CompletedTask),
        });

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.False(string.IsNullOrEmpty(stream.Id));               // 唯一 ID
        Assert.False(string.IsNullOrEmpty(engine.RootStream.Id));
        Assert.NotEqual(stream.Id, engine.RootStream.Id);
        Assert.Same(engine.RootStream, stream.Parent);               // 顶层流父引用＝总流对象
        Assert.Null(engine.RootStream.Parent);                       // 总流为根
        Assert.Contains(stream, engine.RootStream.Children);         // 自动挂载（无需手动）

        var attach = engine.RootStream.Entries.Single(e => e.Kind == LogEntryKind.Attach);
        Assert.Equal(engine.RootStream.Id, attach.Data["parentId"]); // attach 必备父/子流 ID
        Assert.Equal(stream.Id, attach.Data["childId"]);
        Assert.False(string.IsNullOrEmpty(attach.Source));
        Assert.True(attach.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1)); // UTC 时间戳
    }

    [Fact]
    public async Task Entry_Written_To_Child_Stream_Bubbles_Up_To_All_Ancestors_In_Write_Order()
    {
        var engine = new LogicEngine();
        EventStream? childStream = null;

        var child = new Trigger<CounterView>(name: "子", events: new[]
        {
            new TriggerEvent<CounterView>("子事件", (v, c, t) => Task.CompletedTask),
        });
        var parent = new Trigger<CounterView>(name: "父", events: new[]
        {
            new TriggerEvent<CounterView>("父事件", async (v, c, t) =>
            {
                childStream = await child.InvokeAsync(engine, new Dictionary<string, object?>());
            }),
        });

        var parentStream = await parent.InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.NotNull(childStream);

        var e1 = childStream!.WriteLog("测试源", "第一条", keywords: new[] { "shield:2", "trace" });
        var e2 = childStream.WriteLog("测试源", "第二条");
        var e3 = childStream.WriteUpdate("测试源", "更新载荷", data: new Dictionary<string, object?> { ["Amount"] = 3 });

        Assert.Equal(LogEntryKind.Log, e1.Kind);
        Assert.Equal(new[] { "shield:2", "trace" }, e1.Keywords); // 条目含关键词（扁平列表，可含 key:value）
        Assert.Equal(LogEntryKind.Update, e3.Kind);               // update 写入 API 就绪
        Assert.Equal(3, (int)e3.Data["Amount"]!);

        // 冒泡：同一条目（同一引用）出现在全部祖先流（父流、总流）
        Assert.Contains(e1, childStream.Entries);
        Assert.Contains(e1, parentStream.Entries);
        Assert.Contains(e1, engine.RootStream.Entries);
        Assert.Same(e1, parentStream.Entries.First(x => x.Message == "第一条"));

        // 顺序＝写入时序（子流条目：无 attach——attach 写入父流侧）
        Assert.Equal(new[] { e1, e2, e3 }, childStream.Entries.ToArray());

        // 子流挂载的 attach 条目（写入父流；含父子 ID）先于父流上的后续写入：
        var childAttach = parentStream.Entries.Single(
            e => e.Kind == LogEntryKind.Attach && Equals(e.Data["childId"], childStream.Id));
        Assert.Equal(parentStream.Id, childAttach.Data["parentId"]);
        Assert.Equal(new[] { childAttach, e1, e2, e3 }, parentStream.Entries.ToArray());
    }

    [Fact]
    public async Task Multiple_Top_Level_Executions_Create_Sibling_Streams_Under_Root()
    {
        var engine = new LogicEngine();
        var trigger = new Trigger<CounterView>(name: "多次", events: new[]
        {
            new TriggerEvent<CounterView>("仅", (v, c, t) => Task.CompletedTask),
        });

        var s1 = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());
        var s2 = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.NotSame(s1, s2);                       // 各自成流
        Assert.Same(engine.RootStream, s1.Parent);    // 共同挂于总流之下（兄弟顶层流）
        Assert.Same(engine.RootStream, s2.Parent);
        Assert.Equal(2, engine.RootStream.Children.Count);
        Assert.Equal(2, engine.RootStream.Entries.Count(e => e.Kind == LogEntryKind.Attach));
    }

    [Fact]
    public void Entry_Exposes_Complete_Structure_And_Read_Surface_Is_Navigable()
    {
        var engine = new LogicEngine();

        var entry = engine.RootStream.WriteLog(
            "ops",
            "人工记录",
            LogLevel.Warning,
            keywords: new[] { "manual", "level:warning" },
            data: new Dictionary<string, object?> { ["n"] = 1 });

        Assert.Equal(LogEntryKind.Log, entry.Kind);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("ops", entry.Source);
        Assert.Equal("人工记录", entry.Message);
        Assert.Equal(new[] { "manual", "level:warning" }, entry.Keywords);
        Assert.Equal(1, (int)entry.Data["n"]!);
        Assert.True(entry.Timestamp <= DateTimeOffset.UtcNow);
        Assert.Contains(entry, engine.RootStream.Entries);

        // 读面导航（面向 JSON 序列化：条目/子流可枚举、父子可导航）：
        Assert.Empty(engine.RootStream.Children);
    }

    [Fact]
    public async Task Unnamed_Trigger_Degrades_Source_To_View_Type_Name()
    {
        var engine = new LogicEngine();
        var trigger = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("炸", (v, c, t) => throw new InvalidOperationException("x")),
        });

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        var record = stream.Entries.Single(e => e.Keywords.Contains("exception:InvalidOperationException"));
        Assert.Equal("CounterView/炸", record.Source); // 未命名：以视图类型名退化 + 事件名
    }
}
