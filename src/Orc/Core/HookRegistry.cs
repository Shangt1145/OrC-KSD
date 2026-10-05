namespace Orc.Core;

/// <summary>
/// hook 标识注册表（S-C1；引擎级、引擎内维护）：维护 hook 名 ↔ <see cref="HookId"/> 的双向映射与登记序。
/// 语义：开放集合——任意非空白名字均可登记（自由字符串保留，无白名单、无拒绝清单）；
/// 同一名字恒得同一标识（与登记次第无关）；首次登记即固化（对局内不变）。
/// 反向映射单值化：若两个不同名字产生同一标识（64 位碰撞），保留先登记者并记入 <see cref="Collisions"/>，不抛错。
/// 单线程语义（与引擎一致，不引入锁）。
/// </summary>
public sealed class HookRegistry
{
    private readonly Dictionary<string, HookId> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<HookId, string> _byId = new();
    private readonly List<string> _order = new();
    private readonly List<HookIdCollision> _collisions = new();

    /// <summary>登记一个 hook 名并返回其标识（同名幂等复用；空白名＝拒绝）。</summary>
    /// <exception cref="ArgumentException">hookName 为 null/空/纯空白。</exception>
    public HookId Register(string hookName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookName);

        if (_byName.TryGetValue(hookName, out var existing))
        {
            return existing; // 同名幂等：标识与登记序均不变
        }

        var id = HookId.FromName(hookName);
        _byName[hookName] = id;

        if (_byId.TryGetValue(id, out var first))
        {
            _collisions.Add(new HookIdCollision(id, first, hookName)); // 碰撞：保留先登记者、登记碰撞
            return id;
        }

        _byId[id] = hookName;
        _order.Add(hookName);
        return id;
    }

    /// <summary>按名字取标识（未登记＝false；不隐式登记）。</summary>
    public bool TryGetId(string hookName, out HookId id)
    {
        ArgumentNullException.ThrowIfNull(hookName);
        return _byName.TryGetValue(hookName, out id);
    }

    /// <summary>按标识取名字（碰撞时返回先登记者；未登记＝false）。</summary>
    public bool TryGetName(HookId id, out string hookName)
    {
        if (_byId.TryGetValue(id, out var name))
        {
            hookName = name;
            return true;
        }

        hookName = string.Empty;
        return false;
    }

    /// <summary>已登记名字（登记序只读快照）。</summary>
    public IReadOnlyList<string> Names => _order;

    /// <summary>已发现的 64 位标识碰撞（登记序只读快照；正常情形为空）。</summary>
    public IReadOnlyList<HookIdCollision> Collisions => _collisions;

    /// <summary>已登记名字数量。</summary>
    public int Count => _order.Count;
}

/// <summary>
/// hook 标识碰撞记录（S-C1）：两个不同名字产生同一 <see cref="HookId"/> 时的取证记录（不改变登记结果）。
/// </summary>
/// <param name="Id">发生碰撞的标识。</param>
/// <param name="FirstName">先登记的名字（反向映射保留者）。</param>
/// <param name="SecondName">后登记的名字。</param>
public sealed record HookIdCollision(HookId Id, string FirstName, string SecondName);
