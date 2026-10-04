namespace Orc.Core;

/// <summary>
/// 动作作用域（UI 消费桥接；S1）：显式标注"一次动作"的起止。
/// 由驱动方（游戏层动作入口）以 <c>await using var _ = engine.BeginAction();</c> 取得；
/// 引擎在其释放时把该动作产生的引擎内部信号聚合为一段（<see cref="Orc.Output.EventSegment"/>）并放入待取队列。
/// 嵌套：内层作用域合并入外层，**仅最外层释放时产出段**；异常路径由 <c>finally</c>（using/await using）保证仍产出"已发生部分"的段。
/// 释放幂等；未处于作用域时的底层 <c>Emit</c> 不产段。
/// </summary>
public sealed class ActionScope : IDisposable, IAsyncDisposable
{
    private readonly LogicEngine _engine;
    private bool _disposed;

    internal ActionScope(LogicEngine engine, ActionScope? parent, int entryStart, int childStart)
    {
        _engine = engine;
        Parent = parent;
        EntryStart = entryStart;
        ChildStart = childStart;
    }

    /// <summary>父作用域（嵌套；最外层为 null）。</summary>
    internal ActionScope? Parent { get; }

    /// <summary>进入时的总流条目游标（内部使用）。</summary>
    internal int EntryStart { get; }

    /// <summary>进入时的总流子流游标（内部使用）。</summary>
    internal int ChildStart { get; }

    /// <summary>释放作用域：还原作用域栈；最外层释放时产出一段（幂等）。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return; // 幂等：重复释放＝无操作
        }

        _disposed = true;
        _engine.EndAction(this);
    }

    /// <summary>异步释放（等价于 <see cref="Dispose"/>）。</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
