namespace Orc.Core;

/// <summary>
/// 非泛型引用信息面（S5 加性）：引用名与存活性；供弱类型/降级场景（如值序列化降级）统一读取。
/// </summary>
public interface IRefInfo
{
    /// <summary>引用名称（创建时命名，如实体名）。</summary>
    string Name { get; }

    /// <summary>目标是否仍然存活。</summary>
    bool IsAlive { get; }
}

/// <summary>
/// 安全引用：指向可被销毁的目标（如实体），访问时二次校验生命周期。
/// 构造仅限库内（如由 <see cref="Entity"/> 创建）；外部获取引用的唯一途径是持有者暴露的 Ref 属性。
/// </summary>
/// <typeparam name="T">引用目标类型。</typeparam>
public sealed class Ref<T> : IRefInfo where T : class
{
    private readonly T _target;
    private readonly Lifetime _life;

    /// <summary>引用名称（创建时命名，如实体名）。</summary>
    public string Name { get; }

    internal Ref(T target, Lifetime life, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(life);
        _target = target;
        _life = life;
        Name = name;
    }

    /// <summary>目标是否仍然存活。</summary>
    public bool IsAlive => _life.IsAlive;

    /// <summary>
    /// 目标值。目标已失效时抛出 <see cref="StaleReferenceException"/>（访问时二次校验，兜底第一道防线之外的漏网）。
    /// </summary>
    public T Value => _life.IsAlive
        ? _target
        : throw new StaleReferenceException($"Ref<{typeof(T).Name}> '{Name}' 已失效");

    /// <summary>
    /// 目标对象（不校验生命周期；S5 加性内部面）：仅供框架内部降级观察（如失效引用的快照导出——呈现标识与存活标记而不抛）。
    /// 公共访问仍经 <see cref="Value"/> 二次校验；本面无其他消费方。
    /// </summary>
    internal T TargetForObservation => _target;
}
