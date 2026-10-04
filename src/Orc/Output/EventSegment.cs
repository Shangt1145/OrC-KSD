using Orc.Core;

namespace Orc.Output;

/// <summary>
/// 事件流段（UI 消费桥接；S3）：一次<see cref="LogicEngine.BeginAction"/>作用域内产生的引擎内部信号的聚合。
/// 由引擎在动作作用域结束时产出、放入待取队列，供 UI 轮询消费（<see cref="LogicEngine.TakeSegments"/>）。
/// 内容＝本段在总流上新增的全部条目（<see cref="Entries"/>；含冒泡＝本段完整因果，含报错）＋本段新增的顶层子树（<see cref="Children"/>，递归含嵌套）。
/// 元信息仅段号（引擎实例内单调递增）与时间戳（UTC）；可由条目推导的信息（动作/回合/玩家）不重复放置。
/// 条目为只读不可变对象、执行流结束后不再被写入，故段持引用安全、无需深拷贝。
/// </summary>
public sealed class EventSegment
{
    internal EventSegment(long sequence, IReadOnlyList<LogEntry> entries, IReadOnlyList<EventStream> children)
    {
        Sequence = sequence;
        Timestamp = DateTimeOffset.UtcNow;
        Entries = entries;
        Children = children;
    }

    /// <summary>段号（引擎实例内单调递增，自 1 起；用于排序 / 检测漏段）。</summary>
    public long Sequence { get; }

    /// <summary>段产出时间戳（UTC）。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>本段全部条目（含冒泡；写入时序）——UI 可据此线性播放，报错亦在其中（<c>level=error</c>）。</summary>
    public IReadOnlyList<LogEntry> Entries { get; }

    /// <summary>本段新增的顶层子树（挂载时序；递归含嵌套因果）；其内部条目同时出现在 <see cref="Entries"/> 中。</summary>
    public IReadOnlyList<EventStream> Children { get; }
}
