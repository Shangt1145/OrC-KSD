using Orc.Cards;
using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Orc.Game.Players;

namespace Orc.Game.Managers;

/// <summary>
/// 玩家管理器（玩家领域真源）：创建双玩家、提供玩家访问、执行抽牌操作。
/// 抽牌链路＝卡组（名单）取首张并移除 → 经卡牌库实例化 → 加入手牌：
/// 起手装载＝静默版本（不发更新）；回合抽牌发 card.drawn（载荷＝{ 玩家, 卡牌实例 }）。
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

    /// <summary>起手装载（静默——不发更新）：重复 count 次执行抽牌链路。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">count 为负。</exception>
    /// <exception cref="InvalidOperationException">卡组为空（空集合 Draw 被拒绝）。</exception>
    /// <exception cref="KeyNotFoundException">卡组含未注册 id（实例化失败）。</exception>
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

    /// <summary>回合抽牌：抽 1 张入手牌并发 card.drawn（载荷＝{ 玩家, 卡牌实例 }）；返回抽到的卡牌实例。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="InvalidOperationException">卡组为空（空集合 Draw 被拒绝）。</exception>
    /// <exception cref="KeyNotFoundException">卡组含未注册 id（实例化失败）。</exception>
    public async Task<Card> DrawCard(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var card = DrawIntoHand(player);
        await GameUpdates.EmitCardDrawn(_engine, player, card, ct);
        return card;
    }

    /// <summary>抽牌核心链路（静默）：卡组取首张并移除 → 经由卡牌库实例化 → 加入手牌。</summary>
    private Card DrawIntoHand(Player player)
    {
        var id = player.Deck.Draw(); // 空集合抛错（集合契约）
        var card = _library.Instantiate(id); // 未注册 id 抛错（配置错误不吞）
        player.Hand.Add(card);
        return card;
    }
}
