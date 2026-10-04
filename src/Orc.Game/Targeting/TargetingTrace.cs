using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 留痕接收器（可观测性为硬要求）：留痕覆盖所有失败与违规路径（收集失败、桥接交互异常、筛选回调异常、
/// 域判定回调异常、无可用候选、内容不合规、标识不匹配、时序违规、未装配）。
/// 对局场景＝引擎既有渠道（事件流，见 <see cref="EventStreamTargetingTrace"/>）；
/// 独立构造＝注入优先（可选注入留痕目标）、未注入时降级为可观测记录（内存留痕 <see cref="InMemoryTargetingTrace"/>，可查询面）。
/// </summary>
public interface ITargetingTraceSink
{
    /// <summary>写入一条 targeting 留痕（实现不应抛出；框架侧亦做隔离防御）。</summary>
    void Write(TargetingTraceEntry entry);
}

/// <summary>targeting 留痕条目（只读）：级别 / 来源 / 消息 / 关键词（扁平、可含 key:value 形式）/ 结构化负载。</summary>
public sealed class TargetingTraceEntry
{
    internal TargetingTraceEntry(
        LogLevel level,
        string source,
        string message,
        IReadOnlyList<string> keywords,
        IReadOnlyDictionary<string, object?> data)
    {
        Level = level;
        Source = source;
        Message = message;
        Keywords = keywords;
        Data = data;
    }

    /// <summary>条目级别。</summary>
    public LogLevel Level { get; }

    /// <summary>来源（固定为 "targeting"）。</summary>
    public string Source { get; }

    /// <summary>可读消息。</summary>
    public string Message { get; }

    /// <summary>关键词（扁平列表；如 reason:{原因}、violation:{类别}、requestId:{id}）。</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>结构化负载（requestId / 细节等）。</summary>
    public IReadOnlyDictionary<string, object?> Data { get; }
}

/// <summary>内存留痕（独立构造的缺省降级形态；可查询面绑定测试断言与调试观查）。</summary>
public sealed class InMemoryTargetingTrace : ITargetingTraceSink
{
    private readonly object _sync = new();
    private readonly List<TargetingTraceEntry> _entries = new();

    /// <summary>已记录的留痕条目（记录序快照）。</summary>
    public IReadOnlyList<TargetingTraceEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>写入记录（线程安全；追加到记录序末尾）。</summary>
    public void Write(TargetingTraceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_sync)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>是否含带指定关键词的条目（便捷查询）。</summary>
    public bool ContainsKeyword(string keyword)
        => Entries.Any(e => e.Keywords.Contains(keyword, StringComparer.Ordinal));
}

/// <summary>事件流留痕（对局场景＝引擎既有渠道）：把留痕条目转发为事件流 log 条目（冒泡至总流、可经既有读面观测）。</summary>
public sealed class EventStreamTargetingTrace : ITargetingTraceSink
{
    private readonly EventStream _stream;

    /// <summary>创建事件流留痕接收器。</summary>
    /// <exception cref="ArgumentNullException">stream 为 null。</exception>
    public EventStreamTargetingTrace(EventStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    /// <summary>写入（转发为 <see cref="EventStream.WriteLog"/>；条目冒泡沿父链包含引擎总流）。</summary>
    public void Write(TargetingTraceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _stream.WriteLog(entry.Source, entry.Message, entry.Level, entry.Keywords, entry.Data);
    }
}

/// <summary>留痕写入助手（框架内部）：统一来源与关键词形态；写入异常被隔离（留痕不破坏队列与后续请求）。</summary>
internal static class TargetingTraceLog
{
    internal const string Source = "targeting";

    /// <summary>写留痕（sink 异常静默隔离——可观测性为增强、不得反向破坏流程；防崩底线优先）。</summary>
    internal static void Write(
        ITargetingTraceSink sink,
        LogLevel level,
        string message,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        try
        {
            sink.Write(new TargetingTraceEntry(
                level,
                Source,
                message,
                keywords is null ? Array.Empty<string>() : keywords.ToArray(),
                data is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(data)));
        }
        catch
        {
            // 留痕通道异常被隔离：不得反向破坏 targeting 流程与队列（可观测性为增强）。
        }
    }

    /// <summary>构造带 requestId 关键词的负载。</summary>
    internal static IReadOnlyDictionary<string, object?> Payload(string requestId, string? detail = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["requestId"] = requestId,
        };

        if (detail is not null)
        {
            data["detail"] = detail;
        }

        return data;
    }
}
