#pragma warning disable CS8618 // 视图属性值由框架在绑定时提供；声明期不做初始化（视图类不应写属性初始化器）。

using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 指令流程 band 方案（S5 具名 band 扩展位；与 <see cref="AttackFlowBands"/> 全序同构）：
/// Counter＝反制检查（扩展位，无默认处理器；反制类效果经既有装载/注入机制接入此处）／
/// Resolve＝施放结算（默认处理器「施放结算」：调用当前指令的施放链；链被中断时本事件不再执行——施放效果不发生）／
/// Finalize＝收尾（扩展位，无默认处理器）。
/// </summary>
public enum OrderFlowBands
{
    /// <summary>反制检查（扩展位；默认无处理器，供反制类效果注入）。</summary>
    Counter = 1,

    /// <summary>施放结算（默认处理器在此调用指令的施放链；被中断（Interrupt）时跳过——施放不发生）。</summary>
    Resolve = 2,

    /// <summary>收尾（扩展位；默认无处理器，供效果/调用方注入）。</summary>
    Finalize = 3,
}

/// <summary>
/// 指令流程视图（S5）：一次施放的执行时信息通道——来源 / 目标 / 数值 / 当前指令（非泛型接线面）。
/// 反制等检查位 handler 经本视图获得「当前施放的指令 / 来源 / 目标」（执行时数据），而非装配时预绑具体指令引用。
/// </summary>
[ContextView]
public class OrderFlowView
{
    /// <summary>来源（主动效果的持有卡）。</summary>
    [Read]
    public virtual Card Source { get; set; }

    /// <summary>施放目标（承受方）。</summary>
    [Read]
    public virtual Card Target { get; set; }

    /// <summary>数值（如伤害量；流程内可改写）。</summary>
    [Mutate]
    public virtual int Amount { get; set; }

    /// <summary>当前施放的指令（执行时数据；非泛型接线面，反制等检查位不依赖具体指令类型）。</summary>
    [Read]
    public virtual ICastAction Order { get; set; }
}

/// <summary>
/// 指令流程（S5；引擎级共享，与 <see cref="AttackFlow"/> / <see cref="DamageFlow"/> 同构）：
/// 编排「反制检查（Counter）→ 施放结算（Resolve；默认调用指令施放链）→ 收尾（Finalize）」。
/// 承载引擎级共享的施放检查位：反制类效果经既有装载/注入机制（<see cref="Effect"/> 的 Inject）接入 Counter band；
/// 反制判定命中并经 <see cref="Context.Interrupt"/> 中断后，Resolve 不再执行——施放链（含伤害结算）不发生。
/// 框架统一提供位点：指令（主动效果）经本流程施放，作者无需自定位点；检查位施放信息经视图（执行时数据通道）传递。
/// </summary>
public sealed class OrderFlow
{
    private readonly LogicEngine _engine;
    private readonly Trigger<OrderFlowView> _trigger;
    private readonly TriggerRegistration _resolveRegistration;

    internal OrderFlow(LogicEngine engine)
    {
        _engine = engine;
        _trigger = new Trigger<OrderFlowView>(
            name: "指令流程",
            kind: TriggerKind.Active,
            bandType: typeof(OrderFlowBands));

        // 施放结算＝调用当前指令的施放链（默认事件；被中断时跳过）。
        // 经注册面装配（等价通道）：内置处理器为普通注册项、注册句柄直接可得（供 moding（逻辑替换）等经句柄寻址）。
        _resolveRegistration = _trigger.Register("施放结算", OnResolve, OrderFlowBands.Resolve);
    }

    /// <summary>指令流程触发器（band 扩展位载体；效果经 Register 注入具名 band，经 Unregister 撤销）。</summary>
    public Trigger<OrderFlowView> Trigger => _trigger;

    /// <summary>「施放结算」处理器（内置默认事件）的注册句柄（供 moding（逻辑替换）等经句柄寻址）。</summary>
    public TriggerRegistration ResolveRegistration => _resolveRegistration;

    /// <summary>
    /// 执行指令流程（规范施放入口）：反制检查 → 施放结算（执行指令施放链）→ 收尾。
    /// 顶层调用时指令流挂总流；嵌套调用（如触发器内）时挂当前执行者流。
    /// </summary>
    /// <exception cref="ArgumentNullException">source、order 或 target 为 null。</exception>
    public Task<EventStream> ExecuteAsync(
        Card source, ICastAction order, Card target, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(target);

        var data = new Dictionary<string, object?>
        {
            [PayloadKeys.Source] = source,
            [PayloadKeys.Target] = target,
            [PayloadKeys.Amount] = amount,
            [PayloadKeys.Order] = order,
        };
        return _trigger.InvokeAsync(_engine, data, ct);
    }

    /// <summary>施放结算（默认）：调用当前指令的施放链（施放信息经执行时数据传递；不预绑具体指令引用）。</summary>
    private Task OnResolve(OrderFlowView view, Context ctx, CancellationToken ct)
    {
        var castData = new Dictionary<string, object?>
        {
            [PayloadKeys.Source] = view.Source,
            [PayloadKeys.Target] = view.Target,
            [PayloadKeys.Amount] = view.Amount,
        };
        return view.Order.CastAsync(_engine, castData, ct);
    }
}
