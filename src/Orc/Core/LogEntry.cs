namespace Orc.Core;

/// <summary>事件流条目类别：log（普通记录）/ attach（子流挂载）/ update（更新载荷；S3 起由总线 Emit 写入）。</summary>
public enum LogEntryKind
{
    Log,
    Attach,
    Update,
}

/// <summary>条目级别（固定四档）。</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// 事件流条目（只读）：kind / timestamp（统一 UTC）/ level / source（按「触发器名/事件名」构成；触发器未命名时以类型名退化）
/// / message / keywords（扁平字符串列表，允许 key:value 形式）/ data（结构化负载，可空）。
/// 条目由框架或 <see cref="EventStream"/> 公共写 API 创建；在祖先流中为同一逻辑条目（同一引用，不改写内容）。
/// </summary>
public sealed class LogEntry
{
    internal LogEntry(
        LogEntryKind kind,
        LogLevel level,
        string source,
        string message,
        IReadOnlyList<string> keywords,
        IReadOnlyDictionary<string, object?> data)
    {
        Kind = kind;
        Timestamp = DateTimeOffset.UtcNow;
        Level = level;
        Source = source;
        Message = message;
        Keywords = keywords;
        Data = data;
    }

    /// <summary>条目类别。</summary>
    public LogEntryKind Kind { get; }

    /// <summary>时间戳（UTC）。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>条目级别。</summary>
    public LogLevel Level { get; }

    /// <summary>来源（触发器/事件名构成）。</summary>
    public string Source { get; }

    /// <summary>可读消息。</summary>
    public string Message { get; }

    /// <summary>关键词（扁平列表；可含 key:value 形式，如 exception:{TypeName}）。</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>结构化负载（可省略/为空）。</summary>
    public IReadOnlyDictionary<string, object?> Data { get; }
}
