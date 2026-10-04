using Orc.Cards;
using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Collections;
using Orc.Game.Players;

namespace Orc.Game;

/// <summary>
/// 游戏更新常量集：统一承载回合五连（<c>turn.*</c>）、通用三项（<c>card.played</c> / <c>card.drawn</c> / <c>card.stat.changed</c>）、
/// 第二批 hooks 六项（<c>card.load</c> / <c>card.hand.add</c> / <c>card.died</c> / <c>unit.joined</c> / <c>unit.deployed</c> / <c>unit.position.changed</c>）
/// 与 W4-1（G14 收尾）洗切一项（<c>deck.shuffled</c>）——合计 15 条。
/// 全部为新定义、与 Orc 既有常量集无重叠；字面值为对外订阅契约，一经定稿即冻结（引擎总线同一性＝ordinal 序数、大小写敏感）。
/// 发射统一经引擎总线 <see cref="LogicEngine.Emit"/>（本类提供可选静态发射助手，内部即走该通路）。
/// 实际发射（2B 后）：turn 五连、card.drawn 与 card.drawn→card.hand.add 连发（抽牌链路；粒度不同、并存）、card.load（初始化逐张加载）
/// 与打出链接线点（card.played〔部署/指令打出链〕、unit.joined〔加入路径〕、unit.deployed〔部署链〕）；
/// 2C 后：card.died（战斗死亡）与 unit.position.changed（移动/位置联动）调用点已接线；
/// W2a（G3 修饰机制）起：card.stat.changed 升级为载荷化契约（{ Card, ChangedFields }——目标卡＋本轮变化字段集合），
/// 调用点＝修饰机制集中触发（有变更轮恰一次、无变更零发射）；既有流程调用点接入属 W2b；
/// W4-1（G14 收尾）：card.played 升级为载荷化契约（{ Card, Player }——被使用卡实例＋使用方），调用点＝部署/指令打出链「打出宣告」；
/// deck.shuffled 新增（初始化双方卡组洗切各一条——「统一经洗切动作面〔Match.ShuffleDeckAsync〕、恰一次」；「是否首次」不入信号）。
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

    /// <summary>回合结束（"turn.end"）：其后执行切换当前方与回合数 +1（X3：点数保留——不清零）。</summary>
    public const string TurnEnd = "turn.end";

    // ---------- 通用三项 ----------

    /// <summary>
    /// 打牌（"card.played"；W4-1 升级版载荷＝{ Card, Player }——被使用卡实例＋使用方〔玩家对象引用〕）。
    /// 2B 打出链接线：部署/指令打出链发射，恰一次、先于后续结算；旧「空载荷＝硬约定」随升级废止
    /// （无载荷发射在升级语义下无定义）。监听方按「卡的定义」识别卡种（同名卡＝同一定义——G14 使用计数口径）。
    /// </summary>
    public const string CardPlayed = "card.played";

    /// <summary>抽牌（"card.drawn"；载荷＝{ 玩家, 卡牌实例 }）。</summary>
    public const string CardDrawn = "card.drawn";

    /// <summary>
    /// 数值更新（"card.stat.changed"；W2a 升级版载荷＝{ Card, ChangedFields }——目标卡＋本轮变化字段集合
    /// 〔字段标识、开放集合、稳定序〕）。语义＝「有变更才发、单条携带本轮全部变化字段、无变更零发射」；
    /// 旧「无载荷＝硬约定」随升级废止（无载荷发射在升级语义下无定义）。
    /// </summary>
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

    // ---------- W4-1（G14 收尾）洗切一项：卡组信号 ----------

    /// <summary>
    /// 卡组洗切（"deck.shuffled"；载荷＝{ Player, Deck }——被洗切卡组归属玩家〔Player 对象引用〕＋被洗切卡组〔<see cref="CardList"/> 对象引用〕）。
    /// 语义＝「某玩家的卡组发生了一次洗切」——单副卡组粒度、恰一次；初始化双方卡组洗切各一条（玩家索引升序）。
    /// 调用点＝洗切动作面（<see cref="Match.ShuffleDeckAsync"/>——初始化路径与后续「洗切卡组」类效果路径统一经此）。
    /// 「是否首次」不入信号——由监听者自持标志判断（私有闭环）；监听窗口自订阅/装载时点起、不回溯。
    /// </summary>
    public const string DeckShuffled = "deck.shuffled";

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

    /// <summary>载荷键：变化字段集合（值＝字段标识集合〔有序只读列表、稳定序〕；card.stat.changed 携带——W2a 升级版）。</summary>
    public const string PayloadChangedFields = "ChangedFields";

    /// <summary>载荷键：卡组（值＝<see cref="CardList"/> 对象引用；deck.shuffled 携带——被洗切卡组；与 <see cref="PayloadPlayer"/> 归属并列）。</summary>
    public const string PayloadDeck = "Deck";

    // ---------- 发射助手（可选便捷层；统一走总线 Emit 通路） ----------

    /// <summary>发射 card.played（W4-1 升级版；载荷＝{ Card, Player }——被使用卡实例＋使用方；部署/指令打出链调用点）。</summary>
    /// <exception cref="ArgumentNullException">engine / card / player 为 null。</exception>
    public static Task EmitCardPlayed(
        LogicEngine engine, Card card, Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(player);
        return engine.Emit(
            CardPlayed,
            new Dictionary<string, object?>
            {
                [PayloadCard] = card,
                [PayloadPlayer] = player,
            },
            ct);
    }

    /// <summary>发射 deck.shuffled（载荷＝{ Player, Deck }；洗切动作面统一使用——初始化双方卡组洗切各一条、恰一次）。</summary>
    /// <exception cref="ArgumentNullException">engine / player / deck 为 null。</exception>
    public static Task EmitDeckShuffled(
        LogicEngine engine, Player player, CardList deck, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(deck);
        return engine.Emit(
            DeckShuffled,
            new Dictionary<string, object?>
            {
                [PayloadPlayer] = player,
                [PayloadDeck] = deck,
            },
            ct);
    }

    /// <summary>
    /// 发射 card.stat.changed（W2a 升级版；载荷＝{ Card, ChangedFields }——目标卡＋本轮变化字段集合）。
    /// 语义＝有变更才发（无变更零发射——调用方不得以空集合调用）；单条携带本轮全部变化字段。
    /// </summary>
    /// <exception cref="ArgumentNullException">engine / card / changedFields 为 null。</exception>
    /// <exception cref="ArgumentException">changedFields 为空集合（无变更不发——空集合调用属逻辑错误）。</exception>
    public static Task EmitCardStatChanged(
        LogicEngine engine, Card card, IReadOnlyList<string> changedFields, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(changedFields);
        if (changedFields.Count == 0)
        {
            throw new ArgumentException(
                "变化字段集合为空（card.stat.changed 为「有变更才发」——空集合调用属逻辑错误）。", nameof(changedFields));
        }

        return engine.Emit(
            CardStatChanged,
            new Dictionary<string, object?>
            {
                [PayloadCard] = card,
                [PayloadChangedFields] = changedFields,
            },
            ct);
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
