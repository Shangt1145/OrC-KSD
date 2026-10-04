using Orc.Cards;
using Orc.Core;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// W2a G3 修饰机制核心（修饰器侧）：
// 修饰器＝施加于卡上、对目标字段施加变换的「运行时叠加物」（方案 A；轻量伴生、非 DataComponent）。
// W3-3 加性（HQ 实体化）：宿主类型由 CardBase 泛化至引擎薄容器 Card——同一修饰/链/管线机制可作用于
// 非卡实体（HQ 等；Card 提供具名标识与数据组件容器面）；既有卡侧行为语义不变（CardBase 系 Card 子类，照常工作）。
// 体系形态（设计定稿）：基类（挂载抽象方法＋注销面）＋模板子类（加法型/常量设值型/回调型；引用型留结构位）
// ＋使用处直接 new（注入回调与具体数值）。
// W3-1 加性（G4 贡献节·条件评估）：第 7 个并列成员 <see cref="ConditionalModifier"/>——条件成立施加贡献、
// 不成立产出原值（自然结果、无增删成本）；条件读「本卡＋经环境查询面的环境」（在前线/相邻计数等）；
// 与固定值修饰器共享同一生命周期语义（唯一差异＝变换前多一道条件求值）。
// 生命周期＝自托管：期限＝自订阅相位（G9 面）自注销；注销逻辑由子类自写（OnUnmount 钩子——机制统一调用）。
// 来源标记＝任意引用对象（施加方标识；相等性判据＝引用相等）；按来源撤销由其取回全部条目。
// 链节（Apply）＝以卡牌引用为参数、对「目标字段当前累积值」施加变换的纯变换（不发射更新/不改其他数据/不做订阅）。
// 字段标识＝开放集合（<see cref="CardStatFields"/> 为主题域便捷常量；机制对任意标识统一处理）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 数值字段标识常量集（W2a G3 修饰机制；W3-2 加性：扩入部署费——含未单位化的单位，全类别适用；
/// W3-3 加性：扩入 HQ 血量域——独立域常量、不并入 <see cref="All"/>）。
/// 形态＝稳定字符串（跨轮不变、可判别——订阅方据此识别变化字段）；全机制同一标识体系贯穿
/// （修饰器目标字段 / 检测快照字段 / 载荷变化字段集合元素——无映射层）。
/// 标识字符串取既有属性命名口径（<see cref="UnitStateData.OperateCost"/> 等）；
/// 集合为开放集合——后续新增字段标识属加性演进（独立域字段以独立常量承载）。
/// </summary>
public static class CardStatFields
{
    /// <summary>行动费（单位实时值；对应 <see cref="UnitStateData.OperateCost"/>）。</summary>
    public const string OperateCost = "OperateCost";

    /// <summary>攻击力（单位实时值；对应 <see cref="UnitStateData.Attack"/>）。</summary>
    public const string Attack = "Attack";

    /// <summary>防御力（单位实时值；对应 <see cref="UnitStateData.Defense"/>）。</summary>
    public const string Defense = "Defense";

    /// <summary>部署费（W3-2 G5 加性；全类别卡牌值——含未单位化的单位；对应 <see cref="CommandPointCostData.DeployCost"/> 基准，「有效部署费」＝链输出）。</summary>
    public const string DeployCost = "DeployCost";

    /// <summary>
    /// HQ 血量域（W3-3 G11 加性——HQ 实体化）：HQ 血量「防御力」的机制字段
    /// （「获得 +X 防御力」＝HQ 血量 +X——单一当前值模型、无上限语义；
    /// 对应 <see cref="Orc.Game.Players.HqStateData.Health"/>）。
    /// 独立域：不并入 <see cref="All"/>（避免与单位/费用检测组件字段声明重叠——各自独立注册、互不冲突）。
    /// </summary>
    public const string HqHealth = "HqHealth";

    /// <summary>全部既知字段标识（开放集合；声明序＝行动费/攻击/防御/部署费——变化字段集合的稳定序来源之一）。
    /// 独立域字段（如 <see cref="HqHealth"/>）不并入本列表。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { OperateCost, Attack, Defense, DeployCost };
}

/// <summary>
/// 期限声明（W2a G3；修饰器可选构造参数）：到期相位标识＋可选载荷过滤器。
/// 语义＝修饰器挂载后自订阅该相位（总线事件——任意更新标识、不设白名单）；相位到达（且过滤器命中）＝到期，
/// 修饰器经统一注销路径自注销（含自动衔接一轮）。
/// 过滤器＝使用处注入的载荷判定（机制不内建「归属/友方」等语义——如「下个己方回合开始」由使用处按载荷过滤；
/// null＝不过滤、任意该相位更新均触达）。
/// 多阶段订阅（同一修饰器多期限）本单不支持——单一期限声明＝首到期即撤（语义单一、无歧义）。
/// </summary>
public sealed class ModifierExpiry
{
    /// <summary>创建期限声明。</summary>
    /// <param name="phase">到期相位标识（更新字符串；非 null/空白——空声明将造成「永不触发」的静默订阅，fail-fast 拒绝）。</param>
    /// <param name="filter">载荷过滤器（可选；null＝不过滤；判定由使用处提供——机制不内建归属语义）。</param>
    /// <exception cref="ArgumentException">phase 为 null/空白。</exception>
    public ModifierExpiry(string phase, Func<IReadOnlyDictionary<string, object?>?, bool>? filter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        Phase = phase;
        Filter = filter;
    }

    /// <summary>到期相位标识（更新字符串；比较口径＝总线同一性——ordinal、大小写敏感）。</summary>
    public string Phase { get; }

    /// <summary>载荷过滤器（可选；null＝不过滤）。</summary>
    public Func<IReadOnlyDictionary<string, object?>?, bool>? Filter { get; }
}

/// <summary>
/// 修饰器挂载环境（W2a G3；机制在挂载操作时统一创建并传入）：挂载上下文下限承载——
/// ①卡引用（链节与变换的基础）；②引擎引用（订阅通道——期限自订阅所需；缺失＝结构性错误，fail-fast）；
/// ③自注销请求（RequestSelfUnmountAsync——修饰器「自注销」（如期限到期）经此汇入机制统一注销路径，含自动衔接一轮）。
/// 创建限机制内部（卡侧容器在挂载操作时构造）；修饰器侧只见本上下文。
/// </summary>
public sealed class ModifierMountContext
{
    private readonly Func<CancellationToken, Task> _requestSelfUnmount;

    internal ModifierMountContext(Card card, LogicEngine engine, Func<CancellationToken, Task> requestSelfUnmount)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(requestSelfUnmount);
        Card = card;
        Engine = engine;
        _requestSelfUnmount = requestSelfUnmount;
    }

    /// <summary>挂载卡引用（本修饰器所归属的卡牌；W3-3：宿主＝引擎薄容器 Card——含非卡实体如 HQ）。</summary>
    public Card Card { get; }

    /// <summary>引擎引用（订阅通道——期限自订阅等专属资源所需；恒非空）。</summary>
    public LogicEngine Engine { get; }

    /// <summary>
    /// 请求自注销（修饰器主动撤下自身；如期限到期）：汇入机制统一注销路径——调用子类注销钩子（OnUnmount）→ 从卡上移除 →
    /// 自动衔接一轮（有变更才发）。未挂载/已卸载时＝幂等无操作（机制侧判定）。
    /// </summary>
    public Task RequestSelfUnmountAsync(CancellationToken ct = default) => _requestSelfUnmount(ct);
}

/// <summary>
/// 修饰器基类（W2a G3；方案 A 内核）：定义「挂载抽象方法（<see cref="OnMount"/>）＋注销面（<see cref="OnUnmount"/>）」
/// 与链节（<see cref="Apply"/>）。
/// 生命周期契约（机制统一调用、子类不自行调度）：
/// ①挂载＝卡侧容器在挂载操作中调用 <see cref="OnMount"/>（此时归属与上下文已就绪；子类自写专属挂载逻辑——
///   如期限自订阅〈<see cref="SubscribeExpiry"/>〉）；挂载失败（异常）＝该次挂载整体回滚为未挂载、fail-fast 上抛。
/// ②注销＝机制统一路径（使用处显式撤销 / 期限到期 / 按来源撤销 / 批量）调用 <see cref="OnUnmount"/>——
///   子类自写清理（退订等）、保证「注销后无残留」；未挂载即注销＝幂等无操作；重复注销＝幂等。
/// ③链节＝纯变换（以卡牌引用为参数、对当前累积值施加变换；不得发射更新/不得改其他数据/不得做订阅）。
/// 归属：一个实例同一时刻只属于一张卡（跨卡同时挂载＝明确错误）；注销后＝未挂载态、可重挂（不限卡）。
/// 来源（<see cref="Source"/>）＝施加方标识（任意引用对象；相等性判据＝引用相等）；按来源撤销＝撤销该来源在该卡上的全部条目。
/// </summary>
public abstract class Modifier
{
    private IDisposable? _expirySubscription;

    /// <summary>创建修饰器（目标字段标识＋来源标记＋可选期限声明）。</summary>
    /// <param name="field">目标字段标识（非 null/空白；开放集合——挂载时校验「被更新检测组件覆盖」）。</param>
    /// <param name="source">来源标记（施加方标识；任意引用对象，非 null；相等性判据＝引用相等）。</param>
    /// <param name="expiry">期限声明（可选；声明后挂载即自订阅相位、到期自注销）。</param>
    /// <exception cref="ArgumentException">field 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">source 为 null。</exception>
    protected Modifier(string field, object source, ModifierExpiry? expiry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(source);
        Field = field;
        Source = source;
        Expiry = expiry;
    }

    /// <summary>目标字段标识（开放集合；全机制同一标识体系）。</summary>
    public string Field { get; }

    /// <summary>来源标记（施加方标识；相等性判据＝引用相等——同一实例视为同一来源）。</summary>
    public object Source { get; }

    /// <summary>期限声明（可选；null＝无期限）。</summary>
    public ModifierExpiry? Expiry { get; }

    /// <summary>归属卡（挂载后＝挂载卡引用；未挂载/已注销＝null。机制侧用——跨卡冲突判定与幂等判定）。</summary>
    internal Card? HostCard { get; private set; }

    /// <summary>标记归属（机制挂载流程调用；先于 <see cref="OnMount"/>）。</summary>
    internal void MarkMounted(Card card) => HostCard = card;

    /// <summary>清空归属（机制注销/回滚流程调用；后于 <see cref="OnUnmount"/>）。</summary>
    internal void ClearMounted() => HostCard = null;

    /// <summary>
    /// 挂载抽象方法（子类自写；机制在挂载操作中统一调用）：
    /// 子类在此建立专属挂载逻辑（如期限自订阅＝调用 <see cref="SubscribeExpiry"/>）；
    /// 异常＝fail-fast 传播＋该次挂载回滚为未挂载（机制侧保证）。
    /// </summary>
    protected internal abstract void OnMount(ModifierMountContext context);

    /// <summary>
    /// 注销面（子类自写——自托管）：机制统一注销路径调用本钩子，子类清理专属资源（如期限退订＝调用
    /// <see cref="UnsubscribeExpiry"/>），使「注销后无残留」成立；本钩子须对「未完全挂载的清理」与重复调用防御（幂等）。
    /// </summary>
    protected internal abstract void OnUnmount();

    /// <summary>
    /// 链节（纯变换；机制在全量重跑时按挂载序调用）：以卡引用为参数、对「目标字段当前累积值」施加变换并返回新值。
    /// 约束：不得产生持久副作用（不发射更新、不改其他数据、不做订阅——订阅属挂载钩子职责）。
    /// </summary>
    protected internal abstract int Apply(Card card, int current);

    /// <summary>
    /// 期限自订阅（子类钩子辅助；声明 <see cref="Expiry"/> 时调用）：经挂载环境订阅相位更新；
    /// 相位到达（且过滤器命中）→ 请求自注销（统一注销路径）。无期限声明＝无操作（零订阅）。
    /// 上下文/订阅通道缺失＝fail-fast（不得静默留下「永不触发」的订阅）；重复订阅＝明确错误（防御）。
    /// </summary>
    /// <exception cref="ArgumentNullException">context 为 null。</exception>
    /// <exception cref="InvalidOperationException">期限订阅已建立（重复调用——宿主流程错误，fail-fast）。</exception>
    protected void SubscribeExpiry(ModifierMountContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Expiry is null)
        {
            return;
        }

        if (_expirySubscription is not null)
        {
            throw new InvalidOperationException(
                $"修饰器（字段 '{Field}'）的期限订阅已建立（重复订阅——挂载流程错误，fail-fast）。");
        }

        _expirySubscription = context.Engine.Subscribe(
            (updateType, payload, ct) => HandleExpiryUpdateAsync(context, updateType, payload, ct));
    }

    /// <summary>期限退订（子类钩子辅助；注销时调用）：释放订阅（幂等——未订阅/重复调用＝无操作）。</summary>
    protected void UnsubscribeExpiry()
    {
        var subscription = _expirySubscription;
        _expirySubscription = null;
        subscription?.Dispose();
    }

    /// <summary>期限相位回调：相位匹配（ordinal）＋过滤器命中 → 到期自注销（统一注销路径，含自动衔接）。</summary>
    private Task HandleExpiryUpdateAsync(
        ModifierMountContext context, string updateType, IReadOnlyDictionary<string, object?>? payload, CancellationToken ct)
    {
        var expiry = Expiry;
        if (expiry is null || !string.Equals(updateType, expiry.Phase, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (expiry.Filter is not null && !expiry.Filter(payload))
        {
            return Task.CompletedTask; // 过滤器未命中（如归属不符）：未到期、保持挂载
        }

        if (_expirySubscription is null)
        {
            return Task.CompletedTask; // 防御：已卸载（订阅应已释放；双保险——不产生幽灵自注销）
        }

        return context.RequestSelfUnmountAsync(ct); // 到期：自注销（统一注销路径 → 自动衔接一轮 → 有变更才发）
    }
}

/// <summary>
/// 加法型修饰器（模板子类；使用处直接 new）：有效值 = 当前累积值 + 增量。
/// 使用处注入「具体数值」（<see cref="Delta"/>，如 +2 / -1）；挂载序依次应用（多个加法可叠加）。
/// 可选期限声明（到期自注销）。
/// </summary>
public sealed class AddModifier : Modifier
{
    /// <summary>创建加法型修饰器。</summary>
    /// <param name="field">目标字段标识。</param>
    /// <param name="delta">增量（可负）。</param>
    /// <param name="source">来源标记。</param>
    /// <param name="expiry">期限声明（可选）。</param>
    public AddModifier(string field, int delta, object source, ModifierExpiry? expiry = null)
        : base(field, source, expiry)
        => Delta = delta;

    /// <summary>增量（使用处注入的具体数值）。</summary>
    public int Delta { get; }

    /// <inheritdoc />
    protected internal override void OnMount(ModifierMountContext context) => SubscribeExpiry(context);

    /// <inheritdoc />
    protected internal override void OnUnmount() => UnsubscribeExpiry();

    /// <inheritdoc />
    protected internal override int Apply(Card card, int current) => current + Delta;
}

/// <summary>
/// 常量设值型修饰器（模板子类；使用处直接 new）：有效值 = 将当前累积值置为常量（<see cref="Value"/>），
/// 其后修饰器继续应用（合成规则＝按挂载序依次应用——设值不终止链）。
/// 使用处注入「具体数值」（如「攻击力为 0」＝置 0）；可选期限声明（到期自注销）。
/// </summary>
public sealed class SetModifier : Modifier
{
    /// <summary>创建常量设值型修饰器。</summary>
    /// <param name="field">目标字段标识。</param>
    /// <param name="value">目标常量值（将当前累积值置为它）。</param>
    /// <param name="source">来源标记。</param>
    /// <param name="expiry">期限声明（可选）。</param>
    public SetModifier(string field, int value, object source, ModifierExpiry? expiry = null)
        : base(field, source, expiry)
        => Value = value;

    /// <summary>目标常量值（使用处注入的具体数值）。</summary>
    public int Value { get; }

    /// <inheritdoc />
    protected internal override void OnMount(ModifierMountContext context) => SubscribeExpiry(context);

    /// <inheritdoc />
    protected internal override void OnUnmount() => UnsubscribeExpiry();

    /// <inheritdoc />
    protected internal override int Apply(Card card, int current) => Value;
}

/// <summary>
/// 回调型修饰器（模板子类；使用处直接 new＋注入逻辑）：变换由使用处注入的回调完成
/// （自定义变换回调——「现场 new＋注入逻辑」的注入点；如条件变换、依卡上数据的自定义改写）。
/// 可选期限声明（到期自注销）。
/// </summary>
public sealed class CallbackModifier : Modifier
{
    private readonly Func<Card, int, int> _transform;

    /// <summary>创建回调型修饰器。</summary>
    /// <param name="field">目标字段标识。</param>
    /// <param name="transform">变换回调（参数＝卡引用＋当前累积值；返回新值；须为纯变换——不得产生持久副作用）。</param>
    /// <param name="source">来源标记。</param>
    /// <param name="expiry">期限声明（可选）。</param>
    /// <exception cref="ArgumentNullException">transform 为 null。</exception>
    public CallbackModifier(string field, Func<Card, int, int> transform, object source, ModifierExpiry? expiry = null)
        : base(field, source, expiry)
    {
        ArgumentNullException.ThrowIfNull(transform);
        _transform = transform;
    }

    /// <inheritdoc />
    protected internal override void OnMount(ModifierMountContext context) => SubscribeExpiry(context);

    /// <inheritdoc />
    protected internal override void OnUnmount() => UnsubscribeExpiry();

    /// <inheritdoc />
    protected internal override int Apply(Card card, int current) => _transform(card, current);
}

/// <summary>
/// 引用型设值修饰器：有效值 = 由使用处注入的取值回调（valueReader——按「引用源字段的当前有效值」取值）决定。
/// 引用随动（W2b 起启用）：取值回调经 <see cref="CardModifierComponent.ComputeEffectiveValue"/> 现算源字段的
/// 「当前有效值」——源字段变化（修饰/伤害经门户→跑链）在同一轮内重算本字段输出、并与集中触发衔接（无需显式请求）。
/// 形如「攻击力等同于防御力」＝<c>card =&gt; card.Modifiers.ComputeEffectiveValue(CardStatFields.Defense)</c>。
/// 防循环（引用链依赖收敛）仍为后续扩展点——现算对「同一字段的递归现算」（引用链循环）＝拒绝并明确错误（fail-fast）。
/// 可选期限声明（到期自注销）。
/// </summary>
public sealed class ReferenceSetModifier : Modifier
{
    private readonly Func<Card, int> _valueReader;

    /// <summary>创建引用型设值修饰器（结构位）。</summary>
    /// <param name="field">目标字段标识。</param>
    /// <param name="valueReader">取值回调（参数＝卡引用；返回引用源的当前值——须为纯读取）。</param>
    /// <param name="source">来源标记。</param>
    /// <param name="expiry">期限声明（可选）。</param>
    /// <exception cref="ArgumentNullException">valueReader 为 null。</exception>
    public ReferenceSetModifier(string field, Func<Card, int> valueReader, object source, ModifierExpiry? expiry = null)
        : base(field, source, expiry)
    {
        ArgumentNullException.ThrowIfNull(valueReader);
        _valueReader = valueReader;
    }

    /// <inheritdoc />
    protected internal override void OnMount(ModifierMountContext context) => SubscribeExpiry(context);

    /// <inheritdoc />
    protected internal override void OnUnmount() => UnsubscribeExpiry();

    /// <inheritdoc />
    protected internal override int Apply(Card card, int current) => _valueReader(card);
}

/// <summary>
/// 下限钳制型修饰器（W3-2 G5 加性；模板子类——第 5 个并列成员；使用处直接 new）：
/// 有效值 = 当前累积值低于下限时抬升为下限（≥ 下限不变；等于边界不变——不产生变化）。
/// 使用处注入「边界值」（<see cref="Min"/>，如费用下限 1）；单边独立使用；
/// 区间需求＝下限钳制与上限钳制同组组合（不另设区间型合并子类——组合式类型家族；组合结果按挂载序，序敏感）。
/// 钳制值域＝int（合法域由使用场景约定，如费用下限 1）；下限 &gt; 上限的组合不设机制校验（使用者约定）。
/// 可选期限声明（到期自注销）。
/// </summary>
public sealed class MinClampModifier : Modifier
{
    /// <summary>创建下限钳制型修饰器。</summary>
    /// <param name="field">目标字段标识。</param>
    /// <param name="min">下限（低于本值的累积值被抬升为本值）。</param>
    /// <param name="source">来源标记。</param>
    /// <param name="expiry">期限声明（可选）。</param>
    public MinClampModifier(string field, int min, object source, ModifierExpiry? expiry = null)
        : base(field, source, expiry)
        => Min = min;

    /// <summary>下限（使用处注入的边界值）。</summary>
    public int Min { get; }

    /// <inheritdoc />
    protected internal override void OnMount(ModifierMountContext context) => SubscribeExpiry(context);

    /// <inheritdoc />
    protected internal override void OnUnmount() => UnsubscribeExpiry();

    /// <inheritdoc />
    protected internal override int Apply(Card card, int current) => Math.Max(current, Min);
}

/// <summary>
/// 上限钳制型修饰器（W3-2 G5 加性；模板子类——第 6 个并列成员；使用处直接 new）：
/// 有效值 = 当前累积值高于上限时压低为上限（≤ 上限不变；等于边界不变——不产生变化）。
/// 使用处注入「边界值」（<see cref="Max"/>）；单边独立使用；与下限钳制同组组合＝区间语义（组合结果按挂载序，序敏感）。
/// 钳制值域＝int（合法域由使用场景约定）；下限 &gt; 上限的组合不设机制校验（使用者约定）。
/// 可选期限声明（到期自注销）。
/// </summary>
public sealed class MaxClampModifier : Modifier
{
    /// <summary>创建上限钳制型修饰器。</summary>
    /// <param name="field">目标字段标识。</param>
    /// <param name="max">上限（高于本值的累积值被压低为本值）。</param>
    /// <param name="source">来源标记。</param>
    /// <param name="expiry">期限声明（可选）。</param>
    public MaxClampModifier(string field, int max, object source, ModifierExpiry? expiry = null)
        : base(field, source, expiry)
        => Max = max;

    /// <summary>上限（使用处注入的边界值）。</summary>
    public int Max { get; }

    /// <inheritdoc />
    protected internal override void OnMount(ModifierMountContext context) => SubscribeExpiry(context);

    /// <inheritdoc />
    protected internal override void OnUnmount() => UnsubscribeExpiry();

    /// <inheritdoc />
    protected internal override int Apply(Card card, int current) => Math.Min(current, Max);
}

/// <summary>
/// 条件评估型修饰器（W3-1 G4「贡献节统一形态」之条件评估——第 7 个并列成员；模板子类；使用处直接 new）：
/// 有效值 = 条件成立时经变换回调（<see cref="Transform"/>）施加贡献、不成立时产出原值（以当前累积值为输出——自然结果、无增删成本）。
/// 条件回调（<see cref="Condition"/>）读「本卡（card 参数）＋经环境查询面的环境事实」——如「在前线时」「每有相邻单位」；
/// 判定随环境实时变化，「条件失效产出原值」由链重跑自然导出（环境类事件 → 全域重跑 → 条件重求值）。
/// 生命周期与其余模板子类一致（可挂载/注销、按来源撤销、随效果装载链托管清理、可选期限声明）——
/// 与固定值修饰器唯一差异＝变换前多一道条件求值。变换回调须为纯变换（不得产生持久副作用——订阅属挂载钩子职责）。
/// </summary>
public sealed class ConditionalModifier : Modifier
{
    private readonly Func<Card, bool> _condition;
    private readonly Func<Card, int, int> _transform;

    /// <summary>创建条件评估型修饰器。</summary>
    /// <param name="field">目标字段标识。</param>
    /// <param name="condition">条件回调（参数＝宿主卡引用；true＝施加贡献、false＝产出原值；须为纯判定——只读本卡与环境）。</param>
    /// <param name="transform">变换回调（参数＝卡引用＋当前累积值；返回新值；须为纯变换——不得产生持久副作用）。</param>
    /// <param name="source">来源标记。</param>
    /// <param name="expiry">期限声明（可选）。</param>
    /// <exception cref="ArgumentNullException">condition / transform / source 为 null。</exception>
    /// <exception cref="ArgumentException">field 为 null/空白。</exception>
    public ConditionalModifier(
        string field,
        Func<Card, bool> condition,
        Func<Card, int, int> transform,
        object source,
        ModifierExpiry? expiry = null)
        : base(field, source, expiry)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(transform);
        _condition = condition;
        _transform = transform;
    }

    /// <summary>条件回调（只读判定；true＝施加贡献、false＝产出原值）。</summary>
    public Func<Card, bool> Condition => _condition;

    /// <summary>变换回调（条件成立时施加的纯变换）。</summary>
    public Func<Card, int, int> Transform => _transform;

    /// <inheritdoc />
    protected internal override void OnMount(ModifierMountContext context) => SubscribeExpiry(context);

    /// <inheritdoc />
    protected internal override void OnUnmount() => UnsubscribeExpiry();

    /// <inheritdoc />
    protected internal override int Apply(Card card, int current)
        => _condition(card) ? _transform(card, current) : current; // 条件失效：自然产出原值（无增删成本）
}
