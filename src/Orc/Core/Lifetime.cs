namespace Orc.Core;

/// <summary>
/// 实体存活性令牌。线程安全（Volatile / Interlocked 原语）。
/// </summary>
public sealed class Lifetime
{
    private int _alive = 1;

    /// <summary>是否仍然存活。</summary>
    public bool IsAlive => Volatile.Read(ref _alive) == 1;

    /// <summary>终止生命周期（幂等）。</summary>
    public void Kill() => Interlocked.Exchange(ref _alive, 0);
}
