using Orc.Cards;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// W2a G3 修饰机制核心（更新检测侧）：
// 更新检测接口＝「参与运行时数据变更的组件」实现的契约（生成缓存/比较缓存）；由组件承担两者的领域知识，
// 机制侧（卡侧容器）负责调度调用与结果汇聚——每轮管线内集中调用一次：按名单逐组件「生成→比较」，汇聚总差异与变更判定。
// 快照＝字段标识→值的只读集合（须为新对象；生成/比较无副作用——不修改入参、不影响后续轮；失败＝fail-fast、不得坏快照）。
// 比较输出＝「是否有变更」＋「字段级差异」（方向：旧→新；字段顺序＝组件声明序——稳定可判别）。
// 卡端持有检测组件名单＋各组件缓存（每卡实例一份；首轮前以基准状态建立初始快照作为比较基线）。
// 「哪些字段变化」的产出责任＝检测比较侧（供集中触发的载荷变化字段集合）。
// 名单可扩展注册（新增参与组件/字段属加性演进、不得重构机制）；本单需求下限＝UnitStateData（三实时值）。
// W2b G3 接线（加性）：
// ①防御上限语义接入检测域：本体＝损伤量 loss（单位域），链起点＝「基准−损失」，落定输出＝当前值（数值表现钳制不低于 0）；
// ②有效值落定同步（IStatEffectiveValueSync＝可选扩展面）：机制在每轮管线落定后调用，组件把落定值回写其数据表示（表现位）；
// ③有效上限读取面（IStatUpdateDetector.ReadCapBaseValue 默认成员）：上限语义字段的「上限起点」声明
//   （有效上限＝上限起点＋Σ修饰；未声明上限语义的字段＝明确异常）；
// ④变更守卫（IStatChangeGuard＝可选扩展面）：字段运行期变更许可判定（死亡冻结——死亡后数值面冻结、
//   变更类操作拒绝；读取与内部清理不受此限）；机制在变更类操作前查询。
// W3-2 G5 接线（加性）：
// ⑤部署费检测组件（DeployCostUpdateDetector）——全类别一体适用（单位/指令/反制，含未单位化的单位；
//   不区分类别、不依赖单位化状态）：基准＝合并组件的定义静态值（S10 起：FactionCostData.DeployCost——
//   旧独立花费组件退役）；「有效部署费」＝链输出；快照/比较/集中触发沿用同一管线口径（变化字段集合含部署费标识）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 更新检测接口（W2a G3）：参与运行时数据变更的组件实现本接口，为机制提供「字段域声明＋基准读取＋快照生成＋快照比较」。
/// 字段标识与修饰器目标字段、载荷变化字段集合元素为同一标识体系（无映射层）。
/// 履约要点：
/// ①<b>就绪</b>（<see cref="IsReady"/>）＝本组件覆盖字段的基准当前可读（如必要数据组件在场）；缺必要组件＝false——
///   机制在挂载/读取路径据此拒绝（fail-fast、不静默）；true 时 <see cref="ReadBaseValue"/> 与
///   <see cref="GenerateSnapshot"/> 须可正常完成。
/// ②<b>生成快照</b>＝从本轮字段值集合（链输出全量值）为本组件覆盖字段生成新的只读快照（不得修改入参、不得复用入参引用）。
/// ③<b>比较快照</b>＝「新生成快照 vs 上次快照」的字段级比较；输出＝是否有变更＋字段级差异（方向：旧→新；
///   字段顺序＝本组件声明序）。不得修改任一入参。
/// ④失败＝fail-fast（异常向上传播；机制不得留下坏快照）。
/// </summary>
public interface IStatUpdateDetector
{
    /// <summary>覆盖字段标识（开放集合；声明序稳定——变化字段差异的排序来源之一）。</summary>
    IReadOnlyList<string> Fields { get; }

    /// <summary>就绪判定（缺必要组件＝false；true 时基准读取与快照生成须可正常完成）。</summary>
    bool IsReady { get; }

    /// <summary>
    /// 读取字段基准值（该字段未经修饰的基础值——链的起点；不含任何修饰器贡献）。
    /// 未就绪/未知字段＝明确异常（fail-fast；不得静默返回默认值）。
    /// </summary>
    int ReadBaseValue(string field);

    /// <summary>
    /// （W2b 加性·可选）读取字段的「上限起点」（有效上限＝上限起点＋Σ修饰的链——上限语义字段适用）：
    /// 与链起点（<see cref="ReadBaseValue"/>）区分——上限起点不含「损失」等派生修正（如防御：上限起点＝对战组件基准）。
    /// 语义＝只读；未声明上限语义的字段＝明确异常（fail-fast）；默认实现＝抛 NotSupportedException（无上限语义）。
    /// </summary>
    int ReadCapBaseValue(string field)
        => throw new NotSupportedException(
            $"字段 '{field}' 未声明上限语义（上限读取面不适用——fail-fast、不静默）。");

    /// <summary>
    /// 生成快照：从本轮字段值集合（机制喂入——含本组件全部覆盖字段）生成本组件覆盖字段的快照
    /// （只读、新对象；不修改入参）。机制在每轮管线内集中调用一次。
    /// </summary>
    /// <param name="values">本轮字段值集合（字段标识→值；全量——覆盖全部就绪检测组件的字段）。</param>
    IReadOnlyDictionary<string, int> GenerateSnapshot(IReadOnlyDictionary<string, int> values);

    /// <summary>
    /// 比较快照：新生成快照 vs 上次快照（比较基准）——输出是否有变更＋字段级差异（方向：旧→新）。
    /// 不修改任一入参；无副作用（不影响后续轮）。
    /// </summary>
    StatUpdateComparison Compare(
        IReadOnlyDictionary<string, int> newSnapshot, IReadOnlyDictionary<string, int> oldSnapshot);
}

/// <summary>
/// 快照比较输出（W2a G3）：是否有变更＋字段级差异（字段标识集合；方向：旧→新；顺序＝组件声明序——稳定可判别）。
/// 差异为开放集合——订阅方应容忍未知字段标识（新字段标识属加性演进）。
/// </summary>
public sealed class StatUpdateComparison
{
    /// <summary>创建比较输出。</summary>
    /// <param name="changedFields">字段级差异（无变化＝空集合；非 null）。</param>
    /// <exception cref="ArgumentNullException">changedFields 为 null。</exception>
    public StatUpdateComparison(IReadOnlyList<string> changedFields)
    {
        ArgumentNullException.ThrowIfNull(changedFields);
        ChangedFields = changedFields.ToArray(); // 防御性拷贝（输出稳定、不受调用方后续修改影响）
    }

    /// <summary>是否有变更（＝字段级差异非空）。</summary>
    public bool HasChanges => ChangedFields.Count > 0;

    /// <summary>字段级差异（无变化＝空集合；顺序＝组件声明序）。</summary>
    public IReadOnlyList<string> ChangedFields { get; }
}

/// <summary>
/// 有效值落定同步（W2b G3 加性；可选扩展面）：检测组件可实现本接口，在机制每轮管线落定后
/// 把最近一轮有效值同步到其数据表示（如「表现位」），使「组件状态」与「有效值」保持一致。
/// 机制在落定完成后调用（含无变更轮——实现应幂等）；同步失败＝fail-fast（与管线口径一致）。
/// </summary>
public interface IStatEffectiveValueSync
{
    /// <summary>
    /// 同步落定值（该检测组件覆盖字段的最新有效值——只读；实现不得修改入参、不得产生发射）。
    /// </summary>
    /// <param name="effectiveValues">该组件覆盖字段的最新有效值（字段标识→值；全量——快照口径，含数值表现钳制）。</param>
    void SyncEffectiveValues(IReadOnlyDictionary<string, int> effectiveValues);
}

/// <summary>
/// 变更守卫（W2b G3 加性；可选扩展面）：检测组件可实现本接口，为字段提供「运行期变更是否允许」的判定
/// （死亡冻结：死亡后数值面冻结——拒绝后续数值操作；读取与内部清理〔死亡清理〕不受此限）。
/// 机制在变更类操作（挂载）前查询；未实现＝恒允许。
/// </summary>
public interface IStatChangeGuard
{
    /// <summary>字段当前是否允许运行期变更（false＝冻结——变更类操作应被明确拒绝、零副作用）。</summary>
    bool IsChangeAllowed(string field);
}

/// <summary>
/// 单位实时值检测组件（W2a G3 内置成员；W2b 接线）：把「单位三实时值」
/// （<see cref="UnitStateData"/> 的行动费/攻击力/防御力）纳入更新检测。
/// 就绪条件＝UnitStateData 在场（单位化后；未单位化＝false——机制侧拒绝变更/读取，不静默）。
/// 基准（链起点）＝行动费/攻击力实时值当前读数；防御＝「对战组件基准−损伤量」
/// （W2b 上限语义：本体＝损伤量 loss，有效上限＝基准＋Σ加防修饰，当前值＝上限−loss）。
/// 快照（落定输出）＝当前值；防御的输出数值表现钳制不低于 0（逻辑 ≤0 由死亡判定收敛）。
/// 同步面（W2b）：实现 <see cref="IStatEffectiveValueSync"/>——跑链落定后把防御当前值写回数据组件表现位。
/// 上限起点（W2b）：防御的上限起点＝对战组件基准（有效上限读取面 <c>GetEffectiveCapValue</c> 用）；其余字段无上限语义。
/// 变更守卫（W2b）：实现 <see cref="IStatChangeGuard"/>——死亡冻结（已死亡/已毁＝字段变更不允许）。
/// 不硬编码「单位卡」类：仅依赖数据组件在场性（任意卡类装配 UnitStateData 即同样适用；HQ 落地后为其专属组件加新检测组件即可）。
/// W3-2：本组件仅覆盖单位三实时值（部署费域由独立组件 <see cref="DeployCostUpdateDetector"/> 承担——
/// 字段清单不再等同于 <see cref="CardStatFields.All"/>）。
/// </summary>
public sealed class UnitStateUpdateDetector : IStatUpdateDetector, IStatEffectiveValueSync, IStatChangeGuard
{
    /// <summary>单位三实时值字段清单（声明序稳定——变化字段差异的排序来源之一；W3-2 起＝本组件专属域，不含部署费）。</summary>
    private static readonly string[] UnitFieldList =
    {
        CardStatFields.OperateCost,
        CardStatFields.Attack,
        CardStatFields.Defense,
    };

    private readonly Card _card;

    /// <summary>创建检测组件（绑定卡；机制侧内部装配——全卡类构造期注册）。</summary>
    internal UnitStateUpdateDetector(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Fields => UnitFieldList;

    /// <inheritdoc />
    public bool IsReady => _card.TryGetData<UnitStateData>(out _);

    /// <inheritdoc />
    public int ReadBaseValue(string field)
    {
        var state = _card.GetData<UnitStateData>(); // 未就绪＝明确异常（不静默）
        return field switch
        {
            CardStatFields.OperateCost => state.OperateCost,
            CardStatFields.Attack => state.Attack,
            // 防御（W2b 上限语义）：链起点＝「对战组件基准−损伤量」（本体＝loss；随后 Σ加防修饰 → 当前值）。
            CardStatFields.Defense => _card.GetData<BattleStatsData>().Defense - state.DefenseLoss,
            _ => throw new ArgumentException(
                $"字段 '{field}' 不在单位实时值域（{string.Join(" / ", UnitFieldList)}）。", nameof(field)),
        };
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> GenerateSnapshot(IReadOnlyDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var snapshot = new Dictionary<string, int>(UnitFieldList.Length, StringComparer.Ordinal);
        foreach (var field in UnitFieldList)
        {
            var value = values[field]; // 机制喂入全量值（缺失＝机制契约违反——fail-fast、不静默）
            // 防御：数值表现钳制不低于 0（W2b 上限语义；逻辑 ≤0 由死亡判定收敛——表现面不出现负数）。
            snapshot[field] = string.Equals(field, CardStatFields.Defense, StringComparison.Ordinal)
                ? Math.Max(0, value)
                : value;
        }

        return snapshot;
    }

    /// <inheritdoc />
    public StatUpdateComparison Compare(
        IReadOnlyDictionary<string, int> newSnapshot, IReadOnlyDictionary<string, int> oldSnapshot)
    {
        ArgumentNullException.ThrowIfNull(newSnapshot);
        ArgumentNullException.ThrowIfNull(oldSnapshot);
        var changed = new List<string>();
        foreach (var field in UnitFieldList) // 稳定序＝声明序
        {
            if (newSnapshot[field] != oldSnapshot[field])
            {
                changed.Add(field);
            }
        }

        return new StatUpdateComparison(changed);
    }

    /// <inheritdoc />
    public void SyncEffectiveValues(IReadOnlyDictionary<string, int> effectiveValues)
    {
        ArgumentNullException.ThrowIfNull(effectiveValues);
        if (_card.TryGetData<UnitStateData>(out var state))
        {
            // 表现位（防御当前值）＝落定有效值（含钳制）；攻/行动费无独立表现位（有效值经读取口查询）。
            // 未单位化（组件缺席）＝无操作（机制调用前已就绪校验——此处为防御）。
            state.Defense = effectiveValues[CardStatFields.Defense];
        }
    }

    /// <inheritdoc />
    public int ReadCapBaseValue(string field)
    {
        if (!string.Equals(field, CardStatFields.Defense, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"字段 '{field}' 无上限语义（上限语义仅防御力——fail-fast、不静默）。");
        }

        // 上限起点（防御）＝对战组件基准（不含损伤量修正；有效上限＝上限起点＋Σ加防修饰）。
        return _card.GetData<BattleStatsData>().Defense;
    }

    /// <inheritdoc />
    public bool IsChangeAllowed(string field)
    {
        // 死亡冻结（W2b）：已死亡/已毁＝字段变更不允许（读取与内部清理不在本判定范围——调用方另行豁免）。
        return !_card.GetData<UnitStateData>().IsDestroyed;
    }
}

/// <summary>
/// 部署费检测组件（W3-2 G5 内置成员；S10 随改：基准读取点接改至合并组件 <see cref="FactionCostData"/>）：把「部署费」
/// （<see cref="FactionCostData.DeployCost"/> 基准——定义静态值〔本体可经卡侧受控门户修改、修改经管线传播〕）
/// 纳入更新检测；「有效部署费」＝链输出（修饰/设值/钳制等贡献叠加后的读取口径）。
/// 全类别一体适用：单位 / 指令 / 反制（含未单位化的单位）——部署费为全类别装配（构造期常驻），
/// 不区分类别、不依赖单位化状态。
/// 就绪条件＝FactionCostData 在场（全类别构造期常驻，恒就绪；缺组件＝false——机制侧拒绝变更/读取，不静默）。
/// 基准（链起点）＝部署费定义静态值；快照（落定输出）＝链输出（纯链结果、无全局数值规范——
/// 「不低于 0/1」等合法下限由使用处经钳制/设值表达，机制不内建费用域钳制）。
/// 无上限语义（ReadCapBaseValue 默认拒绝）、无表现位同步（部署费无独立表现位——有效值经读取口查询）、
/// 无变更守卫（费用域无死亡冻结语义——未实现 IStatChangeGuard 即恒允许）。
/// 字段清单＝单列（部署费）；修饰导致有效部署费变化时正常发射、载荷变化字段集合含部署费标识；无变化零发射（管线语义既有）。
/// </summary>
public sealed class DeployCostUpdateDetector : IStatUpdateDetector
{
    /// <summary>部署费字段清单（单列；声明序稳定）。</summary>
    private static readonly string[] DeployCostFieldList = { CardStatFields.DeployCost };

    private readonly Card _card;

    /// <summary>创建检测组件（绑定卡；机制侧内部装配——全卡类构造期注册）。</summary>
    internal DeployCostUpdateDetector(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Fields => DeployCostFieldList;

    /// <inheritdoc />
    public bool IsReady => _card.TryGetData<FactionCostData>(out _);

    /// <inheritdoc />
    public int ReadBaseValue(string field)
    {
        if (!string.Equals(field, CardStatFields.DeployCost, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"字段 '{field}' 不在部署费域（{CardStatFields.DeployCost}）。", nameof(field));
        }

        return _card.GetData<FactionCostData>().DeployCost; // 未就绪＝明确异常（不静默）
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> GenerateSnapshot(IReadOnlyDictionary<string, int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // 机制喂入全量值（缺失＝机制契约违反——fail-fast、不静默）；纯链结果、不施加领域钳制。
            [CardStatFields.DeployCost] = values[CardStatFields.DeployCost],
        };
    }

    /// <inheritdoc />
    public StatUpdateComparison Compare(
        IReadOnlyDictionary<string, int> newSnapshot, IReadOnlyDictionary<string, int> oldSnapshot)
    {
        ArgumentNullException.ThrowIfNull(newSnapshot);
        ArgumentNullException.ThrowIfNull(oldSnapshot);
        var changed = new List<string>();
        if (newSnapshot[CardStatFields.DeployCost] != oldSnapshot[CardStatFields.DeployCost])
        {
            changed.Add(CardStatFields.DeployCost); // 稳定序＝声明序
        }

        return new StatUpdateComparison(changed);
    }
}
