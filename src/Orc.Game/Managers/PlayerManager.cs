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
/// 回合抽牌发 card.drawn → card.hand.add（顺序：drawn 先、hand.add 后；载荷均＝{ 玩家, 卡牌实例 }；粒度不同、并存）；
/// 满手（≥ HandLimit〔9〕）＝HandLimit 烧牌裁决（G7；直烧口径：不经手牌、手牌全程保持上限——发 card.drawn 恰一次
/// → 销毁〔引擎既有机制〕→ 发 card.discarded 恰一次；card.hand.add 零次；KARDS 烧牌语义；起手装载不受裁决）。
/// 手牌动作面（G7；范围修正后——KARDS 官方无弃牌堆/墓地语义）：弃置动作（从手牌移除＋销毁＋card.discarded 信号——
/// 归属口径＝卡当前所在手牌、拒绝语义明确、非幂等）与回迁动作（手牌 → 卡组：跨集合受控动作——移出＋装入一体；
/// 位置＝卡组顶/指定位置；静默）。弃置与烧牌共享「销毁」原语与 card.discarded 信号（烧牌不经弃置动作）。
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

    /// <summary>
    /// 回合抽牌（G7 起含 HandLimit 烧牌裁决）：抽 1 张——未满手＝加入手牌并发 card.drawn → card.hand.add
    /// （顺序：drawn 先、hand.add 后；载荷均＝{ 玩家, 卡牌实例 }）；满手（≥ HandLimit〔9〕）＝KARDS 烧牌语义
    /// （直烧口径：不经手牌、手牌全程保持上限——发 card.drawn 恰一次 → 销毁〔引擎既有机制〕→ 发 card.discarded 恰一次；card.hand.add 零次）。
    /// 返回抽到的卡牌实例（烧牌路径＝被烧卡引用——烧掉的牌「算被抽到」、不算「进过手牌」）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="InvalidOperationException">卡组为空（空集合 Draw 被拒绝）。</exception>
    public async Task<CardBase> DrawCard(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var card = player.Deck.DrawInstance(); // 空集合 / 未加载抛错（集合契约）；「移除」由抽取步骤自然完成
        if (player.Hand.Count >= Player.HandLimit)
        {
            // HandLimit 烧牌（直烧：不经手牌——手牌全程保持 9、无瞬时第 10 张；不经弃置动作——共享销毁原语与信号）
            await GameUpdates.EmitCardDrawn(_engine, player, card, ct);
            await DestroyAndEmitDiscardedAsync(player, card, ct);
        }
        else
        {
            player.Hand.Add(card);
            await GameUpdates.EmitCardDrawn(_engine, player, card, ct);
            await GameUpdates.EmitCardHandAdd(_engine, player, card, ct);
        }

        return card;
    }

    // ---------- 手牌动作面（G7：弃置 / 回迁；范围修正后——KARDS 官方无弃牌堆/墓地语义） ----------

    /// <summary>
    /// 弃置动作（G7 手牌操作与弃置）：从手牌移除 → 销毁（接线引擎既有销毁机制——生命周期终止＋效果清理）→
    /// 发射 <see cref="GameUpdates.CardDiscarded"/>（{ Card, Player }；恰一次）——三者为一个受控动作；
    /// 三路径（选择弃/随机弃/指定弃）＝调用方组合调用本动作（不设专属封装）。
    /// 归属口径＝「卡当前所在手牌」（不设「仅己方」限制——他方手牌的弃置经调用方组合合法可达）。
    /// 前提校验失败（卡不在所声明玩家手牌中）＝明确拒绝（可区分成功/拒绝；不静默、非幂等）；
    /// 对同一卡重复弃置＝第二次因前提失败而拒绝。次序：移除 → 销毁（含资源清理）→ 信号（观察者所见即终态）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player / card 为 null。</exception>
    /// <exception cref="InvalidOperationException">卡不在该玩家手牌中（前提校验失败——明确拒绝）。</exception>
    public async Task DiscardCardAsync(Player player, CardBase card, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(card);

        if (!player.Hand.Contains(card))
        {
            throw new InvalidOperationException(
                $"卡牌 '{card.Name}' 不在玩家 #{player.Index} 的手牌中（弃置动作被拒绝——归属口径＝卡当前所在手牌）。");
        }

        player.Hand.Remove(card); // 前提已校验：必命中（移除）——移除后处置链无失败点（成功即完整弃置）
        await DestroyAndEmitDiscardedAsync(player, card, ct);
    }

    /// <summary>
    /// 回迁动作（G7 回迁原子化）：将手牌中的卡装回该玩家卡组顶（位置＝索引 0——下次抽取取件端）；
    /// 跨集合受控动作（手牌移出＋装入卡组一体完成）；同一实例——移出手牌后被装入卡组、不丢失不重复（成功即完整迁移）。
    /// 输入面最小：仅凭「手牌中的卡引用」即可（条目 id 由实现自行解析）；回迁静默（不发信号）；
    /// 「洗入」＝本动作与既有洗切动作面（<see cref="Match.ShuffleDeckAsync"/>）的调用方组合（不设洗入专属封装）。
    /// 全体用例（"将所有手牌洗入卡组"）：手牌为空＝无操作（不洗切、不发信号——调用方组合语义）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player / card 为 null。</exception>
    /// <exception cref="InvalidOperationException">卡不在该玩家手牌中；或卡组侧已含同一实例（重复归属被拒绝）。</exception>
    public void ReturnToDeckTop(Player player, CardBase card) => ReturnToDeck(player, card, 0);

    /// <summary>
    /// 回迁动作（指定位置；G7）：0 基——索引 0＝下次抽取取件端、有效范围 0..N（N＝装入前集合大小；含尾部）。
    /// 其余语义同 <see cref="ReturnToDeckTop"/>（「尾部/其它位置」＝指定位置的特例 p＝N，不另设形态）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player / card 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">position 越界（明确拒绝）。</exception>
    /// <exception cref="InvalidOperationException">卡不在该玩家手牌中；或卡组侧已含同一实例（重复归属被拒绝）。</exception>
    public void ReturnToDeck(Player player, CardBase card, int position)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(card);

        if (!player.Hand.Contains(card))
        {
            throw new InvalidOperationException(
                $"卡牌 '{card.Name}' 不在玩家 #{player.Index} 的手牌中（回迁动作被拒绝——归属口径＝卡当前所在手牌）。");
        }

        if (position < 0 || position > player.Deck.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), position, $"回迁位置越界（合法范围：0..{player.Deck.Count}——0＝卡组顶/下次抽取取件端）。");
        }

        if (player.Deck.ContainsInstance(card))
        {
            throw new InvalidOperationException(
                $"卡牌 '{card.Name}' 已在玩家 #{player.Index} 的卡组中（重复归属被拒绝——实例唯一归属不变量）。");
        }

        var definitionId = ResolveDefinitionId(card); // 素材解析（可能拒绝——先于任何变更）

        player.Hand.Remove(card); // 全部校验通过：移出 → 装入（两步之间无失败点——成功即完整迁移）
        player.Deck.InsertInstanceAt(position, definitionId, card);
    }

    /// <summary>抽牌核心链路（静默）：卡组条目取件（加载实例通道）→ 加入手牌。</summary>
    private CardBase DrawIntoHand(Player player)
    {
        var card = player.Deck.DrawInstance(); // 空集合 / 未加载抛错（集合契约）
        player.Hand.Add(card);
        return card;
    }

    /// <summary>销毁＋发射 card.discarded（G7 弃置动作与烧牌共享的处置链：销毁〔含资源清理〕先、信号后）。</summary>
    private async Task DestroyAndEmitDiscardedAsync(Player player, CardBase card, CancellationToken ct)
    {
        await _engine.DestroyCard(card);
        await GameUpdates.EmitCardDiscarded(_engine, card, player, ct);
    }

    /// <summary>
    /// 解析卡牌的定义注册 id（回迁素材：经卡牌库反查——定义实例引用相等；按注册序取首个匹配）。
    /// 未注册于本对局卡牌库（如独立构造的卡）＝明确拒绝（不静默）。
    /// </summary>
    private string ResolveDefinitionId(CardBase card)
    {
        foreach (var pair in _library.Definitions)
        {
            if (ReferenceEquals(pair.Value, card.Definition))
            {
                return pair.Key;
            }
        }

        throw new InvalidOperationException(
            $"卡牌 '{card.Name}' 的定义未注册于本对局卡牌库（回迁素材解析失败——明确拒绝）。");
    }
}
