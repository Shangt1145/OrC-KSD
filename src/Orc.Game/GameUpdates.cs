using Orc.Cards;
using Orc.Core;
using Orc.Game.Players;

namespace Orc.Game;

/// <summary>
/// 游戏更新常量集（本批）：统一承载回合五连（<c>turn.*</c>）与通用三项（<c>card.played</c> / <c>card.drawn</c> / <c>card.stat.changed</c>）——
/// 8 条均为新定义、与 Orc 既有常量集无重叠；字面值为对外订阅契约，一经定稿即冻结（引擎总线同一性＝ordinal 序数、大小写敏感）。
/// 发射统一经引擎总线 <see cref="LogicEngine.Emit"/>（本类提供可选静态发射助手，内部即走该通路）。
/// 本批实际发射：turn 五连（回合管理器）与 card.drawn（玩家管理器抽牌链路）；
/// card.played / card.stat.changed 零调用点（常量＋发射路径就绪，调用点后续批次）。
/// </summary>
public static class GameUpdates
{
    // ---------- 回合五连 ----------

    /// <summary>回合开始前（"turn.start.before"）：尚未结算、未抽牌。</summary>
    public const string TurnStartBefore = "turn.start.before";

    /// <summary>回合开始（"turn.start"）：回合开始信号；结算与抽牌随后执行（先于 after 通知）。</summary>
    public const string TurnStart = "turn.start";

    /// <summary>回合开始后（"turn.start.after"）：回合开始处理（结算＋抽牌）完成后发出。</summary>
    public const string TurnStartAfter = "turn.start.after";

    /// <summary>回合结束前（"turn.end.before"）。</summary>
    public const string TurnEndBefore = "turn.end.before";

    /// <summary>回合结束（"turn.end"）：回合结束处理（结束方点数清零）随其后执行。</summary>
    public const string TurnEnd = "turn.end";

    // ---------- 通用三项 ----------

    /// <summary>打牌（"card.played"；载荷后置——随调用点批次定型，本批发射以空载荷进行）。</summary>
    public const string CardPlayed = "card.played";

    /// <summary>抽牌（"card.drawn"；载荷＝{ 玩家, 卡牌实例 }）。</summary>
    public const string CardDrawn = "card.drawn";

    /// <summary>数值更新（"card.stat.changed"；无载荷＝硬约定——发射即无载荷）。</summary>
    public const string CardStatChanged = "card.stat.changed";

    // ---------- 载荷键（对外订阅契约；实现内部引用常量而非裸字符串） ----------

    /// <summary>载荷键：玩家（值＝<see cref="Player"/> 对象引用；turn 五连与 card.drawn 携带）。</summary>
    public const string PayloadPlayer = "Player";

    /// <summary>载荷键：回合数（值＝int；turn 五连携带）。</summary>
    public const string PayloadTurnNumber = "TurnNumber";

    /// <summary>载荷键：卡牌实例（值＝<see cref="Card"/> 对象引用；card.drawn 携带）。</summary>
    public const string PayloadCard = "Card";

    // ---------- 发射助手（可选便捷层；统一走总线 Emit 通路） ----------

    /// <summary>发射 card.played（空载荷——载荷随调用点批次定型；调用点后续批次）。</summary>
    public static Task EmitCardPlayed(LogicEngine engine, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Emit(CardPlayed, payload: null, ct);
    }

    /// <summary>发射 card.stat.changed（无载荷＝硬约定；调用点后续批次）。</summary>
    public static Task EmitCardStatChanged(LogicEngine engine, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Emit(CardStatChanged, payload: null, ct);
    }

    /// <summary>发射 card.drawn（载荷＝{ 玩家, 卡牌实例 }；抽牌链路实际使用）。</summary>
    public static Task EmitCardDrawn(LogicEngine engine, Player player, Card card, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(card);
        return engine.Emit(
            CardDrawn,
            new Dictionary<string, object?>
            {
                [PayloadPlayer] = player,
                [PayloadCard] = card,
            },
            ct);
    }
}
