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
/// </summary>
public sealed class PlayerManager
{
    private readonly LogicEngine _engine;
    private readonly CardLibrary _library;
    private readonly List<Player> _players = new();

    internal PlayerManager(LogicEngine engine, CardLibrary library)
    {
        _engine = engine;
        _library = library;
    }

    /// <summary>双玩家（[0]＝玩家A/第一位玩家、[1]＝玩家B；顺序＝创建序）。</summary>
    public IReadOnlyList<Player> Players => _players;

    /// <summary>初始化动作：创建双玩家（资源初始：槽 0 / 点数 0；卡组＝传入名单；手牌空；HQ 20）。只允许一次。</summary>
    /// <exception cref="InvalidOperationException">双玩家已创建（重复创建被拒绝）。</exception>
    internal void CreatePlayers(CardList deckForPlayerA, CardList deckForPlayerB)
    {
        if (_players.Count != 0)
        {
            throw new InvalidOperationException("双玩家已创建（重复创建被拒绝）。");
        }

        _players.Add(new Player(0, deckForPlayerA));
        _players.Add(new Player(1, deckForPlayerB));
    }

    /// <summary>
    /// 对局开始卡牌加载（初始化动作；2A 新增）：逐玩家（索引升序＝玩家A 在前）逐张执行——
    /// 实例化（按定义类别产出三类卡之一）→ 装配到卡组条目 → 加载模板（模板内广播 card.load，载荷 {Card, Player}）。
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
