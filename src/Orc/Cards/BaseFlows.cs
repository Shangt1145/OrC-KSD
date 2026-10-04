#pragma warning disable CS8618 // 视图属性值由框架在绑定时提供；声明期不做初始化（视图类不应写属性初始化器）。

// 基础流程基座（BaseFlows）：攻击流程（AttackFlow）与伤害结算流程（DamageFlow）的 band 方案与视图（类名不变）。
// 后续流程可在本基座上拓展（复用具名 band 扩展位机制）。

using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 攻击流程 band 方案（S4 具名 band 扩展位；命名自定）：
/// Counter＝反制检查（扩展位，无默认处理器）／Resolve＝攻击结算（默认：调用伤害结算流程；效果注入点）／Finalize＝收尾（扩展位，无默认处理器）。
/// </summary>
public enum AttackFlowBands
{
    /// <summary>反制检查（扩展位；默认无处理器，供效果/调用方注入）。</summary>
    Counter = 1,

    /// <summary>攻击结算（默认处理器「伤害结算」在此调用伤害结算流程；「攻击结算完成时」类效果注入于此、以 band 内优先级排于调用之后）。</summary>
    Resolve = 2,

    /// <summary>收尾（扩展位；默认无处理器，供效果/调用方注入）。</summary>
    Finalize = 3,
}

/// <summary>
/// 伤害结算流程 band 方案（S4 具名 band 扩展位；命名自定）：
/// PreApply＝结算前（默认处理器：中性记录待结算伤害）／Apply＝伤害生效（默认处理器）。
/// </summary>
public enum DamageFlowBands
{
    /// <summary>结算前（默认：中性记录待结算伤害、不改动数值；效果/调用方可在本扩展位注入）。</summary>
    PreApply = 1,

    /// <summary>伤害生效（默认：按伤害量扣减承受方生命）。</summary>
    Apply = 2,
}

/// <summary>攻击流程视图（S4）：攻击的发起方视角载荷（Source＝发起方、Target＝承受方、Amount＝攻击量）。</summary>
[ContextView]
public class AttackFlowView
{
    [Read]
    public virtual Card Source { get; set; }

    [Read]
    public virtual Card Target { get; set; }

    [Mutate]
    public virtual int Amount { get; set; }
}

/// <summary>伤害结算流程视图（S4）：伤害的承受方视角载荷（Source＝来源、Target＝承受方、Amount＝伤害量；扩展位可改写伤害量）。</summary>
[ContextView]
public class DamageFlowView
{
    [Read]
    public virtual Card Source { get; set; }

    [Read]
    public virtual Card Target { get; set; }

    [Mutate]
    public virtual int Amount { get; set; }
}

/// <summary>
/// 攻击流程（S4；发起方）：编排「反制检查（Counter）→ 调用伤害结算流程（Resolve）→ 收尾（Finalize）」。
/// 引擎级共享实例（经 <see cref="LogicEngine.AttackFlow"/> 获取）；注册触发器为主动触发器（由流程入口调用、不参与总线挂载）。
/// 效果经 <see cref="Trigger{TView}.Register"/> 把 handler 注入具名 band（S2 band 机制复用）。
/// </summary>
public sealed class AttackFlow
{
    private readonly LogicEngine _engine;
    private readonly Trigger<AttackFlowView> _trigger;

    internal AttackFlow(LogicEngine engine)
    {
        _engine = engine;
        _trigger = new Trigger<AttackFlowView>(
            name: "攻击流程",
            kind: TriggerKind.Active,
            bandType: typeof(AttackFlowBands),
            events: new[]
            {
                // 攻击结算＝调用伤害结算流程（默认事件；效果注入以 band 内优先级排于其后，即「攻击结算完成时」）。
                new TriggerEvent<AttackFlowView>("伤害结算", OnResolve, AttackFlowBands.Resolve),
            });
    }

    /// <summary>攻击流程触发器（band 扩展位载体；效果经 Register 注入具名 band，经 Unregister 撤销）。</summary>
    public Trigger<AttackFlowView> Trigger => _trigger;

    /// <summary>
    /// 执行攻击流程（发起方）：反制检查 → 伤害结算调用 → 收尾。
    /// 顶层调用时攻击流挂总流；嵌套调用（如触发器内）时挂当前执行者流。
    /// </summary>
    /// <exception cref="ArgumentNullException">source 或 target 为 null。</exception>
    public Task<EventStream> ExecuteAsync(Card source, Card target, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var data = new Dictionary<string, object?>
        {
            [PayloadKeys.Source] = source,
            [PayloadKeys.Target] = target,
            [PayloadKeys.Amount] = amount,
        };
        return _trigger.InvokeAsync(_engine, data, ct);
    }

    private Task OnResolve(AttackFlowView view, Context ctx, CancellationToken ct)
        => _engine.DamageFlow.ResolveAsync(view.Source, view.Target, view.Amount, ct);
}

/// <summary>
/// 伤害结算流程（S4；承受方）：编排「结算前（PreApply）→ 伤害生效（Apply）」。
/// 引擎级共享实例（经 <see cref="LogicEngine.DamageFlow"/> 获取）；可被独立触发（不经攻击流程亦可执行）。
/// 默认处理器：结算前中性记录待结算伤害（不改动数值）；伤害按量扣减 <see cref="HealthData"/>。
/// </summary>
public sealed class DamageFlow
{
    private readonly LogicEngine _engine;
    private readonly Trigger<DamageFlowView> _trigger;

    internal DamageFlow(LogicEngine engine)
    {
        _engine = engine;
        _trigger = new Trigger<DamageFlowView>(
            name: "伤害结算",
            kind: TriggerKind.Active,
            bandType: typeof(DamageFlowBands),
            events: new[]
            {
                new TriggerEvent<DamageFlowView>("结算前", OnPreApply, DamageFlowBands.PreApply),
                new TriggerEvent<DamageFlowView>("伤害生效", OnApply, DamageFlowBands.Apply),
            });
    }

    /// <summary>伤害结算流程触发器（band 扩展位载体；效果经 Register 注入具名 band，经 Unregister 撤销）。</summary>
    public Trigger<DamageFlowView> Trigger => _trigger;

    /// <summary>
    /// 执行伤害结算（承受方）：结算前 → 伤害生效。
    /// 顶层调用时挂总流；嵌套调用（如攻击流程内）时挂当前执行者流（攻击执行时伤害结算为其子流）。
    /// </summary>
    /// <exception cref="ArgumentNullException">source 或 target 为 null。</exception>
    public Task<EventStream> ResolveAsync(Card source, Card target, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var data = new Dictionary<string, object?>
        {
            [PayloadKeys.Source] = source,
            [PayloadKeys.Target] = target,
            [PayloadKeys.Amount] = amount,
        };
        return _trigger.InvokeAsync(_engine, data, ct);
    }

    /// <summary>结算前（默认）：中性记录待结算伤害，不改动数值（阶段次序留痕；效果可在此扩展位注入）。</summary>
    private Task OnPreApply(DamageFlowView view, Context ctx, CancellationToken ct)
    {
        var amount = Math.Max(0, view.Amount);
        CardsLog.Write(
            _engine,
            "damage-flow",
            $"结算前：待结算伤害 {amount}。",
            LogLevel.Info,
            new[] { "damage", "preapply", view.Target.Name });
        return Task.CompletedTask;
    }

    /// <summary>伤害生效（默认）：按伤害量扣减承受方生命。</summary>
    private Task OnApply(DamageFlowView view, Context ctx, CancellationToken ct)
    {
        var target = view.Target;
        var damage = Math.Max(0, view.Amount);
        var health = target.GetData<HealthData>();
        var before = health.Hp;
        health.Hp -= damage;

        CardsLog.Write(
            _engine,
            "damage-flow",
            $"伤害生效：'{target.Name}' 承受 {damage}（HP {before}→{health.Hp}）。",
            LogLevel.Info,
            new[] { "damage", "apply", target.Name });
        return Task.CompletedTask;
    }
}
