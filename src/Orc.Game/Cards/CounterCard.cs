using Orc.Core;
using Orc.Game.Players;
using Orc.Game.Triggers;

namespace Orc.Game.Cards;

/// <summary>反制使用评估的拒绝类别（internal；验证与入口映射同源的判定结果，见 <see cref="CounterCard.EvaluateUse"/>）。</summary>
internal enum CounterUseRejection
{
    /// <summary>非己方回合（该卡所属玩家不是当前行动方）。</summary>
    NotOwnerTurn,

    /// <summary>指挥点不足（未激活时检查）。</summary>
    NotEnoughPoints,
}

/// <summary>
/// 反制卡（三大类之一）：激活/取消两态（数据组件 <see cref="CounterActivationData"/>；2B 起实例化路径装配——挂载落实）。
/// 数据组件装配（E 区差异化）：＝指挥点花费（基类）＋激活状态组件。
/// 触发器（2B 起链内容填入；唯一＝使用反制的触发器——<see cref="CounterUseTrigger"/>）：
/// 单入口状态翻转（按当前激活状态分派）：未激活＝激活流程（验证〔指挥点〕→ 扣点 → 置激活 ＋ 注册效果 handler）；
/// 已激活＝取消流程（退点〔无条件、同额〕→ 取消激活 ＋ 取消注册）；仅己方回合（该卡所属玩家是当前行动方）。
/// 效果 handler 集经 <see cref="AddEffectHandler"/> 装配注册；激活时注册进「使用反制的触发器」、取消时注销
/// （在册可经 <see cref="RegisteredEffectHandlerNames"/> 查询断言）。
/// 使用反制不执行 targeter 交互、不发任何游戏更新；位置语义（手牌要求/激活后去向）本批后置。
/// 加载模板与其余装配沿用基类（<see cref="CardBase"/>）。
/// </summary>
public class CounterCard : CardBase
{
    private readonly List<CounterEffectHandler> _effectHandlers = new();
    private readonly List<TriggerRegistration> _activeRegistrations = new();

    /// <summary>创建反制卡（激活状态组件＋触发器与默认链事件在构造期装配）。</summary>
    /// <exception cref="ArgumentNullException">engine 或 definition 为 null。</exception>
    public CounterCard(LogicEngine engine, CardDefinition definition)
        : base(engine, definition)
    {
        // 激活状态组件（E 区：「反制＝激活状态组件」；2B 挂载落实——初始未激活）。
        AddData(new CounterActivationData());

        UseCounterTrigger = new CounterUseTrigger(this);
        UseCounterTrigger.Register("使用反制", HandleUseCounterAsync);
    }

    /// <summary>使用反制的触发器（唯一；单入口状态翻转：未激活⇒激活流程、已激活⇒取消流程）。</summary>
    public Trigger<CardTriggerView> UseCounterTrigger { get; }

    /// <summary>
    /// 已注册的效果 handler 名称（登记序；激活期间＝登记集、未激活＝空）——handler 注册/注销可观测面
    /// （激活后 handler 在册；取消后已注销）。
    /// </summary>
    public IReadOnlyList<string> RegisteredEffectHandlerNames
        => _activeRegistrations.Select(registration => registration.Name).ToArray();

    /// <summary>
    /// 效果 handler 集装配入口（装配/加载阶段注册；登记序保留）：
    /// 激活时按登记序注册进「使用反制的触发器」、取消时注销；触发执行时机属后续批次（本批仅注册/注销可观测）。
    /// </summary>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">handler 为 null。</exception>
    public void AddEffectHandler(string name, Func<CardTriggerView, Context, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _effectHandlers.Add(new CounterEffectHandler(name, handler));
    }

    /// <summary>
    /// 反制使用评估（internal；验证与入口映射的单一判定源）：返回 null＝合法；否则＝拒绝类别。
    /// 规则：① 仅己方回合（owner 非空且＝当前行动方）；② 未激活时检查指挥点（≥ 花费）；已激活＝取消流程、无条件通过。
    /// </summary>
    internal CounterUseRejection? EvaluateUse(Player? currentPlayer)
    {
        var owner = Owner;
        if (owner is null || currentPlayer is null || !ReferenceEquals(owner, currentPlayer))
        {
            return CounterUseRejection.NotOwnerTurn;
        }

        if (!GetData<CounterActivationData>().IsActive && owner.Points < GetData<CommandPointCostData>().DeployCost)
        {
            return CounterUseRejection.NotEnoughPoints;
        }

        return null;
    }

    /// <summary>使用反制默认事件（单入口状态翻转；按当前激活状态分派）。</summary>
    private Task HandleUseCounterAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not CounterCard card || view.Player is not Player player)
        {
            ctx.Interrupt(); // 载荷缺失（结构性错误）：翻转不发生
            return Task.CompletedTask;
        }

        var activation = card.GetData<CounterActivationData>();
        var cost = card.GetData<CommandPointCostData>().DeployCost;

        if (!activation.IsActive)
        {
            // 激活流程：扣点 → 置激活 → 注册（效果）handler（登记序）。
            player.Points -= cost;
            activation.IsActive = true;
            foreach (var effect in card._effectHandlers)
            {
                card._activeRegistrations.Add(card.UseCounterTrigger.Register(effect.Name, effect.Handler));
            }
        }
        else
        {
            // 取消流程：退点（无条件、同额）→ 取消激活 → 取消注册。
            player.Points += cost;
            activation.IsActive = false;
            foreach (var registration in card._activeRegistrations)
            {
                card.UseCounterTrigger.Unregister(registration);
            }

            card._activeRegistrations.Clear();

            // 本次执行快照含激活期间在册的效果 handler（取消前注册）：Stop 防其在本次触发中被执行
            // （「取消后触发不执行」的严谨落实——注销立即生效于后续触发）。
            ctx.Stop();
        }

        return Task.CompletedTask;
    }

    private sealed record CounterEffectHandler(string Name, Func<CardTriggerView, Context, CancellationToken, Task> Handler);
}
