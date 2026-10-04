using Orc.Cards;
using Orc.Output;

namespace Orc.Core;

/// <summary>
/// 逻辑引擎（S5 形态）。公开面按用途组织：
/// ①事件流：<see cref="RootStream"/>（总流；树根，引擎域一切因果记录最终可达）与 <see cref="EventStreamJson"/>（树形 JSON 导出）；
/// ②总线：<see cref="Bus"/>（S3；更新广播；一对一双向绑定）与 <see cref="Emit"/>（便捷转发）；
/// ③流程：<see cref="AttackFlow" /> / <see cref="DamageFlow"/>（S4）与 <see cref="OrderFlow"/>（S5；承载引擎级共享的施放检查位）；
/// ④卡牌：<see cref="Cards"/>（S5 登记读面：创建即注册、销毁清理响应时移除）与 <see cref="DestroyCard"/>（一步式销毁辅助）；
/// ⑤宿主集成（S5 骨架）：<see cref="Bridge"/>（编译期强类型桥）与 <see cref="Subscribe"/>（运行期回调）——两通道均在更新广播（Emit）时通知；
/// ⑥快照：<see cref="SnapshotJson"/>（单实体/全量 JSON）；
/// ⑦卡牌装载处理（S4 懒创建）：<see cref="CardPlacedTrigger"/> / <see cref="CardCleanupTrigger"/>（内置事件注册句柄供给面）。
/// 允许并主张多实例（每实例独立总线、总流与卡牌登记）；无全局单例；链上执行恒随「调用时显式传入的引擎引用」。
/// engine.Emit 为 <see cref="Bus.Emit"/> 的便捷转发（完全一致、纯转发：含入流目标流判定）。
/// </summary>
public sealed class LogicEngine
{
    private readonly List<Card> _cards = new();
    private readonly List<CallbackSubscription> _subscriptions = new();
    private CardLoadoutProcessor? _cardLoadout;

    public LogicEngine()
    {
        RootStream = new EventStream();
        Bus = new Bus(this);
        AttackFlow = new AttackFlow(this);
        DamageFlow = new DamageFlow(this);
        OrderFlow = new OrderFlow(this);
    }

    // ---------- ①事件流 ----------

    /// <summary>总事件流（树根；顶层执行流自动挂载于其下，引擎域一切因果记录最终可达）。</summary>
    public EventStream RootStream { get; }

    // ---------- ②总线 ----------

    /// <summary>总线（S3；与所属引擎一对一绑定；公开只读、不可替换；多实例互不相通）。</summary>
    public Bus Bus { get; }

    // ---------- ③流程 ----------

    /// <summary>攻击流程（S4；发起方：反制检查 → 伤害结算调用 → 收尾；band 扩展位与注入载体）。</summary>
    public AttackFlow AttackFlow { get; }

    /// <summary>伤害结算流程（S4；承受方：结算前 → 伤害生效；可独立触发）。</summary>
    public DamageFlow DamageFlow { get; }

    /// <summary>指令流程（S5；引擎级共享：反制检查 → 施放结算（执行指令施放链）→ 收尾；承载反制类效果接入的具名检查位）。</summary>
    public OrderFlow OrderFlow { get; }

    /// <summary>更新广播便捷转发（与 <see cref="Bus.Emit"/> 行为完全一致）。</summary>
    public Task Emit(
        string updateType, IReadOnlyDictionary<string, object?>? payload = null, CancellationToken ct = default)
        => Bus.Emit(updateType, payload, ct);

    // ---------- ④卡牌 ----------

    /// <summary>
    /// 卡牌登记读面（S5 加性）：全部已创建（注册）卡牌的只读列表，顺序＝创建序（登记序）。
    /// 登记时机＝卡牌创建（<see cref="Card"/> 构造）即注册；范围＝全部已创建卡牌（含未放置）；
    /// 移除时机＝销毁清理响应（card.destroyed 更新处理；一步式与两步式（杀＋发更新）路径收敛一致）。
    /// 多引擎实例各自独立；快照导出经 <see cref="SnapshotJson.SerializeAll"/>。
    /// </summary>
    public IReadOnlyList<Card> Cards => _cards;

    /// <summary>登记卡牌（S5；卡牌构造时调用；内部使用）。</summary>
    internal void RegisterCard(Card card) => _cards.Add(card);

    /// <summary>移除卡牌登记（S5；销毁清理响应调用；幂等——重复移除＝无操作；内部使用）。</summary>
    internal void UnregisterCard(Card card) => _cards.Remove(card);

    /// <summary>
    /// 「卡牌放置处理器」触发器（S4 装载链内置件；懒创建——首次创建卡牌时装配，null＝尚未创建）。
    /// 用途：内置事件（「放置处理」）的寻址面——注册项句柄经 <see cref="Trigger{TView}.InitialRegistrations"/> 供给（moding（逻辑替换）/撤销等场景）。
    /// </summary>
    public Trigger<CardEventView>? CardPlacedTrigger => _cardLoadout?.PlacedTrigger;

    /// <summary>
    /// 「卡牌清理处理器」触发器（S4 装载链内置件；懒创建——首次创建卡牌时装配，null＝尚未创建）。
    /// 用途：内置事件（「清理处理」）的寻址面——注册项句柄经 <see cref="Trigger{TView}.InitialRegistrations"/> 供给（moding（逻辑替换）/撤销等场景）。
    /// </summary>
    public Trigger<CardEventView>? CardCleanupTrigger => _cardLoadout?.CleanupTrigger;

    // ---------- ⑤宿主集成（S5 骨架） ----------

    /// <summary>
    /// 引擎桥（S5 骨架；可空）：宿主实现 <see cref="IEngineBridge"/>（编译期强类型契约）并装配于此；
    /// 引擎在更新广播（Emit）时点调用其更新通知；未装配（null）＝跳过、引擎照常运转；桥异常被隔离（记录、不破坏广播）。
    /// </summary>
    public IEngineBridge? Bridge { get; set; }

    /// <summary>
    /// 注册更新回调（S5 骨架）：订阅单位＝更新（Emit 时回调）；多订阅者＝注册序回调；回调异常被隔离（记录、不破坏更新广播主流程）。
    /// 返回取消句柄：Dispose 后该订阅不再触发（幂等）；句柄释放后订阅引用被移除（不残留）。
    /// </summary>
    /// <exception cref="ArgumentNullException">callback 为 null。</exception>
    public IDisposable Subscribe(EngineUpdateCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var subscription = new CallbackSubscription(this, callback);
        _subscriptions.Add(subscription);
        return subscription;
    }

    private void RemoveSubscription(CallbackSubscription subscription) => _subscriptions.Remove(subscription);

    /// <summary>
    /// 更新时点的外部通知分发（S5 内部；由总线 Emit 调用）：更新条目写入后、订阅者广播前——先桥（单点，可空）、后回调（注册序快照）。
    /// 各通道异常彼此隔离并记入发射者流（Error 级；source＝通道标识）；取消类异常穿透上抛（与订阅者广播口径一致）。
    /// </summary>
    internal async Task NotifyExternalObservers(
        string updateType, IReadOnlyDictionary<string, object?>? payload, EventStream targetStream, CancellationToken ct)
    {
        var bridge = Bridge;
        if (bridge is not null)
        {
            try
            {
                await bridge.OnUpdate(updateType, payload, ct);
            }
            catch (OperationCanceledException)
            {
                throw; // 取消类异常：与订阅者广播口径一致，穿透上抛
            }
            catch (Exception ex)
            {
                WriteObserverError(targetStream, bridge.GetType().Name, ex);
            }
        }

        foreach (var subscription in _subscriptions.ToArray())
        {
            try
            {
                await subscription.Callback(updateType, payload, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteObserverError(targetStream, "update-callback", ex);
            }
        }
    }

    /// <summary>写外部通道（桥/回调）的隔离错误记录（形态与订阅者传播错误记录同构）。</summary>
    private static void WriteObserverError(EventStream stream, string source, Exception ex)
    {
        stream.WriteLog(
            source,
            ex.Message,
            LogLevel.Error,
            new[] { $"exception:{ex.GetType().Name}" },
            new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message,
            });
    }

    // ---------- 内部机制 ----------

    /// <summary>
    /// 确保卡牌装载链处理器已挂载（S4；首次创建卡牌时登记；幂等——每引擎至多两个处理器触发器：
    /// 「卡牌放置处理器」「卡牌清理处理器」）。
    /// S3 域行为不受影响：不使用卡牌的引擎不会挂载任何处理器。
    /// </summary>
    internal void EnsureCardLoadout() => _cardLoadout ??= new CardLoadoutProcessor(this);

    /// <summary>
    /// 一步式销毁辅助（S4）：「杀（<see cref="Entity.Destroy"/> 语义）＋发 card.destroyed 更新」同一步完成，驱动装载链清理该卡全部效果。
    /// <see cref="Entity.Destroy"/> 本身仍「只杀不管卸载」（S3 约束不变）；清理响应归装载链处理器/效果主触发器。
    /// 幂等：重复销毁＝卡牌已失效（Lifetime 幂等）＋清理模板幂等（无操作）。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">card 不属于本引擎。</exception>
    public async Task DestroyCard(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (!ReferenceEquals(card.Engine, this))
        {
            throw new InvalidOperationException($"卡牌 '{card.Name}' 不属于本引擎，不能经本引擎销毁。");
        }

        card.Destroy();
        await Emit(Updates.CardDestroyed, new Dictionary<string, object?> { [PayloadKeys.Card] = card });
    }

    /// <summary>回调订阅记录（S5；Dispose＝取消注册（幂等）；取消后从引擎订阅列表移除、引用释放）。</summary>
    private sealed class CallbackSubscription : IDisposable
    {
        private readonly LogicEngine _engine;
        private bool _disposed;

        internal CallbackSubscription(LogicEngine engine, EngineUpdateCallback callback)
        {
            _engine = engine;
            Callback = callback;
        }

        /// <summary>回调（注册序调用；异常隔离由引擎分发方处理）。</summary>
        internal EngineUpdateCallback Callback { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return; // 幂等：重复取消＝无操作
            }

            _disposed = true;
            _engine.RemoveSubscription(this);
        }
    }
}
