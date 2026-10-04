using Orc.Cards;
using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Players;

namespace Orc.Game;

/// <summary>
/// 游戏更新常量集：统一承载回合五连（<c>turn.*</c>）、通用三项（<c>card.played</c> / <c>card.drawn</c> / <c>card.stat.changed</c>）
/// 与第二批 hooks 六项（<c>card.load</c> / <c>card.hand.add</c> / <c>card.died</c> / <c>unit.joined</c> / <c>unit.deployed</c> / <c>unit.position.changed</c>）。
/// 全部为新定义、与 Orc 既有常量集无重叠；字面值为对外订阅契约，一经定稿即冻结（引擎总线同一性＝ordinal 序数、大小写敏感）。
/// 发射统一经引擎总线 <see cref="LogicEngine.Emit"/>（本类提供可选静态发射助手，内部即走该通路）。
/// 实际发射（2B 后）：turn 五连、card.drawn 与 card.drawn→card.hand.add 连发（抽牌链路；粒度不同、并存）、card.load（初始化逐张加载）
/// 与打出链接线点（card.played〔部署/指令打出链〕、unit.joined〔加入路径〕、unit.deployed〔部署链〕）；
/// 2C 后：card.died（战斗死亡）与 unit.position.changed（移动/位置联动）调用点已接线（零调用点遗留＝card.stat.changed）。
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

    /// <summary>打牌（"card.played"；空载荷——2B 打出链接线：部署/指令打出链发射，恰一次、先于后续结算）。</summary>
    public const string CardPlayed = "card.played";

    /// <summary>抽牌（"card.drawn"；载荷＝{ 玩家, 卡牌实例 }）。</summary>
    public const string CardDrawn = "card.drawn";

    /// <summary>数值更新（"card.stat.changed"；无载荷＝硬约定——发射即无载荷）。</summary>
    public const string CardStatChanged = "card.stat.changed";

    // ---------- 第二批 hooks：卡牌加载与手牌 ----------

    /// <summary>卡牌加载（"card.load"；载荷＝{ Card, Player }——Player＝所属卡组玩家）。对局初始化时对双方卡组逐张发射。</summary>
    public const string CardLoad = "card.load";

    /// <summary>
    /// 手牌加入（"card.hand.add"；载荷＝{ Card, Player }）：语义＝对局开始之后任意来源进手牌（含抽牌）；
    /// 与 card.drawn 并存、粒度不同（drawn＝抽牌动作信号、hand.add＝归属结果）；起手装载体不发（静默约定）。
    /// 本批调用点＝抽牌链路（drawn → hand.add 连发）；其他来源调用点随各流程批次接入。
    /// </summary>
    public const string CardHandAdd = "card.hand.add";

    /// <summary>
    /// 游戏层死亡（"card.died"；载荷＝{ Card }）。与引擎内置 <see cref="Updates.CardDestroyed"/>（"card.destroyed"）并存：
    /// 前者＝游戏层死亡（战斗数值链判定）、后者＝引擎销毁（装载链清理驱动）——近似名不同义，勿混用。
    /// 2C 起调用点＝战斗死亡流程（清理就绪后发射、恰一次；同归于尽＝两枚均发射、被攻击者在前）。
    /// </summary>
    public const string CardDied = "card.died";

    // ---------- 第二批 hooks：单位系列 ----------

    /// <summary>单位加入（"unit.joined"；载荷＝{ Unit, Position }——Unit＝单位卡牌实例、Position＝槽位引用）：加入途径（非部署）入场。2B 加入路径调用点已接线（单位化完成后发射、恰一次）。</summary>
    public const string UnitJoined = "unit.joined";

    /// <summary>单位部署（"unit.deployed"；载荷＝{ Unit, Position }）：部署途径（打出部署链）入场。2B 部署链调用点已接线（单位化完成后发射、恰一次）。</summary>
    public const string UnitDeployed = "unit.deployed";

    /// <summary>单位位置变化（"unit.position.changed"；载荷＝{ Unit, OldPosition, NewPosition }——均为槽位引用）。2C 起调用点＝移动执行段（槽位变更后、收尾段之前）；守护维护由本更新驱动。</summary>
    public const string UnitPositionChanged = "unit.position.changed";

    // ---------- 载荷键（对外订阅契约；实现内部引用常量而非裸字符串） ----------

    /// <summary>载荷键：玩家（值＝<see cref="Player"/> 对象引用；turn 五连与 card.drawn 携带）。</summary>
    public const string PayloadPlayer = "Player";

    /// <summary>载荷键：回合数（值＝int；turn 五连携带）。</summary>
    public const string PayloadTurnNumber = "TurnNumber";

    /// <summary>载荷键：卡牌实例（值＝<see cref="Card"/> 对象引用；card.drawn / card.load / card.hand.add / card.died 携带）。</summary>
    public const string PayloadCard = "Card";

    /// <summary>载荷键：单位（值＝单位卡牌实例引用；unit.* 系列携带）。</summary>
    public const string PayloadUnit = "Unit";

    /// <summary>载荷键：位置（值＝<see cref="Slot"/> 引用；unit.joined / unit.deployed 携带）。</summary>
    public const string PayloadPosition = "Position";

    /// <summary>载荷键：原位置（值＝<see cref="Slot"/> 引用；unit.position.changed 携带）。</summary>
    public const string PayloadOldPosition = "OldPosition";

    /// <summary>载荷键：新位置（值＝<see cref="Slot"/> 引用；unit.position.changed 携带）。</summary>
    public const string PayloadNewPosition = "NewPosition";

    // ---------- 发射助手（可选便捷层；统一走总线 Emit 通路） ----------

    /// <summary>发射 card.played（空载荷；2B 打出链接线——部署/指令打出链调用点）。</summary>
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

    // ---------- 发射助手（第二批 hooks；统一走总线 Emit 通路） ----------

    /// <summary>发射 card.load（载荷＝{ 玩家, 卡牌实例 }；对局初始化逐张加载实际使用）。</summary>
    public static Task EmitCardLoad(LogicEngine engine, Player player, Card card, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(card);
        return engine.Emit(
            CardLoad,
            new Dictionary<string, object?>
            {
                [PayloadPlayer] = player,
                [PayloadCard] = card,
            },
            ct);
    }

    /// <summary>发射 card.hand.add（载荷＝{ 玩家, 卡牌实例 }；抽牌链路实际使用——drawn 之后连发；其他来源调用点随各流程批次）。</summary>
    public static Task EmitCardHandAdd(LogicEngine engine, Player player, Card card, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(card);
        return engine.Emit(
            CardHandAdd,
            new Dictionary<string, object?>
            {
                [PayloadPlayer] = player,
                [PayloadCard] = card,
            },
            ct);
    }

    /// <summary>发射 card.died（载荷＝{ 卡牌实例 }；2C 起调用点＝战斗死亡流程——清理就绪后、恰一次）。</summary>
    public static Task EmitCardDied(LogicEngine engine, Card card, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(card);
        return engine.Emit(
            CardDied,
            new Dictionary<string, object?> { [PayloadCard] = card },
            ct);
    }

    /// <summary>发射 unit.joined（载荷＝{ 单位, 位置 }；2B 加入路径调用点——单位化完成后）。</summary>
    public static Task EmitUnitJoined(LogicEngine engine, Card unit, Slot position, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(position);
        return engine.Emit(
            UnitJoined,
            new Dictionary<string, object?>
            {
                [PayloadUnit] = unit,
                [PayloadPosition] = position,
            },
            ct);
    }

    /// <summary>发射 unit.deployed（载荷＝{ 单位, 位置 }；2B 部署链调用点——单位化完成后）。</summary>
    public static Task EmitUnitDeployed(LogicEngine engine, Card unit, Slot position, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(position);
        return engine.Emit(
            UnitDeployed,
            new Dictionary<string, object?>
            {
                [PayloadUnit] = unit,
                [PayloadPosition] = position,
            },
            ct);
    }

    /// <summary>发射 unit.position.changed（载荷＝{ 单位, 原位置, 新位置 }；2C 起调用点＝移动执行段——槽位变更后、恰一次）。</summary>
    public static Task EmitUnitPositionChanged(
        LogicEngine engine, Card unit, Slot oldPosition, Slot newPosition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(oldPosition);
        ArgumentNullException.ThrowIfNull(newPosition);
        return engine.Emit(
            UnitPositionChanged,
            new Dictionary<string, object?>
            {
                [PayloadUnit] = unit,
                [PayloadOldPosition] = oldPosition,
                [PayloadNewPosition] = newPosition,
            },
            ct);
    }
}
