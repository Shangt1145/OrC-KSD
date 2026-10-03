#pragma warning disable CS8618 // 视图属性值由框架在绑定时提供；声明期不做初始化（视图类不应写属性初始化器）。

using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 施放流程 band 方案（S5 具名 band 扩展位；与 <see cref="AttackFlowBands"/> 全序同构）：
/// Counter＝反制检查（扩展位，无默认处理器；反制类效果经既有装载/注入机制接入此处）／
/// Resolve＝施放结算（默认处理器「施放结算」：调用当前法术的施放链；链被中断时本事件不再执行——施放效果不发生）／
/// Finalize＝收尾（扩展位，无默认处理器）。
/// </summary>
public enum SpellFlowBands
{
    /// <summary>反制检查（扩展位；默认无处理器，供反制类效果注入）。</summary>
    Counter = 1,

    /// <summary>施放结算（默认处理器在此调用法术的施放链；被中断（Interrupt）时跳过——施放不发生）。</summary>
    Resolve = 2,

    /// <summary>收尾（扩展位；默认无处理器，供效果/调用方注入）。</summary>
    Finalize = 3,
}

/// <summary>
/// 施放流程视图（S5）：一次施放的执行时信息通道——施法者 / 目标 / 数值 / 当前法术（非泛型接线面）。
/// 反制等检查位 handler 经本视图获得「当前施放的法术 / 施法者 / 目标」（执行时数据），而非装配时预绑具体法术引用。
/// </summary>
[ContextView]
public class SpellFlowView
{
    /// <summary>施法者（主动效果的持有卡）。</summary>
    [Read]
    public virtual Card Source { get; set; }

    /// <summary>施放目标（承受方）。</summary>
    [Read]
    public virtual Card Target { get; set; }

    /// <summary>数值（如伤害量；流程内可改写）。</summary>
    [Mutate]
    public virtual int Amount { get; set; }

    /// <summary>当前施放的法术（执行时数据；非泛型接线面，反制等检查位不依赖具体法术类型）。</summary>
    [Read]
    public virtual ICastAction Spell { get; set; }
}

/// <summary>
/// 施放流程（S5；引擎级共享，与 <see cref="AttackFlow"/> / <see cref="DamageFlow"/> 同构）：
/// 编排「反制检查（Counter）→ 施放结算（Resolve；默认调用法术施放链）→ 收尾（Finalize）」。
/// 承载引擎级共享的施放检查位：反制类效果经既有装载/注入机制（<see cref="Effect"/> 的 Inject）接入 Counter band；
/// 反制判定命中并经 <see cref="Context.Interrupt"/> 中断后，Resolve 不再执行——施放链（含伤害结算）不发生。
/// 框架统一提供位点：法术（主动效果）经本流程施放，作者无需自定位点；检查位施放信息经视图（执行时数据通道）传递。
/// </summary>
public sealed class SpellFlow
{
    private readonly LogicEngine _engine;
    private readonly Trigger<SpellFlowView> _trigger;

    internal SpellFlow(LogicEngine engine)
    {
        _engine = engine;
        _trigger = new Trigger<SpellFlowView>(
            name: "施放流程",
            kind: TriggerKind.Active,
            bandType: typeof(SpellFlowBands),
            events: new[]
            {
                // 施放结算＝调用当前法术的施放链（默认事件；被中断时跳过）。
                new TriggerEvent<SpellFlowView>("施放结算", OnResolve, SpellFlowBands.Resolve),
            });
    }

    /// <summary>施放流程触发器（band 扩展位载体；效果经 Register 注入具名 band，经 Unregister 撤销）。</summary>
    public Trigger<SpellFlowView> Trigger => _trigger;

    /// <summary>
    /// 执行施放流程（规范施放入口）：反制检查 → 施放结算（执行法术施放链）→ 收尾。
    /// 顶层调用时施放流挂总流；嵌套调用（如触发器内）时挂当前执行者流。
    /// </summary>
    /// <exception cref="ArgumentNullException">source、spell 或 target 为 null。</exception>
    public Task<EventStream> ExecuteAsync(
        Card source, ICastAction spell, Card target, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(target);

        var data = new Dictionary<string, object?>
        {
            [PayloadKeys.Source] = source,
            [PayloadKeys.Target] = target,
            [PayloadKeys.Amount] = amount,
            [PayloadKeys.Spell] = spell,
        };
        return _trigger.InvokeAsync(_engine, data, ct);
    }

    /// <summary>施放结算（默认）：调用当前法术的施放链（施放信息经执行时数据传递；不预绑具体法术引用）。</summary>
    private Task OnResolve(SpellFlowView view, Context ctx, CancellationToken ct)
    {
        var castData = new Dictionary<string, object?>
        {
            [PayloadKeys.Source] = view.Source,
            [PayloadKeys.Target] = view.Target,
            [PayloadKeys.Amount] = view.Amount,
        };
        return view.Spell.CastAsync(_engine, castData, ct);
    }
}
