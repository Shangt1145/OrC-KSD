namespace Orc.Core;

/// <summary>
/// 事件流（S5 形态）：一次执行（或引擎总流）的因果记录载体，树形结构。
/// 读面：唯一 ID、父流引用、条目可枚举（本地写入＋后代冒泡，顺序＝写入时序）、本地条目可枚举（仅本流自身写入；树形 JSON 导出的数据源）、子流可枚举（挂载时序）。
/// 写面：WriteLog / WriteUpdate 公共就绪（attach 由框架在挂载时内部写入）。
/// 写入任一流的一条条目自动向全部祖先流冒泡（同一条目；逐级递归至根，含引擎总流）。
/// 原型不设封口、裁剪与上限；树导航（父/子）与本地条目读面面向 JSON 序列化（<c>Orc.Output.EventStreamJson</c>）。
/// </summary>
public sealed class EventStream
{
    private readonly List<LogEntry> _entries = new();
    private readonly List<LogEntry> _localEntries = new();
    private readonly List<EventStream> _children = new();

    internal EventStream()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    /// <summary>流唯一 ID。</summary>
    public string Id { get; }

    /// <summary>父流引用；顶层流被挂载后其父为总流；总流为根（null）。</summary>
    public EventStream? Parent { get; private set; }

    /// <summary>条目枚举（含本地写入与后代冒泡）。</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <summary>
    /// 本地条目枚举（仅本流自身写入的条目；不含后代冒泡；顺序＝写入时序）。S5 加性读面——
    /// 树形 JSON 导出（条目归属写入流、树中不重复）的数据源；内存读面（<see cref="Entries"/> 含冒泡）不受影响。
    /// </summary>
    public IReadOnlyList<LogEntry> LocalEntries => _localEntries;

    /// <summary>子流枚举（顺序＝挂载时序）。</summary>
    public IReadOnlyList<EventStream> Children => _children;

    /// <summary>写入一条 log 条目（公共就绪面；含关键词与结构化负载）。返回写入的条目。</summary>
    /// <exception cref="ArgumentNullException">source 或 message 为 null。</exception>
    public LogEntry WriteLog(
        string source,
        string message,
        LogLevel level = LogLevel.Info,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyDictionary<string, object?>? data = null)
        => Write(LogEntryKind.Log, level, source, message, keywords, data);

    /// <summary>写入一条 update 条目（S3 起由总线 Emit 接线使用）。返回写入的条目。</summary>
    /// <exception cref="ArgumentNullException">source 或 message 为 null。</exception>
    public LogEntry WriteUpdate(
        string source,
        string message,
        LogLevel level = LogLevel.Info,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyDictionary<string, object?>? data = null)
        => Write(LogEntryKind.Update, level, source, message, keywords, data);

    internal LogEntry Write(
        LogEntryKind kind,
        LogLevel level,
        string source,
        string message,
        IReadOnlyList<string>? keywords,
        IReadOnlyDictionary<string, object?>? data)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(message);

        var entry = new LogEntry(
            kind,
            level,
            source,
            message,
            keywords is null ? Array.Empty<string>() : new List<string>(keywords),
            data is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(data));

        AppendEntry(entry);
        return entry;
    }

    /// <summary>把条目写入本流（记为本地条目）并沿父链向全部祖先流冒泡（同一条目引用；本流与每个祖先均按写入时序）。</summary>
    internal void AppendEntry(LogEntry entry)
    {
        _entries.Add(entry);
        _localEntries.Add(entry);
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor._entries.Add(entry);
        }
    }

    /// <summary>把本流挂载到父流：登记父子关系，并在父流写入 attach 条目（data 含 parentId / childId）。</summary>
    internal void AttachTo(EventStream parent, string source)
    {
        Parent = parent;
        parent._children.Add(this);
        parent.AppendEntry(new LogEntry(
            LogEntryKind.Attach,
            LogLevel.Debug,
            source,
            "子执行流挂载",
            Array.Empty<string>(),
            new Dictionary<string, object?>
            {
                ["parentId"] = parent.Id,
                ["childId"] = Id,
            }));
    }
}
