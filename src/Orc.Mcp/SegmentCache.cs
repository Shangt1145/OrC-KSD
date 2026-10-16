using Orc.Output;

namespace Orc.Mcp;

/// <summary>
/// 事件流段缓存（R2e 决策）：引擎侧 <c>TakeSegments()</c> 为"取走即移除"，
/// 服务层在拉取后保留最近 N 段副本，供 agent 重复读取（≤ <see cref="Capacity"/>，超出丢最旧）。
/// 与引擎语义的关系：**不改变**段队列的消费语义；缓存仅是服务层的只读副本。
/// </summary>
public sealed class SegmentCache
{
    private readonly Queue<EventSegment> _segments = new();

    public SegmentCache(int capacity)
    {
        Capacity = Math.Max(1, capacity);
    }

    /// <summary>容量（最多保留段数）。</summary>
    public int Capacity { get; }

    /// <summary>已缓存段数。</summary>
    public int Count => _segments.Count;

    /// <summary>最近一次缓存的段序号（无＝0）。</summary>
    public long LastSequence { get; private set; }

    /// <summary>按入队顺序的缓存快照。</summary>
    public IReadOnlyList<EventSegment> Snapshot() => _segments.ToArray();

    /// <summary>追加段并做容量淘汰。</summary>
    public void AddRange(IEnumerable<EventSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        foreach (var segment in segments)
        {
            if (segment is null)
            {
                continue;
            }

            _segments.Enqueue(segment);
            LastSequence = Math.Max(LastSequence, segment.Sequence);
        }

        while (_segments.Count > Capacity)
        {
            _segments.Dequeue();
        }
    }

    /// <summary>清空缓存（不影响引擎侧段队列）。</summary>
    public void Clear()
    {
        _segments.Clear();
        LastSequence = 0;
    }
}
