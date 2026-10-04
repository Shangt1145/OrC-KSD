using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 效果基类（S4；逻辑组件）：一个效果 ＝ 一个主触发器（主动/被动）＋作者覆写的装载/卸载钩子。
/// 行为契约（框架装载链保证）：
/// ①单位放置/加载时点装载后效果生效（被动：主触发器挂载至总线 ＋ OnMount 注入完成）；
/// ②效果移除/卡牌销毁后清理完成（OnUnmount → 撤销注入登记 → 总线卸载 → 执行宿主卸载清理动作 → 清宿主引用）；
/// ③顺序：单位初始化逻辑先、效果注入后；④装载＝【挂主触发器 → OnMount】、卸载＝【OnUnmount → 撤销登记 → 总线卸载 → 宿主清理】。
/// 主动效果（指令）**不参与装载/卸载**（不挂总线、不执行钩子；纯列表进出），其生效经 <see cref="ActiveEffect{TView}.CastAsync"/> 调用主触发器（施放）。
/// 作者逻辑统一写在 <see cref="OnMount"/> / <see cref="OnUnmount"/>（框架模板负责「何时触发」）；
/// 注入经 <see cref="Inject{TView}"/> 登记，卸载时框架自动撤销全部登记项（作者不手写撤销）；
/// 宿主机制（如游戏层修饰器托管）可经 <see cref="AddUnmountCleanup"/> 登记卸载清理动作（挂/卸两向均由框架链保证）。
/// 幂等边界（框架保证）：未装载不触 OnUnmount（重复移除幂等）；已装载不重复 OnMount（重复放置不重复装载）；同一效果实例可多次成对装载/卸载（移除后重新 Add 复装）。
/// </summary>
public abstract class Effect
{
    private readonly List<Action> _rollbacks = new();
    private readonly List<Action> _unmountCleanups = new();
    private Card? _host;
    private bool _loading;

    internal Effect(string name, TriggerKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("效果名不能为空或纯空白。", nameof(name));
        }

        Name = name;
        Kind = kind;
    }

    /// <summary>效果名（用于留痕/排查；不承担全局唯一键职责）。</summary>
    public string Name { get; }

    /// <summary>效果种类（触发触发器种类）：被动＝装载语境（挂载/钩子/更新接线）；主动＝施放语境（不装载）。</summary>
    public TriggerKind Kind { get; }

    /// <summary>是否处于已装载状态（被动：主触发器已挂载且 OnMount 已完成；主动恒为 false）。</summary>
    public bool IsMounted { get; private set; }

    /// <summary>宿主卡牌（添加后可用；清理完成后引用被清除、访问抛 <see cref="InvalidOperationException"/>）。</summary>
    /// <exception cref="InvalidOperationException">尚未添加宿主或引用已被清理。</exception>
    public Card Host => _host ?? throw new InvalidOperationException(
        $"效果 '{Name}' 当前无宿主卡牌（尚未添加或引用已被清理）。");

    /// <summary>宿主卡牌引用（可为 null；内部防御用）。</summary>
    internal Card? HostOrNull => _host;

    /// <summary>是否已登记宿主（未被清理）。</summary>
    internal bool HasHost => _host is not null;

    /// <summary>装载钩子（作者覆写；被动效果在「主触发器已挂载」后执行——注入逻辑写在此处，经 <see cref="Inject{TView}"/> 登记）。</summary>
    protected virtual void OnMount()
    {
    }

    /// <summary>卸载钩子（作者覆写；被动效果在「撤销登记/总线卸载」前执行——专属清理写在此处，如清除引用登记）。</summary>
    protected virtual void OnUnmount()
    {
    }

    /// <summary>
    /// 注入辅助（框架装载登记；仅可在 OnMount 装载语境中调用）：
    /// 把 handler 注册进目标触发器（如流程触发器）的具名 band，并把撤销动作登记到本效果；
    /// 卸载时框架自动撤销全部登记项（作者不在 OnUnmount 手写撤销）。
    /// </summary>
    /// <exception cref="ArgumentNullException">target 为 null（其余校验由目标触发器注册面执行）。</exception>
    /// <exception cref="InvalidOperationException">不在装载语境（OnMount）中调用。</exception>
    protected TriggerRegistration Inject<TView>(
        Trigger<TView> target,
        string name,
        Enum band,
        Func<TView, Context, CancellationToken, Task> handler,
        int priority = 0)
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_loading)
        {
            throw new InvalidOperationException(
                $"Inject 仅可在装载语境（OnMount）中调用；效果 '{Name}' 当前不在装载语境。");
        }

        var registration = target.Register(name, handler, band, priority);
        _rollbacks.Add(() => target.Unregister(registration));
        return registration;
    }

    /// <summary>
    /// 登记卸载清理动作（加性公共面；宿主机制扩展面——如游戏层「效果修饰器按来源撤销」托管）：
    /// 效果卸载（<see cref="ExecuteUnmount"/>）时经框架执行（后进先出；各动作恰执行一次；执行后清空——重新装载需重新登记）。
    /// 登记约定＝装载完成后（<see cref="ExecuteMount"/> 成功）进行；故装载回滚（<see cref="RollbackMount"/>）不触发已登记动作
    /// （装载失败的残留清理由登记方在装载失败路径自行兜底——幂等清理语义）。
    /// 动作异常＝隔离记录（不阻断卸载链其余步骤——与框架清理语义一致）；动作不得引入新的跨调用可变状态。
    /// 与 <see cref="Inject{TView}"/> 的「注入登记」区分：本面供宿主机制登记（不面向效果作者手写撤销）。
    /// </summary>
    /// <exception cref="ArgumentNullException">cleanup 为 null。</exception>
    public void AddUnmountCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        _unmountCleanups.Add(cleanup);
    }

    // ---------- 框架装载链原语（由 CardLoadout 调用；对作者不可见） ----------

    /// <summary>挂载主触发器（被动子类重写；主动无操作）。</summary>
    internal virtual void MountMainTrigger(Bus bus)
    {
    }

    /// <summary>卸载主触发器（被动子类重写：经其实际挂载的总线 UnmountOwner 卸载；主动无操作）。</summary>
    internal virtual void UnmountMainTrigger()
    {
    }

    /// <summary>装载链：挂载主触发器 → 执行 OnMount。异常不拦截（由调用方记录并回滚）。</summary>
    internal void ExecuteMount(Bus bus)
    {
        MountMainTrigger(bus);
        IsMounted = true;
        _loading = true;
        try
        {
            OnMount();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>装载失败回滚：撤销已登记注入 → 卸载主触发器 → 复位装载状态（该效果视为未生效）。</summary>
    internal void RollbackMount()
    {
        RollbackInjections();
        UnmountMainTrigger();
        IsMounted = false;
    }

    /// <summary>
    /// 卸载链：执行 OnUnmount（异常捕获并返回，由调用方记录；后续步骤照常完成）→ 撤销全部注入登记 → 卸载主触发器
    /// → 执行宿主卸载清理动作（<see cref="AddUnmountCleanup"/> 登记项；异常隔离记录、不阻断）→ 清宿主引用。
    /// </summary>
    /// <returns>OnUnmount 中抛出的异常（成功时为 null；「清理未完成，以实况计」）。</returns>
    internal Exception? ExecuteUnmount()
    {
        Exception? failure = null;
        try
        {
            OnUnmount();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        RollbackInjections();
        UnmountMainTrigger();
        RunUnmountCleanups();
        IsMounted = false;
        _host = null;
        return failure;
    }

    /// <summary>登记宿主引用（AddEffect 时由容器调用）。</summary>
    internal void SetHost(Card card) => _host = card;

    /// <summary>清除宿主引用（未装载效果的容器移除路径）。</summary>
    internal void ClearHost() => _host = null;

    /// <summary>撤销全部注入登记（后进先出；撤销动作自身幂等、不抛错）。</summary>
    private void RollbackInjections()
    {
        for (var i = _rollbacks.Count - 1; i >= 0; i--)
        {
            _rollbacks[i]();
        }

        _rollbacks.Clear();
    }

    /// <summary>
    /// 执行宿主卸载清理动作（后进先出；异常隔离记录、不阻断卸载链其余步骤；执行后清空——重新装载需重新登记）。
    /// 记录写入宿主卡所属引擎的「当前执行者流（无则总流）」（与装载链留痕同渠道）。
    /// </summary>
    private void RunUnmountCleanups()
    {
        if (_unmountCleanups.Count == 0)
        {
            return;
        }

        var engine = _host?.Engine;
        for (var i = _unmountCleanups.Count - 1; i >= 0; i--)
        {
            try
            {
                _unmountCleanups[i]();
            }
            catch (Exception ex)
            {
                if (engine is not null)
                {
                    CardsLog.Write(
                        engine,
                        $"{Name}/UnmountCleanup",
                        ex.Message,
                        LogLevel.Error,
                        new[] { "loadout", "error", $"exception:{ex.GetType().Name}", Name });
                }
            }
        }

        _unmountCleanups.Clear();
    }
}

/// <summary>
/// 被动效果（S4）：装载语境效果——主触发器由框架装配（hooks 覆盖移除类更新：<see cref="Updates.EffectRemoved"/>、<see cref="Updates.CardDestroyed"/>；
/// 模板事件在移除/销毁更新到来且针对本效果时，经同一清理模板执行「OnUnmount → 撤销登记 → 总线卸载」；与容器入口收敛）。
/// 作者只需覆写 <see cref="Effect.OnMount"/>（注入）/ <see cref="Effect.OnUnmount"/>（专属清理）。
/// 放置驱动装载由装载链完成（放置处理器：初始化 → 逐效果「挂载主触发器 → OnMount」，Effects 列表序、幂等）。
/// </summary>
public abstract class PassiveEffect : Effect
{
    private readonly Trigger<CardEventView> _lifecycleTrigger;

    protected PassiveEffect(string name)
        : base(name, TriggerKind.Passive)
    {
        _lifecycleTrigger = new Trigger<CardEventView>(
            name,
            TriggerKind.Passive,
            events: new[]
            {
                new TriggerEvent<CardEventView>("生命周期清理", OnLifecycleUpdate),
            },
            hooks: new[] { Updates.EffectRemoved, Updates.CardDestroyed },
            owner: this);
    }

    internal override void MountMainTrigger(Bus bus) => bus.Mount(_lifecycleTrigger);

    internal override void UnmountMainTrigger()
    {
        var bus = _lifecycleTrigger.MountedBus;
        if (bus is not null)
        {
            bus.UnmountOwner(this);
        }
    }

    /// <summary>主触发器模板事件：对「针对本效果」的移除类更新（effect.removed 载荷含本效果 / card.destroyed 载荷为宿主卡）收敛到同一清理模板。</summary>
    private Task OnLifecycleUpdate(CardEventView view, Context ctx, CancellationToken ct)
    {
        var payloadEffect = view.Effect as Effect;
        if (payloadEffect is not null)
        {
            if (!ReferenceEquals(payloadEffect, this))
            {
                return Task.CompletedTask; // 非本效果的移除信号（多效果隔离）
            }

            var card = view.Card as Card;
            if (card is not null && !ReferenceEquals(card, HostOrNull))
            {
                return Task.CompletedTask; // 载荷卡牌与宿主不一致（防御）
            }

            card ??= HostOrNull;
            if (card is null)
            {
                return Task.CompletedTask;
            }

            CardLoadout.CleanupEffect(card, this);
            return Task.CompletedTask;
        }

        var destroyed = view.Card as Card;
        if (destroyed is null || !ReferenceEquals(destroyed, HostOrNull))
        {
            return Task.CompletedTask;
        }

        CardLoadout.CleanupEffect(destroyed, this);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 主动效果（S4；指令）：施放语境效果——主触发器为作者声明的主动触发器（不挂总线、无装载/卸载、不执行钩子；S3 约束下主动触发器不可挂载）。
/// 施放＝主触发器被调用（<see cref="CastAsync"/>）→ 施放事件链（作者注册；最小形态：对目标调用伤害结算流程）。
/// 实现 <see cref="ICastAction"/>（S5）：供引擎级共享指令流程（<see cref="OrderFlow"/>）在检查位通过后执行施放链。
/// 放置/加入时无装载动作（仅存在于 Effects 列表）；一次性/消耗语义不在本期范围。
/// </summary>
/// <typeparam name="TView">施放载荷的视图类型（作者视图）。</typeparam>
public abstract class ActiveEffect<TView> : Effect, ICastAction
    where TView : class
{
    private readonly Trigger<TView> _castTrigger;

    protected ActiveEffect(string name, Type? bandType = null, IEnumerable<TriggerEvent<TView>>? castEvents = null)
        : base(name, TriggerKind.Active)
    {
        _castTrigger = new Trigger<TView>(
            name,
            TriggerKind.Active,
            bandType,
            events: castEvents,
            owner: this);
    }

    /// <summary>主触发器（作者视图；可在子类构造体内经 <see cref="Trigger{TView}.Register"/> 注册施放事件）。</summary>
    protected Trigger<TView> CastTrigger => _castTrigger;

    /// <summary>施放入口：调用主触发器（施放 ＝ 主触发器被调用；其施放事件链按注册序执行）。</summary>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    public Task<EventStream> CastAsync(
        LogicEngine engine, IDictionary<string, object?>? data = null, CancellationToken ct = default)
        => _castTrigger.InvokeAsync(engine, data, ct);
}
