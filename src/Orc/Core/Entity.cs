namespace Orc.Core;

/// <summary>
/// 实体基类：具名、可终结、可安全引用。卡牌、单位、玩家等后续均继承此类（保持可继承）。
/// </summary>
public class Entity
{
    /// <summary>实体名（非 null）。</summary>
    public string Name { get; }

    /// <summary>生命周期令牌。</summary>
    public Lifetime Life { get; } = new();

    /// <summary>指向自身的引用（公开只读，类型为基类视角的 <see cref="Ref{T}"/>）。</summary>
    public Ref<Entity> Ref { get; }

    public Entity(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Ref = new Ref<Entity>(this, Life, name);
    }

    /// <summary>销毁实体（幂等）：置生命周期为失效。</summary>
    /// <remarks>
    /// 只杀不管卸载：Destroy 后 Name / Life / Ref 本身的访问仍允许（观察点为 Life.IsAlive / Ref.IsAlive 为 false、Ref.Value 抛 Stale）。
    /// 总线卸载通知链路（bus.UnmountOwner）留待 S3，本阶段不实现。
    /// </remarks>
    public void Destroy() => Life.Kill();
}
