using Orc.Cards;
using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Orc.Game.Players;

namespace Orc.Game.Managers;

/// <summary>
/// 玩家管理器（玩家领域真源）：创建双玩家、提供玩家访问、执行卡组加载与抽牌操作。
/// 卡组加载（对局开始；2A 新增）：逐玩家（索引升序）逐张——卡组取件（洗牌后顺序）→ 经卡牌库实例化（三类卡之一）
/// → 装配到卡组条目（同一性：即后续起手 / 抽牌所得实例）→ 执行加载模板（其内广播 card.load）。
/// 抽牌链路＝卡组条目（加载实例）取件 → 加入手牌：起手装载＝静默版本（不发更新）；
/// 回合抽牌发 card.drawn → card.hand.add（顺序：drawn 先、hand.add 后；载荷均＝{ 玩家, 卡牌实例 }；粒度不同、并存）。
/// 空卡组抽牌＝抛错（集合契约）；"卡组为空的游戏层处理（疲劳等）"属后续批次。
/// 〔W1-1 G12 加性面〕对局级卡牌 ID 水位线：加载时逐张分配自增整数 ID（承载于卡上）→ 初始化加载完成后快照水位线
/// （＝已分配最大值；起手装载之前）→ 构筑外判定（ID ＞ 水位线）与访问口（本类 <see cref="IsOutsideDeck"/>）。
/// </summary>
public sealed class PlayerManager
{
    private readonly LogicEngine _engine;
    private readonly CardLibrary _library;
    private readonly List<Player> _players = new();
    private int _lastIssuedCardId;
    private int? _cardIdWatermark;

    internal PlayerManager(LogicEngine engine, CardLibrary library)
    {
        _engine = engine;
        _library = library;
    }

    /// <summary>双玩家（[0]＝玩家A/第一位玩家、[1]＝玩家B；顺序＝创建序）。</summary>
    public IReadOnlyList<Player> Players => _players;

    /// <summary>初始化动作：创建双玩家（资源初始：槽 0 / 点数 0；卡组＝传入名单；手牌空；HQ 随创建〔W3-3〕）。只允许一次。</summary>
    /// <exception cref="InvalidOperationException">双玩家已创建（重复创建被拒绝）。</exception>
    internal void CreatePlayers(CardList deckForPlayerA, CardList deckForPlayerB)
    {
        if (_players.Count != 0)
        {
            throw new InvalidOperationException("双玩家已创建（重复创建被拒绝）。");
        }

        _players.Add(new Player(0, deckForPlayerA, _engine)); // W3-3：引擎注入——HQ 随 Player 创建（构造期绑定）
        _players.Add(new Player(1, deckForPlayerB, _engine));
    }

    /// <summary>
    /// 对局开始卡牌加载（初始化动作；2A 新增）：逐玩家（索引升序＝玩家A 在前）逐张执行——
    /// 实例化（按定义类别产出三类卡之一）→ 装配到卡组条目 → 加载模板（模板内：对局级 ID 分配〔W1-1〕＋
    /// 元数据装配〔W1-1〕＋词条装载＋广播 card.load，载荷 {Card, Player}）。
    /// 加载顺序＝玩家索引升序 × 洗牌后卡组顺序（同种子＋同参 → 逐位稳定）；加载不改计数（仅装配）。
    /// 失败直接上抛（未注册 id / 加载模板异常＝初始化 fail-fast）。
    /// </summary>
    /// <exception cref="InvalidOperationException">双玩家尚未创建。</exception>
    /// <exception cref="KeyNotFoundException">卡组含未注册 id（实例化失败）。</exception>
    internal async Task LoadDecksAsync(CancellationToken ct = default)
    {
        foreach (var player in _players)
        {
            var deck = player.Deck;
            for (var i = 0; i < deck.Count; i++)
            {
                var card = _library.Instantiate(deck[i]);
                deck.AttachInstanceAt(i, card);
                await card.LoadAsync(player, ct);
            }
        }
    }

    // ---------- 对局级卡牌 ID 水位线（W1-1 G12 加性面） ----------

    /// <summary>
    /// 分配下一个对局级卡牌 ID（对局级自增序列；由加载路径经卡牌 ID 提供器逐张调用——
    /// 「分配顺序＝加载推进顺序」：玩家索引升序 × 洗牌后卡组顺序）。数值读面不作公开要求（内部面）。
    /// </summary>
    internal int NextCardId() => ++_lastIssuedCardId;

    /// <summary>
    /// 初始化加载完成后快照水位线（＝此时已分配卡牌 ID 的最大值——初始化域末位）。
    /// 调用时机＝双方卡组全量加载完成后、任何后续动作（起手装载等）之前；快照后 <see cref="IsOutsideDeck"/> 可用；
    /// 快照前判定＝明确失败。
    /// </summary>
    internal void SnapshotCardIdWatermark() => _cardIdWatermark = _lastIssuedCardId;

    /// <summary>水位线（已快照＝初始化域末位 ID；未快照＝null；数值读面——不作公开要求、内部面）。</summary>
    internal int? CardIdWatermark => _cardIdWatermark;

    /// <summary>
    /// 构筑外判定（W1-1；访问口）：卡牌 ID ＞ 初始化水位线＝true（构筑外生成牌）；≤＝false（初始化内卡）。
    /// 判定为纯读、无副作用、不触发更新（P1 精神）。
    /// 未分配 ID 的卡（未加载 / 独立构造 / 无提供器）＝明确失败（拒绝——不静默返回 false）；
    /// 水位线未快照（初始化加载未完成）＝明确失败。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">card 未分配对局级 ID；或水位线尚未快照。</exception>
    public bool IsOutsideDeck(CardBase card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (_cardIdWatermark is null)
        {
            throw new InvalidOperationException(
                "卡牌 ID 水位线尚未快照（初始化加载未完成）——构筑外判定不可用（明确失败）。");
        }

        if (card.MatchCardId is not { } id)
        {
            throw new InvalidOperationException(
                $"卡牌 '{card.Name}' 未分配对局级 ID（未加载/独立构造的卡不能参与构筑外判定——明确失败）。");
        }

        return id > _cardIdWatermark.Value;
    }

    /// <summary>起手装载（静默——不发更新）：重复 count 次执行抽牌链路。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">count 为负。</exception>
    /// <exception cref="InvalidOperationException">卡组为空（空集合 Draw 被拒绝）。</exception>
    public void LoadOpeningHand(Player player, int count)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "装载张数不能为负。");
        }

        for (var i = 0; i < count; i++)
        {
            DrawIntoHand(player);
        }
    }

    /// <summary>回合抽牌：抽 1 张入手牌并发 card.drawn → card.hand.add（顺序：drawn 先、hand.add 后；载荷均＝{ 玩家, 卡牌实例 }）；返回抽到的卡牌实例。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="InvalidOperationException">卡组为空（空集合 Draw 被拒绝）。</exception>
    public async Task<CardBase> DrawCard(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var card = DrawIntoHand(player);
        await GameUpdates.EmitCardDrawn(_engine, player, card, ct);
        await GameUpdates.EmitCardHandAdd(_engine, player, card, ct);
        return card;
    }

    /// <summary>抽牌核心链路（静默）：卡组条目取件（加载实例通道）→ 加入手牌。</summary>
    private CardBase DrawIntoHand(Player player)
    {
        var card = player.Deck.DrawInstance(); // 空集合 / 未加载抛错（集合契约）
        player.Hand.Add(card);
        return card;
    }
}
