namespace Orc.Core;

/// <summary>
/// 稳定哈希（S-C1；审查链标识层）：FNV-1a 64 位，输入按 UTF-8 字节、ordinal 逐字节计算。
/// 契约：纯函数——同一字符串在任何进程/机器/次第上得到同一值；不做文化敏感处理、不做归一化。
/// </summary>
public static class StableHash
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    /// <summary>计算字符串的 64 位 FNV-1a 稳定哈希。</summary>
    /// <exception cref="ArgumentNullException">text 为 null。</exception>
    public static ulong Fnv1a64(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hash = OffsetBasis;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash;
    }
}

/// <summary>
/// hook 标识（S-C1）：hook 名（开放字符串）的**定义级**稳定标识，与名字双向互转（见 <see cref="HookRegistry"/>）。
/// 由名字确定性派生（<see cref="FromName"/>）——不依赖装配顺序、跨对局/跨进程/跨机器一致。
/// 自由字符串语义保留：任意名字均可取标识。
/// </summary>
public readonly record struct HookId
{
    private HookId(ulong value) => Value = value;

    /// <summary>标识原始值（64 位稳定哈希）。</summary>
    public ulong Value { get; }

    /// <summary>由 hook 名确定性派生标识（纯函数）。</summary>
    /// <exception cref="ArgumentException">hookName 为 null/空/纯空白。</exception>
    public static HookId FromName(string hookName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookName);
        return new HookId(StableHash.Fnv1a64(hookName));
    }

    /// <summary>是否为未初始化值（default）。</summary>
    public bool IsUnset => Value == 0UL;

    /// <inheritdoc />
    public override string ToString() => IsUnset ? "hook:<unset>" : $"hook:{Value:x16}";
}

/// <summary>
/// 触发器标识（S-C1）：触发器**种类/定义**的稳定标识（如"打出触发器"），与展示名无关（展示名同名不消歧）。
/// 由稳定键确定性派生（<see cref="FromKey"/>）——跨对局/跨进程/跨机器一致；具体实例以 (TriggerId, 宿主) 定位。
/// </summary>
public readonly record struct TriggerId
{
    private TriggerId(ulong value) => Value = value;

    /// <summary>标识原始值（64 位稳定哈希）。</summary>
    public ulong Value { get; }

    /// <summary>由稳定键确定性派生标识（纯函数）。</summary>
    /// <exception cref="ArgumentException">stableKey 为 null/空/纯空白。</exception>
    public static TriggerId FromKey(string stableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);
        return new TriggerId(StableHash.Fnv1a64(stableKey));
    }

    /// <summary>是否为未初始化值（default）。</summary>
    public bool IsUnset => Value == 0UL;

    /// <inheritdoc />
    public override string ToString() => IsUnset ? "trigger:<unset>" : $"trigger:{Value:x16}";
}
