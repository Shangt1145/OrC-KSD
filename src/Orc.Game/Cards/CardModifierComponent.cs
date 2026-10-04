using Orc.Cards;
using Orc.Core;
using Orc.Game.Board;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// W2a G3 修饰机制核心（卡侧容器 / 链 / 管线）：
// 卡侧修饰器组件＝所有卡牌（任意卡类；含 HQ 等非卡实体）构造期常驻的专用轻量容器（非 AddData 数据组件——
// 不进装配/加载/快照语义；空状态零行为负担）。承载：修饰器集（登记序）＋更新检测组件名单＋各组件缓存
// ＋全量重跑链＋「每轮管线」（链式调用 → 更新检测接口生成/比较 → 有变更：更新缓存＋最后集中触发）。
// W3-3 加性（HQ 实体化）：宿主类型由 CardBase 泛化至引擎薄容器 Card——同一修饰/链/管线机制可作用于
// 非卡实体（HQ 等；Card 提供具名标识与数据组件容器面）；既有卡侧行为语义不变（CardBase 系 Card 子类，照常工作）。
// 触发语义：单一入口（RequestRerunAsync——一切「跑一轮」汇入）；修饰器增删在操作完成时自动衔接一轮
// （批量＝一次衔接、非逐条）；基准变化由调用方显式请求（本单不接既有流程调用点）。
// 收敛（P1）：无变更＝整体不动（缓存不替换、零发射、无其他副作用）；固定点＝稳定后不再触发。
// 同步（P2）：全链路同步/顺序语义（无异步结构、无延迟任务）；调用返回即终态（含发射完成——「先落定、后发射」）。
// 重入：跑链执行窗内禁止状态变更（挂载/注销/注册/重跑）——策略＝拒绝并明确错误（防嵌套与中间态）。
// 失败路径：链/检测/比较异常＝fail-fast 上抛、缓存不落定（不留半更新）；注销钩子异常＝隔离记录（列表一致性优先）；
// 挂载钩子异常＝该次挂载回滚为未挂载、批量整体回滚。
// 就绪：字段被名单检测组件覆盖 ∧ 该组件就绪（缺必要组件时挂载/跑链/读取明确错误、不静默；撤销/查询不设门槛）。
// W2b G3 接线（加性）：
// ①现算读取（ComputeEffectiveValue——读取时合成）：以当前状态对字段链现算一遍（不落定、不发射、纯读），
//   供「引用型修饰求值」等需要「同一轮内最新值」的取值场景（引用随动＝读源字段当前有效值）；
//   重入（引用链循环）＝拒绝并明确错误（防循环收敛属后续扩展点）。
// ②落定同步：实现 IStatEffectiveValueSync 的检测组件在每轮跑链落定后接收落定值（回写数据表示——表现位；幂等）。
// ③有效上限读取面（GetEffectiveCapValue）：上限起点（检测组件声明）＋Σ修饰的现算（只读、不落定不发射）。
// ④变更守卫（死亡冻结）：挂载类操作前经检测组件守卫（IStatChangeGuard）查询——死亡后数值面冻结＝明确拒绝；
//   撤销按「或无效」语义（死亡清理后集合为空＝幂等无操作、零副作用）。
// ⑤死亡清理（ClearAllForDeathAsync——死亡流程内部特殊路径）：注销全部修饰器（含期限订阅随销）
//   ＋数值整合至最终态（跑链落定/同步）但**不产生任何新发射**（发射契约以致死变更为界）。
// W3-1 G4 接线（加性）：
// ⑥光环收集合成（管线固定步骤——对每卡一致生效）：每字段链输出（基准 → Σ挂载修饰器）之后、快照生成之前，
//   经「卡→玩家→环境」读取场级收集面，现收集命中声明（过滤〔字段＋通用门禁＋谓词〕——步骤内建）并按登记序
//   依次合成；受益卡侧零结构增删（收集非卡上条目——集合恒不变、仅有效值随重跑变化）；无环境可达
//   （未加载/独立构造）＝无光环面（自然产出当前值、不抛错）；现算读取（ComputeEffectiveValue）同构合成。
//   相对序（固定、文档化）：先挂载修饰器（挂载序）、后光环收集（登记序）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 卡侧修饰器组件（W2a G3 修饰机制核心）：卡牌实例构造期常驻的专用轻量容器。公开面：
/// ①挂载（单条 <see cref="AddModifierAsync"/>／批量原子 <see cref="AddModifiersAsync"/>）；
/// ②注销（单条 <see cref="RemoveModifierAsync"/>／批量 <see cref="RemoveModifiersAsync"/>／按来源 <see cref="RemoveBySourceAsync"/>——幂等清理语义）；
/// ③跑链（统一入口 <see cref="RequestRerunAsync"/>）；④集中查询（<see cref="All"/>——可读出目标字段与来源）；
/// ⑤有效值读取（<see cref="GetEffectiveValue"/>——纯读）；⑥检测组件名单扩展注册（<see cref="RegisterDetector"/>）。
/// 「增删即生效」＝挂载/注销操作完成后自动衔接一轮（批量＝一次衔接）；「有变更才发」＝无变更零发射。
/// </summary>
public sealed class CardModifierComponent
{
    private readonly Card _card;
    private readonly LogicEngine _engine;
    private readonly List<Modifier> _modifiers = new(); // 修饰器集（登记序——链应用序与批量原子序的来源）
    private readonly List<IStatUpdateDetector> _detectors = new(); // 更新检测组件名单（注册序）
    private readonly Dictionary<IStatUpdateDetector, IReadOnlyDictionary<string, int>> _snapshots =
        new(ReferenceEqualityComparer.Instance); // 各组件缓存（上次快照；有效值与检测快照合一承载）
    private readonly HashSet<string> _computingFields = new(StringComparer.Ordinal); // 现算读取执行窗（引用链循环/重入防护；单线程语义）
    private bool _isRunning; // 跑链执行窗标志（重入保护；单线程语义）

    /// <summary>
    /// 创建卡侧修饰器组件（由卡牌基类构造装配；每卡实例恰一份；W3-3：宿主＝引擎薄容器 Card——含非卡实体如 HQ）。
    /// 内置注册：单位实时值检测组件（<see cref="UnitStateUpdateDetector"/>——需求下限「UnitStateData 三实时值」；
    /// 就绪＝UnitStateData 在场——未单位化时相关操作明确错误、不静默）
    /// ＋部署费检测组件（<see cref="DeployCostUpdateDetector"/>——W3-2 G5 加性：全类别费用域，
    /// 就绪＝CommandPointCostData 在场——全类别构造期常驻、含未单位化的单位）。
    /// </summary>
    internal CardModifierComponent(Card card, LogicEngine engine)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(engine);
        _card = card;
        _engine = engine;
        _detectors.Add(new UnitStateUpdateDetector(card));
        _detectors.Add(new DeployCostUpdateDetector(card)); // W3-2 G5 加性：部署费入链（全类别——构造期常驻）
    }

    /// <summary>修饰器全量列举（登记序；集中查询面——条目可读出目标字段标识与来源；空＝无修饰）。</summary>
    public IReadOnlyList<Modifier> All => _modifiers;

    // ---------- 检测组件名单（可扩展注册） ----------

    /// <summary>
    /// 注册更新检测组件（名单扩展面；新增参与组件/字段属加性演进）。注册序＝名单序（跑链遍历序）。
    /// 字段冲突（与既有名单重复）＝明确错误（同一字段不得双真源）；组件内字段重复＝拒绝。
    /// 注册不触发跑链（初始快照在首轮跑链时以基准状态建立）。
    /// </summary>
    /// <exception cref="ArgumentNullException">detector 为 null。</exception>
    /// <exception cref="ArgumentException">组件字段声明含 null/空白。</exception>
    /// <exception cref="InvalidOperationException">跑链执行中；或字段与既有名单冲突、组件内字段重复、字段声明为 null。</exception>
    public void RegisterDetector(IStatUpdateDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        EnsureNotRunning();

        var fields = detector.Fields;
        if (fields is null)
        {
            throw new InvalidOperationException(
                $"更新检测组件 '{detector.GetType().Name}' 的字段声明为 null（契约违反——fail-fast）。");
        }

        var declared = new List<string>();
        foreach (var field in fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            if (declared.Contains(field))
            {
                throw new InvalidOperationException(
                    $"更新检测组件 '{detector.GetType().Name}' 的字段声明含重复项 '{field}'（fail-fast）。");
            }

            if (FindDetector(field) is not null)
            {
                throw new InvalidOperationException(
                    $"字段 '{field}' 已被既有更新检测组件覆盖（同一字段不得双真源；卡牌 '{_card.Name}'）。");
            }

            declared.Add(field);
        }

        _detectors.Add(detector);
    }

    // ---------- 挂载 ----------

    /// <summary>
    /// 挂载单个修饰器（单条）：字段就绪校验 → 变更守卫校验（死亡冻结）→ 子类挂载钩子（含期限自订阅）→ 登记 → 自动衔接一轮。
    /// 幂等：已挂载本卡＝无操作（不重复贡献、不衔接）；跨卡（已挂载到另一张卡）＝明确错误（归属单卡）。
    /// 失败：钩子异常＝回滚为未挂载（不留半挂载状态）＋原异常上抛（fail-fast）；死亡冻结＝明确拒绝（零副作用）。
    /// </summary>
    /// <exception cref="ArgumentNullException">modifier 为 null。</exception>
    /// <exception cref="InvalidOperationException">跑链执行中；跨卡冲突；字段未被覆盖、基准未就绪或字段被变更守卫冻结（死亡冻结）。</exception>
    public Task AddModifierAsync(Modifier modifier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        EnsureNotRunning();

        if (modifier.HostCard is not null)
        {
            if (ReferenceEquals(modifier.HostCard, _card))
            {
                return Task.CompletedTask; // 幂等：已挂载本卡（不重复贡献、不衔接）
            }

            throw new InvalidOperationException(
                $"修饰器（字段 '{modifier.Field}'）已挂载到另一张卡牌（归属单卡；须先注销再挂载——fail-fast、不静默）。");
        }

        EnsureFieldReady(modifier.Field);
        EnsureFieldChangeAllowed(modifier.Field); // W2b：变更守卫（死亡冻结）——变更类操作前查询
        return AddModifierCoreAsync(modifier, ct);
    }

    /// <summary>
    /// 挂载批量修饰器（原子）：全批预检（跨卡/字段就绪/变更守卫〔死亡冻结〕）→ 按输入序逐条挂载（内部序＝输入序）→ 一次衔接。
    /// 任一失败＝整体回滚（已挂载条目经完整注销路径退回、不留部分条目）＋不衔接＋原异常上抛。
    /// 批内已挂载本卡的条目＝幂等跳过；全批均为幂等条目＝无状态变化（不衔接）。
    /// </summary>
    /// <exception cref="ArgumentNullException">modifiers 为 null。</exception>
    /// <exception cref="ArgumentException">集合含 null 元素。</exception>
    /// <exception cref="InvalidOperationException">跑链执行中；跨卡冲突；字段未被覆盖、基准未就绪或字段被变更守卫冻结（死亡冻结）。</exception>
    public Task AddModifiersAsync(IEnumerable<Modifier> modifiers, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        EnsureNotRunning();

        var batch = new List<Modifier>();
        foreach (var modifier in modifiers)
        {
            if (modifier is null)
            {
                throw new ArgumentException("修饰器集合含 null 元素（批量为明确列举——fail-fast）。", nameof(modifiers));
            }

            batch.Add(modifier);
        }

        if (batch.Count == 0)
        {
            return Task.CompletedTask; // 空批＝无操作
        }

        foreach (var modifier in batch) // 预检（全批；不做任何状态修改——原子性前置）
        {
            if (modifier.HostCard is not null)
            {
                if (!ReferenceEquals(modifier.HostCard, _card))
                {
                    throw new InvalidOperationException(
                        $"修饰器（字段 '{modifier.Field}'）已挂载到另一张卡牌（归属单卡；批量预检拒绝——fail-fast）。");
                }

                continue; // 已挂载本卡：幂等条目（执行时跳过）
            }

            EnsureFieldReady(modifier.Field);
            EnsureFieldChangeAllowed(modifier.Field); // W2b：变更守卫（死亡冻结）——变更类操作前查询
        }

        return AddModifiersCoreAsync(batch, ct);
    }

    // ---------- 注销 ----------

    /// <summary>
    /// 注销单个修饰器（单条）：子类注销钩子（退订/清理——自托管）→ 移出登记 → 自动衔接一轮。
    /// 幂等：未挂载/已移除/重复注销＝无操作（不衔接）。清理钩子异常＝隔离记录（列表一致性优先——仍完成移除与衔接）。
    /// </summary>
    /// <exception cref="ArgumentNullException">modifier 为 null。</exception>
    /// <exception cref="InvalidOperationException">跑链执行中。</exception>
    public Task RemoveModifierAsync(Modifier modifier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        EnsureNotRunning();

        if (!_modifiers.Contains(modifier))
        {
            return Task.CompletedTask; // 幂等：未挂载/已移除＝无操作（不衔接）
        }

        return RemoveModifierCoreAsync(modifier, ct);
    }

    /// <summary>
    /// 注销批量修饰器（单交接点）：命中条目逐条清理（异常隔离记录）→ 一次衔接。
    /// 全未命中＝幂等无操作（不衔接）；同实例重复出现在输入＝只处理一次。
    /// </summary>
    /// <exception cref="ArgumentNullException">modifiers 为 null。</exception>
    /// <exception cref="ArgumentException">集合含 null 元素。</exception>
    /// <exception cref="InvalidOperationException">跑链执行中。</exception>
    public Task RemoveModifiersAsync(IEnumerable<Modifier> modifiers, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        EnsureNotRunning();

        var hits = new List<Modifier>();
        foreach (var modifier in modifiers)
        {
            if (modifier is null)
            {
                throw new ArgumentException("修饰器集合含 null 元素（批量为明确列举——fail-fast）。", nameof(modifiers));
            }

            if (_modifiers.Contains(modifier) && !hits.Contains(modifier))
            {
                hits.Add(modifier);
            }
        }

        if (hits.Count == 0)
        {
            return Task.CompletedTask; // 全未命中＝幂等无操作（不衔接）
        }

        return RemoveModifiersCoreAsync(hits, ct);
    }

    /// <summary>
    /// 按来源撤销（本卡内）：撤销该来源在本卡上的全部修饰器条目（组语义）→ 一次衔接。
    /// 来源相等性判据＝引用相等（同一对象实例视为同一来源）。
    /// 该来源无修饰＝幂等无操作（不衔接）。跨卡批量撤销不在本单（使用处逐卡调用）。
    /// </summary>
    /// <exception cref="ArgumentNullException">source 为 null。</exception>
    /// <exception cref="InvalidOperationException">跑链执行中。</exception>
    public Task RemoveBySourceAsync(object source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureNotRunning();

        var hits = new List<Modifier>();
        foreach (var modifier in _modifiers)
        {
            if (ReferenceEquals(modifier.Source, source))
            {
                hits.Add(modifier);
            }
        }

        if (hits.Count == 0)
        {
            return Task.CompletedTask; // 幂等：该来源无修饰＝无操作
        }

        return RemoveModifiersCoreAsync(hits, ct);
    }

    // ---------- 跑链（统一入口） ----------

    /// <summary>
    /// 请求重跑（统一入口）：一切「跑一轮」汇入本方法（修饰器增删自动衔接；基准变化由调用方显式请求）。
    /// 每轮＝全量重跑（基准 → 链 → 有效值）→ 更新检测（生成/比较，集中调用一次）→ 有变更：更新缓存＋最后集中触发；
    /// 无变更：整体不动（零发射、无副作用）。
    /// 重入策略＝拒绝并明确错误（跑链执行窗内再次请求或被并发请求均同步拒绝——防嵌套与中间态）。
    /// </summary>
    /// <exception cref="InvalidOperationException">跑链执行中（重入）；或无就绪检测组件（缺必要基准/组件——不得静默）。</exception>
    public Task RequestRerunAsync(CancellationToken ct = default)
    {
        EnsureNotRunning();
        return RequestRerunCoreAsync(ct);
    }

    // ---------- 有效值读取（纯读） ----------

    /// <summary>
    /// 读取字段有效值（纯读：不触发跑链、无副作用）：与「最近一轮有效值」一致（有变更轮随新值；无变更轮不变）；
    /// 就绪但未跑过链＝基准值（初始快照口径）；无修饰卡＝基准值。
    /// 字段未被覆盖或基准未就绪＝明确错误（不静默返回错值）。
    /// </summary>
    /// <exception cref="ArgumentException">field 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">字段未被任何检测组件覆盖；或基准来源未就绪。</exception>
    public int GetEffectiveValue(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var detector = FindDetector(field);
        if (detector is null)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 未被任何更新检测组件覆盖（卡牌 '{_card.Name}'——无基准来源，无法提供有效值）。");
        }

        if (!detector.IsReady)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 的基准来源未就绪（卡牌 '{_card.Name}'——缺必要组件；禁止静默返回错值）。");
        }

        if (_snapshots.TryGetValue(detector, out var snapshot))
        {
            return snapshot[field]; // 最近一轮有效值（缓存；与检测快照合一承载）
        }

        return detector.ReadBaseValue(field); // 就绪但未跑过链：基准值（初始快照口径）
    }

    /// <summary>
    /// 现算有效值（读取时合成；W2b 加性——纯读：不落定、不发射、不触发跑链）：
    /// 以「当前状态」对目标字段的链现算一遍（基准 → Σ修饰 → 输出规范化），返回该字段的当前有效值。
    /// 与 <see cref="GetEffectiveValue"/>（缓存口径）的差异：本方法不受「最近一轮落定」时点约束——
    /// 供「引用型修饰求值」等需要「同一轮内最新值」的取值场景（引用随动＝读源字段当前有效值；无需依赖传播/订阅机制）。
    /// 仅目标字段参与求值（其余字段以基准值占位——仅满足快照生成的「全量喂入」契约、不触发其他字段的修饰链求值；
    /// 快照仅取目标字段）。
    /// 重入防护：现算执行窗内对同一字段再次现算＝引用链循环/重入——拒绝并明确错误（fail-fast；
    /// 防循环收敛属后续扩展点）。字段未被覆盖或基准未就绪＝明确错误（不静默）。
    /// </summary>
    /// <exception cref="ArgumentException">field 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">字段未被任何检测组件覆盖；基准来源未就绪；引用链循环/重入。</exception>
    public int ComputeEffectiveValue(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var detector = FindDetector(field);
        if (detector is null)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 未被任何更新检测组件覆盖（卡牌 '{_card.Name}'——无基准来源，无法现算有效值）。");
        }

        if (!detector.IsReady)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 的基准来源未就绪（卡牌 '{_card.Name}'——缺必要组件；禁止静默返回错值）。");
        }

        if (!_computingFields.Add(field))
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 的现算读取已在执行中（卡牌 '{_card.Name}'——引用链循环/重入；fail-fast、拒绝并明确错误）。");
        }

        try
        {
            // 现算目标字段的链（ReadBaseValue → Σ修饰）；其余字段以基准值占位——满足快照生成的「全量喂入」契约、
            // 且不触发其他字段的修饰链求值（防引用链的无谓嵌套；快照仅取目标字段、其余字段值不参与输出）。
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var componentField in detector.Fields)
            {
                if (!string.Equals(componentField, field, StringComparison.Ordinal))
                {
                    values[componentField] = detector.ReadBaseValue(componentField);
                    continue;
                }

                var value = detector.ReadBaseValue(componentField);
                foreach (var modifier in _modifiers)
                {
                    if (!string.Equals(modifier.Field, componentField, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    value = modifier.Apply(_card, value); // 链节＝纯变换（以卡牌引用为参数）
                }

                value = ApplyAuraContributions(componentField, value); // W3-1：光环收集合成（与主链同构）
                values[componentField] = value;
            }

            var snapshot = detector.GenerateSnapshot(values);
            if (snapshot is null)
            {
                throw new InvalidOperationException(
                    $"更新检测组件 '{detector.GetType().Name}' 生成快照返回 null（契约违反——fail-fast）。");
            }

            return snapshot[field]; // 输出规范化后的当前有效值（含数值表现钳制）
        }
        finally
        {
            _computingFields.Remove(field); // 任何路径结束后不留中间态、可再次现算
        }
    }

    /// <summary>
    /// 读取「有效上限」（只读查询面；W2b 加性——纯读：不落定、不发射、不触发跑链）：
    /// 有效上限＝上限起点（检测组件声明的 <see cref="IStatUpdateDetector.ReadCapBaseValue"/>——如防御＝对战组件基准）
    /// ＋Σ修饰（对上限起点的链现算；不含「损失」等派生修正——与当前值读取面并列区分）。
    /// 不提供任意写（变更仍经门户/修饰）。字段未覆盖、基准未就绪或未声明上限语义＝明确错误（不静默）。
    /// 重入防护与现算共享（同字段现算窗——引用链循环/重入＝拒绝并明确错误）。
    /// </summary>
    /// <exception cref="ArgumentException">field 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">字段未被任何检测组件覆盖；基准来源未就绪；引用链循环/重入。</exception>
    /// <exception cref="NotSupportedException">字段未声明上限语义（检测组件默认成员——fail-fast）。</exception>
    public int GetEffectiveCapValue(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var detector = FindDetector(field);
        if (detector is null)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 未被任何更新检测组件覆盖（卡牌 '{_card.Name}'——无基准来源，无法提供有效上限）。");
        }

        if (!detector.IsReady)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 的基准来源未就绪（卡牌 '{_card.Name}'——缺必要组件；禁止静默返回错值）。");
        }

        if (!_computingFields.Add(field))
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 的现算读取已在执行中（卡牌 '{_card.Name}'——引用链循环/重入；fail-fast、拒绝并明确错误）。");
        }

        try
        {
            var value = detector.ReadCapBaseValue(field); // 未声明上限语义＝明确异常（fail-fast）
            foreach (var modifier in _modifiers)
            {
                if (string.Equals(modifier.Field, field, StringComparison.Ordinal))
                {
                    value = modifier.Apply(_card, value); // 链节＝纯变换（以卡牌引用为参数）
                }
            }

            return value; // 有效上限：上限起点＋Σ修饰（无「损失」修正；读取口径不施加输出钳制）
        }
        finally
        {
            _computingFields.Remove(field); // 任何路径结束后不留中间态、可再次现算
        }
    }

    // ---------- 内部：挂载/注销执行 ----------

    private async Task AddModifierCoreAsync(Modifier modifier, CancellationToken ct)
    {
        MountModifier(modifier);
        _modifiers.Add(modifier);
        await RequestRerunAsync(ct); // 增删即生效：操作完成时自动衔接一轮
    }

    private async Task AddModifiersCoreAsync(List<Modifier> batch, CancellationToken ct)
    {
        var mountedNow = new List<Modifier>();
        foreach (var modifier in batch)
        {
            if (modifier.HostCard is not null)
            {
                continue; // 幂等条目（已挂载本卡）
            }

            try
            {
                MountModifier(modifier);
                _modifiers.Add(modifier);
                mountedNow.Add(modifier);
            }
            catch
            {
                foreach (var mounted in mountedNow) // 原子回滚：本批已挂载条目全部退回（不留部分条目、不衔接）
                {
                    UnmountModifier(mounted);
                }

                throw;
            }
        }

        if (mountedNow.Count == 0)
        {
            return; // 全幂等（无状态变化）：不衔接
        }

        await RequestRerunAsync(ct); // 一次衔接（非逐条）
    }

    private async Task RemoveModifierCoreAsync(Modifier modifier, CancellationToken ct)
    {
        UnmountModifier(modifier);
        await RequestRerunAsync(ct); // 增删即生效：操作完成时自动衔接一轮
    }

    private async Task RemoveModifiersCoreAsync(List<Modifier> hits, CancellationToken ct)
    {
        foreach (var modifier in hits)
        {
            UnmountModifier(modifier); // 逐条清理、异常隔离（列表一致性保证）
        }

        await RequestRerunAsync(ct); // 单交接点：一次完成、一次衔接
    }

    // ---------- 死亡清理（W2b；死亡流程内部特殊路径） ----------

    /// <summary>
    /// 死亡清理（W2b；死亡流程内部特殊路径——不受变更守卫〔死亡冻结〕限制，为内部流程例外）：
    /// 注销全部修饰器（子类钩子自清理——含期限订阅随销；清理异常隔离记录、列表一致性优先）
    /// → 修饰集合清空 → 数值整合至最终态（全量重跑 + 落定/同步；**不产生任何新发射**——发射契约以致死变更为界）。
    /// 幂等（无修饰/重复调用＝空清理 + 整合零发射）；跑链执行窗内调用＝拒绝（与其它操作一致）。
    /// </summary>
    /// <exception cref="InvalidOperationException">跑链执行中（重入）。</exception>
    internal async Task ClearAllForDeathAsync(CancellationToken ct = default)
    {
        EnsureNotRunning();

        var toClear = _modifiers.ToArray();
        foreach (var modifier in toClear)
        {
            UnmountModifier(modifier); // 逐条清理、异常隔离（含期限订阅随销）
        }

        _modifiers.Clear();
        await RequestRerunCoreAsync(ct, emitChanges: false); // 数值整合至最终态：跑链 + 落定/同步、零发射
    }

    /// <summary>挂载执行（含失败回滚）：子类钩子异常＝回滚为未挂载（尽力清理半挂载资源）＋原异常上抛。</summary>
    private void MountModifier(Modifier modifier)
    {
        var context = new ModifierMountContext(_card, _engine, selfCt => RemoveModifierAsync(modifier, selfCt));
        modifier.MarkMounted(_card);
        try
        {
            modifier.OnMount(context); // 子类自写挂载（含期限自订阅）
        }
        catch
        {
            try
            {
                modifier.OnUnmount(); // 失败挂载的回滚清理（可能已建立部分资源；子类钩子须防御）
            }
            catch (Exception ex)
            {
                WriteCleanupError(modifier, ex);
            }

            modifier.ClearMounted();
            throw;
        }
    }

    /// <summary>注销执行：子类钩子（异常＝隔离记录）→ 移出登记 → 清归属。幂等（列表移除与归属清空皆幂等）。</summary>
    private void UnmountModifier(Modifier modifier)
    {
        try
        {
            modifier.OnUnmount(); // 子类自写注销（自托管：退订/清理——「注销后无残留」）
        }
        catch (Exception ex)
        {
            WriteCleanupError(modifier, ex); // 清理异常隔离（列表一致性优先——仍完成移除与衔接）
        }

        _modifiers.Remove(modifier);
        modifier.ClearMounted();
    }

    // ---------- 内部：管线（全量重跑 → 检测 → 落定 → 发射） ----------

    private async Task RequestRerunCoreAsync(CancellationToken ct, bool emitChanges = true)
    {
        _isRunning = true;
        bool hasChanges;
        IReadOnlyList<string> changedFields;
        try
        {
            (hasChanges, changedFields) = RunChainAndDetect();
        }
        finally
        {
            _isRunning = false; // 任何路径结束后不留中间态、可再次请求
        }

        if (hasChanges && emitChanges)
        {
            // 管线末步（「最后集中触发」）：快照/有效值已落定、状态一致后再发射；发射失败≠状态失败（无重试/补偿）。
            // （emitChanges=false＝死亡清理等内部整合路径：数值整合至最终态但不发射——发射契约以致死变更为界。）
            await GameUpdates.EmitCardStatChanged(_engine, _card, changedFields, ct);
        }
    }

    /// <summary>每轮内核：链式调用（全量重跑）→ 逐组件「生成 → 比较」→ 有变更则统一落定（先全量计算、后落定——异常不留半更新）。</summary>
    private (bool HasChanges, IReadOnlyList<string> ChangedFields) RunChainAndDetect()
    {
        // ① 就绪域（名单序）：全无就绪＝缺必要基准/组件——明确错误（不得静默零产出）
        var ready = new List<IStatUpdateDetector>();
        foreach (var detector in _detectors)
        {
            if (detector.IsReady)
            {
                ready.Add(detector);
            }
        }

        if (ready.Count == 0)
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 无就绪的更新检测组件（缺必要基准/组件——禁止静默产出；先装配所需数据组件或注册检测组件）。");
        }

        foreach (var modifier in _modifiers) // 防御：已挂修饰器目标字段必须仍有就绪基准（防静默丢失贡献）
        {
            EnsureFieldReady(modifier.Field);
        }

        // ② 链式调用：全量重跑（基准 → 链 → 有效值；字段无关、逐字段独立、挂载序确定变换）
        //    W3-1 G4：链输出后合成光环收集贡献（管线固定步骤——对每卡一致生效；受益侧零增删）
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var detector in ready)
        {
            foreach (var field in detector.Fields)
            {
                var value = detector.ReadBaseValue(field);
                foreach (var modifier in _modifiers)
                {
                    if (!string.Equals(modifier.Field, field, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    value = modifier.Apply(_card, value); // 链节＝纯变换（以卡牌引用为参数）
                }

                value = ApplyAuraContributions(field, value); // W3-1：光环收集合成（固定序：挂载修饰器之后）
                values[field] = value;
            }
        }

        // ③ 更新检测：按名单逐组件「生成 → 比较」（每轮集中调用一次；汇聚总差异与变更判定）
        var pending = new List<(IStatUpdateDetector Detector,
            IReadOnlyDictionary<string, int> NewSnapshot,
            IReadOnlyDictionary<string, int> OldSnapshot)>();
        var changed = new List<string>();
        foreach (var detector in ready)
        {
            var newSnapshot = detector.GenerateSnapshot(values);
            if (newSnapshot is null)
            {
                throw new InvalidOperationException(
                    $"更新检测组件 '{detector.GetType().Name}' 生成快照返回 null（契约违反——fail-fast）。");
            }

            if (!_snapshots.TryGetValue(detector, out var oldSnapshot))
            {
                oldSnapshot = GenerateBaselineSnapshot(detector); // 首轮：初始快照＝基准状态（比较基线）
            }

            var comparison = detector.Compare(newSnapshot, oldSnapshot);
            if (comparison is null)
            {
                throw new InvalidOperationException(
                    $"更新检测组件 '{detector.GetType().Name}' 比较返回 null（契约违反——fail-fast）。");
            }

            pending.Add((detector, newSnapshot, oldSnapshot));
            changed.AddRange(comparison.ChangedFields);
        }

        // ④ 落定：有变更＝各组件缓存替换为新一轮快照（有效值承载随之就绪）；无变更＝整体不动
        var hasChanges = changed.Count > 0;
        if (hasChanges)
        {
            foreach (var entry in pending)
            {
                _snapshots[entry.Detector] = entry.NewSnapshot;
            }
        }
        else
        {
            foreach (var entry in pending) // 首轮无变化：以基准状态建立持久初始快照（此后每轮「新 vs 上次」）
            {
                if (!_snapshots.ContainsKey(entry.Detector))
                {
                    _snapshots[entry.Detector] = entry.OldSnapshot;
                }
            }
        }

        // ⑤ 落定同步（W2b 加性）：实现同步接口的检测组件把落定值回写其数据表示（表现位；幂等——含无变更轮）
        foreach (var entry in pending)
        {
            if (entry.Detector is IStatEffectiveValueSync sync)
            {
                sync.SyncEffectiveValues(entry.NewSnapshot);
            }
        }

        return (hasChanges, changed);
    }

    /// <summary>生成「基准状态」快照（首轮比较基线：各字段基准值经组件自身生成）。</summary>
    private IReadOnlyDictionary<string, int> GenerateBaselineSnapshot(IStatUpdateDetector detector)
    {
        var baseValues = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in detector.Fields)
        {
            baseValues[field] = detector.ReadBaseValue(field);
        }

        return detector.GenerateSnapshot(baseValues);
    }

    // ---------- 内部：校验与辅助 ----------

    /// <summary>重入保护（跑链执行窗内禁止状态变更——策略＝拒绝并明确错误）。</summary>
    private void EnsureNotRunning()
    {
        if (_isRunning)
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 的修饰链正在执行中，暂不允许状态变更（挂载/注销/注册/重跑；"
                + "重入策略＝拒绝并明确错误——防嵌套与中间态）。");
        }
    }

    /// <summary>字段就绪校验：覆盖（名单内组件声明）∧ 该组件就绪（缺必要组件≠静默）。</summary>
    private void EnsureFieldReady(string field)
    {
        var detector = FindDetector(field);
        if (detector is null)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 未被任何更新检测组件覆盖（卡牌 '{_card.Name}'——未知字段域，操作被拒绝）。");
        }

        if (!detector.IsReady)
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 的基准来源未就绪（卡牌 '{_card.Name}'——缺必要组件；未就绪时明确错误、不静默）。");
        }
    }

    /// <summary>
    /// 变更守卫校验（W2b）：字段被检测组件的变更守卫（<see cref="IStatChangeGuard"/>）判定为冻结
    /// （如死亡冻结）→ 明确拒绝（fail-fast、零副作用）。未实现守卫＝恒允许。读取与死亡清理不受此限。
    /// </summary>
    private void EnsureFieldChangeAllowed(string field)
    {
        if (FindDetector(field) is IStatChangeGuard guard && !guard.IsChangeAllowed(field))
        {
            throw new InvalidOperationException(
                $"字段 '{field}' 的数值面已冻结（卡牌 '{_card.Name}'——死亡后数值面冻结、变更操作被拒绝；fail-fast、零副作用）。");
        }
    }

    /// <summary>查找覆盖该字段的名单组件（名单序首个；注册冲突校验保证一一对应）。</summary>
    private IStatUpdateDetector? FindDetector(string field)
    {
        foreach (var detector in _detectors)
        {
            foreach (var declared in detector.Fields)
            {
                if (string.Equals(declared, field, StringComparison.Ordinal))
                {
                    return detector;
                }
            }
        }

        return null;
    }

    // ---------- 内部：光环收集合成（W3-1 G4） ----------

    /// <summary>
    /// 光环收集合成（管线固定步骤——对每卡一致生效）：经「卡→玩家→环境」读取场级收集面，
    /// 现收集命中声明（过滤〔字段匹配＋通用门禁＋声明谓词〕——步骤内建）并按登记序依次施加贡献
    /// （纯变换；稳定序＝收集面登记序——确定变换）。
    /// 零增删：收集不产生任何链上条目变化（受益卡侧集合恒不变——仅有效值随重跑变化）。
    /// 无环境可达（未加载/独立构造）＝无光环面——产出当前值（自然原值、不抛错）。
    /// 相对序（固定、文档化）：先挂载修饰器（挂载序）、后光环收集（登记序）。
    /// </summary>
    private int ApplyAuraContributions(string field, int current)
    {
        var environment = GameEnvironment.ResolveFor(_card);
        if (environment is null)
        {
            return current; // 无环境面：自然产出当前值
        }

        var contributions = environment.CollectAuras(_card, field);
        foreach (var declaration in contributions)
        {
            current = declaration.Transform(_card, current); // 贡献合成（按登记序依次施加）
        }

        return current;
    }

    /// <summary>注销/回滚清理异常＝隔离记录（写引擎总流 Error；不阻断清理链路与衔接）。</summary>
    private void WriteCleanupError(Modifier modifier, Exception ex)
    {
        _engine.RootStream.WriteLog(
            "修饰器注销",
            ex.Message,
            LogLevel.Error,
            new[] { $"exception:{ex.GetType().Name}" },
            new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message,
                ["field"] = modifier.Field,
            });
    }
}
