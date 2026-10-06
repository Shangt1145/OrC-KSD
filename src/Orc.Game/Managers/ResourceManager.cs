using Orc.Cards;
using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Judicators;
using Orc.Game.Players;

namespace Orc.Game.Managers;

/// <summary>点数变更种类（通用入口 <see cref="ResourceManager.ChangePointsAsync"/> 的语义选择）。</summary>
public enum PointChangeKind
{
    /// <summary>设为（直接置为给定值——回合开始的"点数＝槽值"用此）。</summary>
    Set = 0,

    /// <summary>增减（在现值上叠加给定增量；负值＝减——费用扣除/退点用此）。</summary>
    Add = 1,
}

/// <summary>
/// 资源管理器（指挥点领域规则服务；无独立状态——结算直接作用于玩家对象）：
/// 回合开始结算＝槽 += 递增判定器值（默认 1，至上限封顶）→ 点数＝槽值（设为）；
/// 点数在回合结束时刻与敌方回合内保留（X3：回合结束不清零）。
/// <para>
/// E1-25（指挥点槽事件改进）：槽变更受控面＝<see cref="SettleAsync"/>（回合开始递增）／
/// <see cref="GainSlotsAsync"/>（额外获得）／<see cref="LoseSlotsAsync"/>（失去）——三者皆发 <c>slot.changed</c>（唯一「槽值真的变了」信号）；
/// 后两者并各发**语义前置**信号 <c>slot.gained</c> / <c>slot.lost</c>；回合开始递增**不发** gained/lost。
/// 递增经判定器 <see cref="JudicatorNames.PointSlotIncrement"/> 取值；额外获得/失去的请求数字各经
/// <see cref="JudicatorNames.PointSlotGain"/> / <see cref="JudicatorNames.PointSlotLose"/> 包裹（默认恒等）。
/// </para>
/// <para>
/// E1-25 后续（指挥点事件改造）：点数变更受控面分两路——
/// ① **卡效果语义路**：<see cref="GainPointsAsync"/>（「获得 n 个指挥点」）／<see cref="LosePointsAsync"/>（「失去 n 个指挥点」）
///    各发**语义前置**信号 <c>point.gained</c> / <c>point.lost</c>（数字经 <see cref="JudicatorNames.PointGain"/> /
///    <see cref="JudicatorNames.PointLose"/> 包裹、默认恒等），其后汇聚到共用路径；
/// ② **通用无语义路**：<see cref="ChangePointsAsync"/>（设为/增减）＝唯一通用入口，供**游戏内通用来源**
///    （打牌扣费、指挥行动费、反制扣费/退点、回合开始设为）调用——只发 <c>point.changed</c>。
/// 两路最终皆经 <c>point.changed</c>（唯一「点数值真的变了」信号）收敛；<see cref="AddPointsAsync"/> 为受控加值的薄包装。
/// </para>
/// <para>上限＝配置项（默认 12；须为正整数，非法配置抛参数校验异常）。资源初始口径：玩家槽 0 / 点数 0；"第 1 回合＝1 点"由统一递增结算达成（无特例分支）。</para>
/// </summary>
public sealed class ResourceManager
{
    /// <summary>指挥点上限默认值（12）。</summary>
    public const int DefaultMaxPointSlots = 12;

    private readonly LogicEngine _engine;
    private readonly Func<JudicatorRegistry?> _judicators;

    /// <summary>创建资源管理器（载入配置；判定器可达面以延迟读取注入——装配链时序无关）。</summary>
    /// <exception cref="ArgumentNullException">engine 或 judicators 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">maxPointSlots 非正整数（&lt;1）。</exception>
    public ResourceManager(
        LogicEngine engine,
        Func<JudicatorRegistry?> judicators,
        int maxPointSlots = DefaultMaxPointSlots)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(judicators);
        if (maxPointSlots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPointSlots), maxPointSlots, "指挥点上限须为正整数（≥1）。");
        }

        _engine = engine;
        _judicators = judicators;
        MaxPointSlots = maxPointSlots;
    }

    /// <summary>指挥点上限（配置值；默认 12；本批不新增外部写入方法）。</summary>
    public int MaxPointSlots { get; private set; }

    /// <summary>卡 → 资源管理器解析（「卡 → 玩家 → 服务」收敛点；脱局/未注入＝null）。</summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    public static ResourceManager? ResolveFor(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card switch
        {
            Hq hq => hq.Owner.ResourceManager,
            CardBase cardBase => cardBase.Owner?.ResourceManager,
            _ => null,
        };
    }

    // ---------- ① 槽受控面（E1-25） ----------

    /// <summary>
    /// 回合开始结算：槽 += 递增判定器值（默认 1、至上限封顶）→（槽实际变化才发 <c>slot.changed</c>）→ 点数＝槽值（设为）。
    /// **不发** <c>slot.gained</c> / <c>slot.lost</c>（回合开始递增直走"槽改变"）；点数设为经通用入口（值变化才发 <c>point.changed</c>）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public async Task SettleAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        var increment = InvokeIncrement(player);
        var target = Math.Min(player.PointSlots + increment, MaxPointSlots);
        await SetSlotsAsync(player, target, emitChanged: true, ct).ConfigureAwait(false);
        await ChangePointsAsync(player, player.PointSlots, PointChangeKind.Set, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 额外获得指挥点槽（受控面；"额外获得 n 个指挥点槽"类效果）：
    /// 请求数字经 <see cref="JudicatorNames.PointSlotGain"/> 包裹（默认恒等）→ Δ＝min(实际数字, 上限-当前槽)；
    /// Δ＞0 时发 <c>slot.gained</c>（载荷 Amount＝Δ）→ 改值 → 发 <c>slot.changed</c>；Δ≤0（已在上限）＝**零信号**。
    /// 不改动点数（<c>Points</c>）。
    /// </summary>
    /// <returns>槽是否实际发生变化。</returns>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">amount 非正（≤0）。</exception>
    public async Task<bool> GainSlotsAsync(Player player, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "加槽须为严格正数（≤0 被拒绝——加值语义严格为正）。");
        }

        var effective = InvokeGainAmount(player, amount);
        var delta = Math.Min(effective, MaxPointSlots - player.PointSlots);
        if (delta <= 0)
        {
            return false;
        }

        await GameUpdates.EmitSlotGained(_engine, player, delta, ct).ConfigureAwait(false);
        await SetSlotsAsync(player, player.PointSlots + delta, emitChanged: true, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 失去指挥点槽（受控面；"失去 n 个指挥点槽"类效果）：
    /// 请求数字经 <see cref="JudicatorNames.PointSlotLose"/> 包裹（默认恒等）→ Δ＝min(实际数字, 当前槽)；
    /// Δ＞0 时发 <c>slot.lost</c>（载荷 Amount＝Δ）→ 改值 → 发 <c>slot.changed</c>；Δ≤0（已为 0）＝**零信号**。
    /// 不改动点数（<c>Points</c>）。
    /// </summary>
    /// <returns>槽是否实际发生变化。</returns>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">amount 非正（≤0）。</exception>
    public async Task<bool> LoseSlotsAsync(Player player, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "减槽须为严格正数（≤0 被拒绝——减槽语义严格为正）。");
        }

        var effective = InvokeLoseAmount(player, amount);
        var delta = Math.Min(effective, player.PointSlots);
        if (delta <= 0)
        {
            return false;
        }

        await GameUpdates.EmitSlotLost(_engine, player, delta, ct).ConfigureAwait(false);
        await SetSlotsAsync(player, player.PointSlots - delta, emitChanged: true, ct).ConfigureAwait(false);
        return true;
    }

    // ---------- ② 点数受控面（E1-25 后续） ----------

    /// <summary>
    /// 点数通用入口（**唯一**通用落点；供游戏内通用无语义来源——打牌扣费、指挥行动费、反制扣费/退点、回合开始设为）：
    /// <paramref name="kind"/>＝<see cref="PointChangeKind.Set"/>（设为）或 <see cref="PointChangeKind.Add"/>（增减；负值＝减）。
    /// 值实际变化才发 <c>point.changed</c>（载荷＝{ Player, OldPoints, NewPoints }）；未变化＝**零信号**。
    /// 本入口**不发**语义前置信号（gained/lost）——"动作缝"逻辑归属额外获得/失去两路。
    /// </summary>
    /// <returns>点数是否实际发生变化。</returns>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">Add 且叠加后超出 int 上限（防御性拒绝、不产生溢出写）。</exception>
    public async Task<bool> ChangePointsAsync(
        Player player,
        int value,
        PointChangeKind kind = PointChangeKind.Add,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        int target;
        if (kind == PointChangeKind.Set)
        {
            target = value;
        }
        else
        {
            if (value > 0 && player.Points > int.MaxValue - value)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "加点后点数超出 int 上限（防御性拒绝、不产生溢出写）。");
            }

            target = player.Points + value;
        }

        return await SetPointsAsync(player, target, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 额外获得点数（**卡效果**语义路；"获得 n 个指挥点"类效果）：
    /// 请求数字经 <see cref="JudicatorNames.PointGain"/> 包裹（默认恒等）；实际数字 ≤0 ＝**零信号**（本次不生效、不抛错）；
    /// 否则发 <c>point.gained</c>（载荷 Amount＝实际 Δ）→ 汇聚到通用入口 → 发 <c>point.changed</c>。
    /// </summary>
    /// <returns>点数是否实际发生变化。</returns>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">amount 非正（≤0）。</exception>
    public async Task<bool> GainPointsAsync(Player player, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "加点须为严格正数（≤0 被拒绝——加值语义严格为正）。");
        }

        var effective = InvokeGainPointAmount(player, amount);
        if (effective <= 0)
        {
            return false;
        }

        await GameUpdates.EmitPointGained(_engine, player, effective, ct).ConfigureAwait(false);
        return await ChangePointsAsync(player, effective, PointChangeKind.Add, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 失去点数（**卡效果**语义路；"失去 n 个指挥点"类效果）：
    /// 请求数字经 <see cref="JudicatorNames.PointLose"/> 包裹（默认恒等）→ Δ＝min(实际数字, 当前点数)（**下限 0**）；
    /// Δ＞0 时发 <c>point.lost</c>（载荷 Amount＝Δ）→ 汇聚到通用入口 → 发 <c>point.changed</c>；Δ≤0 ＝**零信号**。
    /// </summary>
    /// <returns>点数是否实际发生变化。</returns>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">amount 非正（≤0）。</exception>
    public async Task<bool> LosePointsAsync(Player player, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "减点须为严格正数（≤0 被拒绝——减点语义严格为正）。");
        }

        var effective = InvokeLosePointAmount(player, amount);
        var delta = Math.Min(effective, player.Points);
        if (delta <= 0)
        {
            return false;
        }

        await GameUpdates.EmitPointLost(_engine, player, delta, ct).ConfigureAwait(false);
        return await ChangePointsAsync(player, -delta, PointChangeKind.Add, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 点数加值（受控加值面；＝<see cref="ChangePointsAsync"/>（Add）的薄包装——原校验/数值语义逐条保留：
    /// 玩家 null 拒绝、amount 非正拒绝、溢出防护）。
    /// </summary>
    /// <returns>点数是否实际发生变化。</returns>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">amount 非正（≤0）；或加值后超出 int 上限。</exception>
    public Task<bool> AddPointsAsync(Player player, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "加点须为严格正数（≤0 被拒绝——加值语义严格为正）。");
        }

        return ChangePointsAsync(player, amount, PointChangeKind.Add, ct);
    }

    // ---------- 内部收敛点 ----------

    /// <summary>收敛改值＋（可选）发 <c>slot.changed</c>：槽值未变化＝不发信号。</summary>
    private async Task<bool> SetSlotsAsync(Player player, int newValue, bool emitChanged, CancellationToken ct)
    {
        var oldValue = player.PointSlots;
        if (oldValue == newValue)
        {
            return false;
        }

        player.PointSlots = newValue;
        if (emitChanged)
        {
            await GameUpdates.EmitSlotChanged(_engine, player, oldValue, newValue, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>收敛改值＋发 <c>point.changed</c>：点数值未变化＝不发信号。</summary>
    private async Task<bool> SetPointsAsync(Player player, int newValue, CancellationToken ct)
    {
        var oldValue = player.Points;
        if (oldValue == newValue)
        {
            return false;
        }

        player.Points = newValue;
        await GameUpdates.EmitPointChanged(_engine, player, oldValue, newValue, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>递增取值：经判定器（不可达/结果非法＝默认 1）。</summary>
    private int InvokeIncrement(Player player)
        => Invoke(JudicatorNames.PointSlotIncrement, player, fallback: 1);

    /// <summary>槽额外获得数字包裹取值：经判定器（不可达/结果非法＝原值）。</summary>
    private int InvokeGainAmount(Player player, int requested)
        => Invoke(JudicatorNames.PointSlotGain, player, fallback: requested, extra: requested);

    /// <summary>槽失去数字包裹取值：经判定器（不可达/结果非法＝原值）。</summary>
    private int InvokeLoseAmount(Player player, int requested)
        => Invoke(JudicatorNames.PointSlotLose, player, fallback: requested, extra: requested);

    /// <summary>点数获得数字包裹取值：经判定器（不可达/结果非法＝原值）。</summary>
    private int InvokeGainPointAmount(Player player, int requested)
        => Invoke(JudicatorNames.PointGain, player, fallback: requested, extra: requested);

    /// <summary>点数失去数字包裹取值：经判定器（不可达/结果非法＝原值）。</summary>
    private int InvokeLosePointAmount(Player player, int requested)
        => Invoke(JudicatorNames.PointLose, player, fallback: requested, extra: requested);

    private int Invoke(string name, Player player, int fallback, int? extra = null)
    {
        var args = extra is null ? new object[] { player } : new object[] { player, extra.Value };
        var result = _judicators()?.Invoke(name, args);
        return result is { Length: > 0 } && result[0] is int value ? value : fallback;
    }
}
