using System.Runtime.CompilerServices;

namespace Orc.Lua;

/// <summary>引用相等比较器（用于循环引用检测与域代理缓存）。</summary>
internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    internal static readonly ReferenceComparer<T> Instance = new();

    private ReferenceComparer()
    {
    }

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
