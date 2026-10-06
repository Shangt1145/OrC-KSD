using Orc.Core;
using Orc.Game.Managers;
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
/// 数据组件装配（E 区差异化）：＝阵营〔国籍〕＋部署费合并组件（基类）＋激活状态组件。
/// 触发器（2B 起链内容填入；唯一＝使用反制的触发器——<see cref="CounterUseTrigger"/>）：
/// 单入口状态翻转（按当前激活状态分派）：未激活＝激活流程（验证〔指挥点〕→ 扣点〔读「有效部署费」〕→ 记录实扣额 →
/// 置激活 ＋ 注册效果 handler）；已激活＝取消流程（退点〔无条件、按激活实扣额——「扣点与退点同额」跨调用守恒、
/// 与期间修饰漂移解耦〕→ 取消激活 ＋ 取消注册）；仅己方回合（该卡所属玩家是当前行动方）。
/// 效果 handler 集经 <see cref="AddEffectHandler"/> 装配注册；激活时注册进「使用反制的触发器」、取消时注销
/// （在册可经 <see cref="RegisteredEffectHandlerNames"/> 查询断言）。
/// 使用反制不执行 targeter 交互、不发任何游戏更新；位置语义＝不做激活区——激活/取消＝状态翻转＋费用
/// （卡保持手牌位置不变）；可重复激活（激活↔取消可反复、无次数上限，费用各恰一次；仅限己方回合）。
/// 加载模板与其余装配沿用基类（<see cref="CardBase"/>）。
/// </summary>
public class CounterCard : CardBase
{
    private readonly List<CounterEffectHandler> _effectHandlers = new();
    private readonly List<TriggerRegistration> _activeRegistrations = new();

    /// <summary>激活实扣额（W3-2 G5）：激活时写入、取消时按此退还、取消后清空——「扣点与退点同额」跨调用守恒（与期间修饰漂移解耦）。</summary>
    private int? _chargedCost;

    /// <summary>创建反制卡（激活状态组件＋触发器与默认链事件在构造期装配；
    /// 使用反制触发器按名绑定反制使用检查判定器——解析器缺省＝内置默认〔独立构造即可用〕）。</summary>
    /// <param name="engine">引擎（发射/触发）。</param>
    /// <param name="definition">卡牌定义。</param>
    /// <param name="validationJudicatorResolver">验证判定器解析器（按名解析——对局路径＝注册表解析；
    /// 缺省＝null＝独立构造路径——内置默认解析）。</param>
    /// <exception cref="ArgumentNullException">engine 或 definition 为 null。</exception>
    public CounterCard(
        LogicEngine engine,
        CardDefinition definition,
        Func<string, JudicatorBinding>? validationJudicatorResolver = null)
        : base(engine, definition)
    {
        // 激活状态组件（E 区：「反制＝激活状态组件」；2B 挂载落实——初始未激活）。
        AddData(new CounterActivationData());

        UseCounterTrigger = new CounterUseTrigger(this, validationJudicatorResolver);
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

        // W3-2 G5：未激活时检查指挥点（≥「有效部署费」——修饰贡献叠加后的链输出；评估与翻转同口径）。
        if (!GetData<CounterActivationData>().IsActive
            && owner.Points < Modifiers.GetEffectiveValue(CardStatFields.DeployCost))
        {
            return CounterUseRejection.NotEnoughPoints;
        }

        return null;
    }

    /// <summary>使用反制默认事件（单入口状态翻转；按当前激活状态分派；E1-25 后续：扣点/退点经点数通用入口发 point.changed）。</summary>
    private async Task HandleUseCounterAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not CounterCard card || view.Player is not Player player)
        {
            ctx.Interrupt(); // 载荷缺失（结构性错误）：翻转不发生
            return;
        }

        var activation = card.GetData<CounterActivationData>();

        if (!activation.IsActive)
        {
            // 激活流程：读「有效部署费」（W3-2 G5——修饰贡献叠加后的链输出；与评估同口径）→ 扣点 →
            // 记录实扣额（取消按此退还——「扣点与退点同额」跨调用守恒）→ 置激活 → 注册（效果）handler（登记序）。
            var cost = card.Modifiers.GetEffectiveValue(CardStatFields.DeployCost);
            await ChangePointsAsync(card, player, -cost, ct);
            card._chargedCost = cost;
            activation.IsActive = true;
            foreach (var effect in card._effectHandlers)
            {
                card._activeRegistrations.Add(card.UseCounterTrigger.Register(effect.Name, effect.Handler));
            }
        }
        else
        {
            // 取消流程：退点（无条件、按激活实扣额——与期间修饰漂移解耦、点数守恒）→ 清记录 → 取消激活 → 取消注册。
            var refund = card._chargedCost ?? throw new InvalidOperationException(
                $"反制 '{card.Name}' 处于激活态但无实扣额记录（结构性错误——激活须经使用流程；fail-fast、不静默）。");
            await ChangePointsAsync(card, player, refund, ct);
            card._chargedCost = null;
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
    }

    /// <summary>点数变更归口（E1-25 后续）：经资源管理器通用入口（发 point.changed）；脱局＝直写兜底（保持既有语义）。</summary>
    private static async Task ChangePointsAsync(CounterCard card, Player player, int delta, CancellationToken ct)
    {
        if (ResourceManager.ResolveFor(card) is { } manager)
        {
            await manager.ChangePointsAsync(player, delta, PointChangeKind.Add, ct).ConfigureAwait(false);
            return;
        }

        player.Points += delta;
    }

    private sealed record CounterEffectHandler(string Name, Func<CardTriggerView, Context, CancellationToken, Task> Handler);
}
