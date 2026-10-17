using Orc.UI.Contracts;

namespace Orc.Ai;

/// <summary>一条 AI 意图（决策产出；执行由 <see cref="AiPilot"/> 负责）。</summary>
/// <param name="Kind">动作类别（见 <see cref="HeuristicPolicy"/> 常量）。</param>
/// <param name="Ref">目标卡/单位的引用键（<c>UiCardView.Uid</c>）。</param>
/// <param name="Slot">槽位序号（仅出牌/移动意图相关）。</param>
/// <param name="AttackPower">攻击者攻击力（仅攻击意图：用于待决应答判定"可击杀"）。</param>
public sealed record AiIntent(string Kind, string? Ref = null, int Slot = -1, int? AttackPower = null);

/// <summary>
/// 启发式决策（纯函数：座位过滤投影 → 一条意图；不做搜索、不做前瞻）。
/// <para>规则表见 <c>.ams-docs/启发式AI玩家/requirements.md</c> R3：单位 → 指令 → 反制 → 攻击 → 移动 → 结束回合。</para>
/// </summary>
public static class HeuristicPolicy
{
    public const string PlayUnit = "play.unit";
    public const string PlayCommand = "play.command";
    public const string PlayCounter = "play.counter";
    public const string Attack = "command.attack";
    public const string Move = "command.move";
    public const string EndTurn = "end-turn";

    private const string UnitType = "unit";
    private const string OrderType = "order";
    private const string CounterType = "counter";

    /// <summary>决定下一步动作（无可行动作＝null）。</summary>
    /// <param name="view">AI 视角投影。</param>
    /// <param name="excluded">本回合已失败的动作键（不再重试）。</param>
    /// <param name="attackable">可用攻击的单位引用键（null＝退回投影的 <c>CanAttack</c> 标志）。</param>
    /// <param name="movable">可移动的单位引用键（null＝退回投影的 <c>CanMoveAndAttack</c> 标志）。</param>
    public static AiIntent? Decide(
        UiMatchView view,
        IReadOnlySet<string>? excluded = null,
        IReadOnlySet<string>? attackable = null,
        IReadOnlySet<string>? movable = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        excluded ??= new HashSet<string>(StringComparer.Ordinal);

        if (DecidePlay(view, excluded) is { } play)
        {
            return play;
        }

        if (TryAttack(view, excluded, attackable) is { } attack)
        {
            return attack;
        }

        if (TryMove(view, excluded, movable) is { } move)
        {
            return move;
        }

        return null;
    }

    /// <summary>只决定「手中牌」的动作（单位 → 指令 → 反制）；无可出牌＝null。</summary>
    public static AiIntent? DecidePlay(UiMatchView view, IReadOnlySet<string>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        excluded ??= new HashSet<string>(StringComparer.Ordinal);

        if (TryPlayUnit(view, excluded) is { } unit)
        {
            return unit;
        }

        if (TryPlayFromHand(view, excluded, OrderType, PlayCommand) is { } command)
        {
            return command;
        }

        // 反制是「激活/取消」翻转入口：已有激活的反制时不再点（否则会反复激活/取消同一张）。
        return view.SelfCounterCount > 0
            ? null
            : TryPlayFromHand(view, excluded, CounterType, PlayCounter);
    }

    /// <summary>本回合已失败动作的键（与 <see cref="Decide"/> 的排除集合同构）。</summary>
    public static string KeyOf(AiIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return intent.Ref is null ? intent.Kind : intent.Kind + "|" + intent.Ref;
    }

    private static AiIntent? TryPlayUnit(UiMatchView view, IReadOnlySet<string> excluded)
    {
        var slot = FreeSupportSlot(view);
        if (slot < 0)
        {
            return null;
        }

        var card = view.SelfHand
            .Where(c => string.Equals(c.Definition.CardType, UnitType, StringComparison.Ordinal))
            .Where(c => CanPay(c, view.SelfKredits))
            .OrderByDescending(Cost)
            .ThenByDescending(Stats)
            .FirstOrDefault(c => !excluded.Contains(PlayUnit + "|" + c.Uid));

        return card is null ? null : new AiIntent(PlayUnit, card.Uid, slot);
    }

    private static AiIntent? TryPlayFromHand(UiMatchView view, IReadOnlySet<string> excluded, string cardType, string kind)
    {
        var card = view.SelfHand
            .Where(c => string.Equals(c.Definition.CardType, cardType, StringComparison.Ordinal))
            .Where(c => CanPay(c, view.SelfKredits))
            .OrderByDescending(Cost)
            .FirstOrDefault(c => !excluded.Contains(kind + "|" + c.Uid));

        return card is null ? null : new AiIntent(kind, card.Uid);
    }

    private static AiIntent? TryAttack(
        UiMatchView view, IReadOnlySet<string> excluded, IReadOnlySet<string>? attackable)
    {
        var attacker = view.SelfLine
            .Where(c => attackable is null ? c.CanAttack : attackable.Contains(c.Uid))
            .OrderByDescending(c => c.EffectiveAttack ?? 0)
            .FirstOrDefault(c => !excluded.Contains(Attack + "|" + c.Uid));

        return attacker is null
            ? null
            : new AiIntent(Attack, attacker.Uid, AttackPower: attacker.EffectiveAttack ?? 0);
    }

    private static AiIntent? TryMove(
        UiMatchView view, IReadOnlySet<string> excluded, IReadOnlySet<string>? movable)
    {
        if (FrontLineHasFreeSlot(view) is false)
        {
            return null;
        }

        var mover = view.SelfLine
            .Where(c => string.Equals(c.Zone, "support", StringComparison.Ordinal))
            .Where(c => movable is null ? c.CanMoveAndAttack : movable.Contains(c.Uid))
            .OrderByDescending(c => c.EffectiveAttack ?? 0)
            .FirstOrDefault(c => !excluded.Contains(Move + "|" + c.Uid));

        return mover is null ? null : new AiIntent(Move, mover.Uid);
    }

    /// <summary>支援线第一个空槽（排除单位占位与总部占位）；无空槽＝-1。</summary>
    public static int FreeSupportSlot(UiMatchView view)
    {
        var occupied = new HashSet<int>(
            view.SelfLine
                .Where(c => string.Equals(c.Zone, "support", StringComparison.Ordinal))
                .Select(c => c.SlotIndex));

        if (view.SelfHq is { } hq)
        {
            occupied.Add(hq.SlotIndex);
        }

        for (var i = 0; i < view.SupportLineSlotCount; i++)
        {
            if (!occupied.Contains(i))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>前线是否有空槽。</summary>
    public static bool FrontLineHasFreeSlot(UiMatchView view)
    {
        var occupied = new HashSet<int>(
            view.SelfLine
                .Where(c => string.Equals(c.Zone, "frontline", StringComparison.Ordinal))
                .Select(c => c.SlotIndex));

        for (var i = 0; i < view.FrontLineSlotCount; i++)
        {
            if (!occupied.Contains(i))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 费用门槛（**不读** <c>UiCardView.CanPlayCard</c>——投影侧该字段恒为 false，合法性由 ui 动作面提供；
    /// 规则合法性交给引擎判定：打不出即失败，按 R7 记为该动作不可用）。
    /// </summary>
    private static bool CanPay(UiCardView card, int kredits)
    {
        var cost = card.EffectiveCost ?? 0;
        return cost >= 0 && cost <= kredits;
    }

    private static int Cost(UiCardView card) => card.EffectiveCost ?? 0;

    private static int Stats(UiCardView card) => (card.EffectiveAttack ?? 0) + (card.EffectiveDefense ?? 0);
}
