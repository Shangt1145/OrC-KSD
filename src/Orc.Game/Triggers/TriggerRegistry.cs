using Orc.Core;

namespace Orc.Game.Triggers;

/// <summary>
/// 触发器注册表（游戏层、对局级）：登记「触发器对象本体＋分层分类」；注册与挂载/运行完全解耦（只记账，不影响运行、不参与挂载）。
/// 边界（轻量、靠自觉）：未注册触发器照常运行；不强制底层必须注册；无运行期检查与拦截；注册后不可变（不提供移除，随对局销毁回收）。
/// 拒绝情形（明确错误、不吞）：null 触发器；未定义的分层值；同一触发器对象重复登记。
/// 查询：按分层枚举（<see cref="GetByLayer"/>——「全部底层」/「全部外部」）；全量（<see cref="Entries"/>，登记序）。
/// 「底层触发器清单」查询供后续批次/审计核对（本批不建审计行为）。
/// 声明位触发器本批不注册（真实注册自 2C 指挥触发器启用）；本批以测试触发器演示。
/// </summary>
public sealed class TriggerRegistry
{
    private readonly List<TriggerRegistryEntry> _entries = new();

    /// <summary>
    /// 登记一个触发器（携带分层分类——「标注」＝该分类信息的登记内容；分类为登记参数、必填）。
    /// </summary>
    /// <exception cref="ArgumentNullException">trigger 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">layer 为未定义的分层值（配置错误在登记期被拒绝）。</exception>
    /// <exception cref="InvalidOperationException">同一触发器对象重复登记（被拒绝）。</exception>
    public void Register<TView>(Trigger<TView> trigger, TriggerLayer layer)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (!Enum.IsDefined(layer))
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "未定义的触发器分层值（配置错误在登记期被拒绝）。");
        }

        if (_entries.Any(entry => ReferenceEquals(entry.Trigger, trigger)))
        {
            throw new InvalidOperationException("同一触发器对象重复登记（注册后不可变；如需变更，重建对局/注册表）。");
        }

        _entries.Add(new TriggerRegistryEntry(trigger, layer));
    }

    /// <summary>全量登记（登记序）。</summary>
    public IReadOnlyList<TriggerRegistryEntry> Entries => _entries;

    /// <summary>按分层枚举登记项（「全部底层」/「全部外部」；登记序）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">layer 为未定义的分层值。</exception>
    public IReadOnlyList<TriggerRegistryEntry> GetByLayer(TriggerLayer layer)
    {
        if (!Enum.IsDefined(layer))
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "未定义的触发器分层值。");
        }

        return _entries.Where(entry => entry.Layer == layer).ToList();
    }
}

/// <summary>
/// 登记项（登记信息面最小集）：触发器对象本体＋分层分类；
/// 名称/来源/归属不强制登记（可按需从触发器自身读取）。
/// </summary>
public sealed class TriggerRegistryEntry
{
    internal TriggerRegistryEntry(object trigger, TriggerLayer layer)
    {
        Trigger = trigger;
        Layer = layer;
    }

    /// <summary>触发器对象本体（<c>Trigger&lt;TView&gt;</c> 实例；具体视图类型经运行时类型可判）。</summary>
    public object Trigger { get; }

    /// <summary>分层分类（登记时携带、必填）。</summary>
    public TriggerLayer Layer { get; }
}
