using System.Runtime.CompilerServices;

namespace Orc.Core;

/// <summary>
/// 引擎托管引用表（引擎实例级）：以**全 GUID 字符串（N 格式）**为 key 的"对象引用"登记面——
/// 供宿主/服务层以稳定标识表达与解析对局内对象（事件载荷、工具参数、观察面输出）。
/// <para>
/// 语义：
/// ①已有 GUID 的对象（<see cref="Entity.Id"/>，如卡牌与其子类 HQ）直接以既有 Id 为 key 登记（不发明第二套 id）；
/// ②无 GUID 的对象（如游戏层 <c>Player</c>、效果对象）由本表**分配** GUID（<see cref="Assign"/>）；
/// ③同一 key 重复登记**同一对象**＝幂等；同一 key 登记**不同对象**＝拒绝（不静默覆盖）；
/// 同一对象不允许以两个不同 key 登记；
/// ④注销：随实体销毁/对局销毁；<see cref="Clear"/> 供回滚与临时装载使用。
/// </para>
/// <para>
/// 线程模型：与引擎一致的单线程语义（不加锁；调用方负责串行化）。
/// 多引擎实例各自独立（每实例一张表；无全局单例）。
/// </para>
/// </summary>
public sealed class EngineReferences
{
    private readonly Dictionary<string, object> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> _keyByObject = new(ObjectIdentityComparer.Instance);

    /// <summary>已登记条目数。</summary>
    public int Count => _byKey.Count;

    /// <summary>全部已登记 key（只读视图；登记序不保证）。</summary>
    public IReadOnlyCollection<string> Keys => _byKey.Keys;

    /// <summary>是否存在该 key（空白/null＝false）。</summary>
    public bool Contains(string key)
        => !string.IsNullOrWhiteSpace(key) && _byKey.ContainsKey(key);

    /// <summary>
    /// 为**无 GUID** 的对象分配并登记引用键（已登记则返回既有键）。
    /// </summary>
    /// <returns>分配的引用键（N 格式 GUID 字符串）。</returns>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public string Assign(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_keyByObject.TryGetValue(target, out var existing))
        {
            return existing;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = Guid.NewGuid().ToString("N");
            if (_byKey.ContainsKey(key))
            {
                continue; // 不可达（防御）
            }

            return Register(key, target);
        }

        throw new InvalidOperationException("引用键分配冲突（连续生成同键）——不可达。");
    }

    /// <summary>以既有 GUID（<see cref="Entity.Id"/>）为 key 登记对象（等价于 <see cref="Register(string,object)"/> 的 N 格式形态）。</summary>
    /// <returns>登记使用的引用键（N 格式）。</returns>
    public string Register(Guid id, object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Register(id.ToString("N"), target);
    }

    /// <summary>
    /// 登记对象引用（key 为 N 格式 GUID 字符串）。
    /// </summary>
    /// <returns>登记使用的引用键。</returns>
    /// <exception cref="ArgumentException">key 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一 key 已登记**另一对象**；或该对象已以**另一 key** 登记。</exception>
    public string Register(string key, object target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(target);

        if (_byKey.TryGetValue(key, out var existing))
        {
            if (ReferenceEquals(existing, target))
            {
                return key; // 幂等：同 key 同对象
            }

            throw new InvalidOperationException(
                $"引用键 '{key}' 已被另一对象登记（重复登记被拒绝——不静默覆盖）。");
        }

        if (_keyByObject.TryGetValue(target, out var otherKey))
        {
            throw new InvalidOperationException(
                $"对象已以引用键 '{otherKey}' 登记（同一对象不允许两个引用键）。");
        }

        _byKey.Add(key, target);
        _keyByObject.Add(target, key);
        return key;
    }

    /// <summary>按 key 解析对象（不存在＝false，不抛错）。</summary>
    public bool TryResolve(string key, out object? target)
    {
        if (!string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key, out var found))
        {
            target = found;
            return true;
        }

        target = null;
        return false;
    }

    /// <summary>按 key 解析并做类型判定（类型不符＝false）。</summary>
    public bool TryResolve<T>(string key, out T? target)
        where T : class
    {
        target = TryResolve(key, out var found) && found is T typed ? typed : null;
        return target is not null;
    }

    /// <summary>反查对象的引用键（未登记＝false）。</summary>
    public bool TryGetKey(object target, out string? key)
    {
        ArgumentNullException.ThrowIfNull(target);
        var found = _keyByObject.TryGetValue(target, out var value);
        key = value;
        return found;
    }

    /// <summary>注销引用（幂等：不存在＝false，不抛错）。</summary>
    public bool Unregister(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!_byKey.Remove(key, out var target))
        {
            return false;
        }

        _keyByObject.Remove(target);
        return true;
    }

    /// <summary>以既有 GUID 注销引用（幂等）。</summary>
    public bool Unregister(Guid id) => Unregister(id.ToString("N"));

    /// <summary>清空全部登记（回滚/临时装载用）。</summary>
    public void Clear()
    {
        _byKey.Clear();
        _keyByObject.Clear();
    }

    /// <summary>引用身份比较器（按引用相等；不触发 <c>Equals</c> 重写）。</summary>
    private sealed class ObjectIdentityComparer : IEqualityComparer<object>
    {
        public static readonly ObjectIdentityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
