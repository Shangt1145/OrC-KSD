using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 卡牌（S4）：薄容器——数据放数据组件（<see cref="AddData"/> / <see cref="GetData{T}"/>）、逻辑放逻辑组件（<see cref="AddEffect"/> / <see cref="Effects"/>）。
/// 继承 <see cref="Entity"/>（具名、可终结、可安全引用）；构造时绑定引擎（引擎首次创建卡牌时挂载其装载链处理器）。
/// 容器语义：键＝类型（每类型恰一份数据组件；重复添加拒绝、缺失读取抛明确异常）；引用共享（读取返回实例、外部可直接修改）；
/// Effects 为只读枚举面（增删经 Add/Remove API）；单线程语义、无额外保护。
/// 生命周期（驱动信号＝更新；响应＝装载链处理器/效果主触发器）：
/// 放置（card.placed）→ 初始化＋效果装载（被动：挂载＋OnMount；已放置后再 Add 的被动效果即时装载）；
/// 加载时点装载（MountPassiveEffects）→ 挂主触发器＋OnMount（幂等；与放置驱动共用同一装载链——后续放置/入场入口幂等跳过）；
/// 移除（RemoveEffect）/ 效果移除更新（effect.removed）→ 容器面移除＋卸载链；销毁（<see cref="LogicEngine.DestroyCard"/>）＝杀＋card.destroyed 驱动清理。
/// </summary>
public class Card : Entity
{
    private readonly Dictionary<Type, object> _data = new();
    private readonly List<Effect> _effects = new();

    /// <summary>创建卡牌并绑定引擎（引擎引用用于：装载链登记、已放置卡上的即时装载、销毁辅助校验、卡牌登记（S5））。</summary>
    /// <exception cref="ArgumentNullException">engine 为 null（name 的 null 校验沿用 <see cref="Entity"/>）。</exception>
    public Card(LogicEngine engine, string name)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
        engine.EnsureCardLoadout();
        engine.RegisterCard(this); // S5 卡牌登记：创建即注册（全量快照数据源；销毁清理响应时移除）
    }

    /// <summary>所属引擎（构造期绑定；内部使用）。</summary>
    internal LogicEngine Engine { get; }

    /// <summary>放置初始化标记（S4 顺序契约观察点）：经 card.placed 放置处理置位（先于效果注入）；置位后新增被动效果即时装载。</summary>
    public bool IsPlaced { get; private set; }

    // ---------- 数据组件（数据面：键＝类型；每类型恰一份） ----------

    /// <summary>
    /// 添加数据组件（任意类；实例由外部构造）。键＝实例的运行时类型。
    /// 同类型重复添加＝拒绝（明确错误，不静默覆盖；内容变更请取出实例改字段——引用共享）。
    /// </summary>
    /// <exception cref="ArgumentNullException">data 为 null。</exception>
    /// <exception cref="InvalidOperationException">已存在同类型数据组件。</exception>
    public void AddData(object data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var type = data.GetType();
        if (_data.ContainsKey(type))
        {
            throw new InvalidOperationException(
                $"卡牌 '{Name}' 已存在类型 '{type.Name}' 的数据组件（每类型恰一份；重复添加被拒绝，修改请取出实例改字段）。");
        }

        _data.Add(type, data);
    }

    /// <summary>读取数据组件（强类型；返回实例引用）。缺失＝明确异常（不返回 null/default，防 null 蔓延）。</summary>
    /// <exception cref="KeyNotFoundException">未添加该类型的数据组件。</exception>
    public T GetData<T>()
        where T : class
    {
        if (_data.TryGetValue(typeof(T), out var value))
        {
            return (T)value;
        }

        throw new KeyNotFoundException($"卡牌 '{Name}' 不存在类型 '{typeof(T).Name}' 的数据组件。");
    }

    /// <summary>试探读取数据组件（加性读面；未添加返回 false 且 data 为 null）。</summary>
    public bool TryGetData<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? data)
        where T : class
    {
        if (_data.TryGetValue(typeof(T), out var value))
        {
            data = (T)value;
            return true;
        }

        data = null;
        return false;
    }

    /// <summary>数据组件全量读面（S5 加性内部面）：类型 → 实例；快照序列化（<c>Orc.Output.SnapshotJson</c>）用。</summary>
    internal IReadOnlyDictionary<Type, object> DataComponents => _data;

    // ---------- 逻辑组件（效果列表） ----------

    /// <summary>效果列表（只读枚举面；顺序＝添加序——装载/清理顺序确定性的来源）。</summary>
    public IReadOnlyList<Effect> Effects => _effects;

    /// <summary>
    /// 添加效果。放置前 Add＝静态组装（等待放置驱动）；已放置卡 Add 的被动效果＝即时执行装载链（与放置驱动装载行为一致，不产生静默死效果）；
    /// 主动效果始终仅进入列表（等待施放）。
    /// </summary>
    /// <exception cref="ArgumentNullException">effect 为 null。</exception>
    /// <exception cref="InvalidOperationException">该实例已在列表（重复添加被拒绝）；或该效果实例已属于另一张卡牌。</exception>
    public void AddEffect(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (_effects.Contains(effect))
        {
            throw new InvalidOperationException(
                $"卡牌 '{Name}' 已持有效果 '{effect.Name}'（同一实例重复添加被拒绝；先移除再添加）。");
        }

        if (effect.HasHost && !ReferenceEquals(effect.HostOrNull, this))
        {
            throw new InvalidOperationException(
                $"效果 '{effect.Name}' 已属于另一张卡牌（须先移除再添加）。");
        }

        _effects.Add(effect);
        effect.SetHost(this);

        if (IsPlaced && effect.Kind == TriggerKind.Passive)
        {
            CardLoadout.MountEffect(this, effect); // 即时装载（幂等：未装载才执行）
        }
    }

    /// <summary>
    /// 移除效果（规范入口）：容器面移除；已装载 → 卸载链（OnUnmount → 撤销登记 → 总线卸载 → 清引用）；
    /// 未装载 → 仅容器面移除（无运行态清理动作）。
    /// 幂等：移除不存在/已移除的效果＝无操作、不抛错；重复移除＝幂等。
    /// </summary>
    /// <exception cref="ArgumentNullException">effect 为 null。</exception>
    public void RemoveEffect(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        CardLoadout.CleanupEffect(this, effect);
    }

    /// <summary>
    /// 装载全部被动效果（加载时点装载入口；加性公共面——供游戏层在「卡牌加载」时点驱动）：
    /// 按 Effects 列表序逐效果执行装载链（挂主触发器 → OnMount；幂等——已装载者跳过；失败＝记录 ＋ 回滚为未生效、不阻断其它效果与宿主）。
    /// 与放置驱动装载（card.placed 处理器）共用同一装载链与幂等语义：加载时点装载后，
    /// 后续其它装载入口（放置/入场等）到达时幂等跳过（已装载不重复 OnMount）；主动效果不装载（仅列表进出）。
    /// </summary>
    public void MountPassiveEffects()
    {
        foreach (var effect in EffectsSnapshot())
        {
            if (effect.Kind == TriggerKind.Passive)
            {
                CardLoadout.MountEffect(this, effect);
            }
        }
    }

    // ---------- 框架内部（装载链/销毁/收集） ----------

    /// <summary>置放置标记（装载链处理器放置处理调用；幂等）。</summary>
    internal void MarkPlaced() => IsPlaced = true;

    /// <summary>从列表移除（不清算；清理模板调用）。返回是否命中。</summary>
    internal bool RemoveEffectCore(Effect effect) => _effects.Remove(effect);

    /// <summary>列表是否含该效果（归属校验用）。</summary>
    internal bool ContainsEffect(Effect effect) => _effects.Contains(effect);

    /// <summary>列表快照（遍历中允许清理动作修改列表）。</summary>
    internal IReadOnlyList<Effect> EffectsSnapshot() => _effects.ToArray();
}
