using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Orc.Game.Managers;
using Orc.Game.Players;
using Orc.Game.Targeting;

namespace Orc.Game;

/// <summary>
/// 对局（KARDS 模仿第一批：对局骨架）：持有逻辑引擎、双玩家、回合序、战场与管理器群（回合 / 玩家 / 战场 / 资源 / 卡牌库 / 目标选择）。
/// 装配模式＝调用方提供数据、Match 负责装配：创建输入＝双方卡组名单（CardList×2）、卡牌定义集、可选种子（可复现）、可选先手指定（默认第一位玩家）、可选规则配置（指挥点上限）、可选目标选择桥接（第六员装配输入）。
/// 两步式：创建（准备态）→ 显式 <see cref="Initialize"/>（初始化完成置"进行"态）。
/// 状态门禁：回合推进仅"进行"态允许；准备态访问管理器与转发属性抛错；重复 <see cref="Initialize"/> 抛错（明确拒绝、非幂等）。
/// 失败模式：无效创建参数 → 创建期抛参数校验异常；初始化中异常直接传播（不承诺回滚；失败可重建对局）。
/// 初始化流程：生成管理器群 → 卡牌库批量注册 → 创建双玩家 → 双方卡组洗牌（初始化内自动、传对局随机源）→
/// 起手装载（静默、不发更新；先手 4 / 后手 5）→ 先手回合开始序列（3 条更新入总流）→ 置"进行"。
/// </summary>
public sealed class Match
{
    private const int OpeningHandSizeFirstPlayer = 4;
    private const int OpeningHandSizeSecondPlayer = 5;

    private readonly CardList _deckForPlayerA;
    private readonly CardList _deckForPlayerB;
    private readonly IReadOnlyList<CardDefinitionEntry> _cardDefinitions;
    private readonly Random _random;
    private readonly int _firstPlayerIndex;
    private readonly MatchOptions _options;

    private TurnManager? _turnManager;
    private PlayerManager? _playerManager;
    private BattlefieldManager? _battlefieldManager;
    private ResourceManager? _resourceManager;
    private CardLibrary? _cardLibrary;
    private TargeterManager? _targeterManager;
    private readonly ITargeterBridge? _targeterBridge;

    /// <summary>
    /// 创建对局（准备态；内部新建 LogicEngine 并公开）。装配校验：卡组名单非 null、非空、不含 null/空白 id；
    /// 先手指定为 0/1；指挥点上限为正整数；定义集允许为空（空库）。"未注册 id"不在创建期校验（初始化实例化时抛错）。
    /// </summary>
    /// <param name="deckForPlayerA">玩家A 的卡组名单（第一位玩家）。</param>
    /// <param name="deckForPlayerB">玩家B 的卡组名单。</param>
    /// <param name="cardDefinitions">卡牌定义集（条目＝id＋定义；初始化时批量注册）。</param>
    /// <param name="seed">可选随机种子（默认自动生成且事后经 <see cref="Seed"/> 可读）。</param>
    /// <param name="firstPlayerIndex">可选先手指定（0＝玩家A、1＝玩家B；默认第一位玩家）。</param>
    /// <param name="options">可选规则配置（指挥点上限；默认 12）。</param>
    /// <param name="targeterBridge">可选目标选择桥接（第六员〔目标选择管理器〕的装配输入；缺省＝null＝允许无桥接装配——Targeting 被调用时以失败结局暴露、不抛）。</param>
    /// <exception cref="ArgumentNullException">deckForPlayerA / deckForPlayerB / cardDefinitions 为 null。</exception>
    /// <exception cref="ArgumentException">卡组名单为空或含 null/空白 id；定义集含 null 条目。</exception>
    /// <exception cref="ArgumentOutOfRangeException">先手指定越界；指挥点上限非正整数。</exception>
    public Match(
        CardList deckForPlayerA,
        CardList deckForPlayerB,
        IEnumerable<CardDefinitionEntry> cardDefinitions,
        int? seed = null,
        int? firstPlayerIndex = null,
        MatchOptions? options = null,
        ITargeterBridge? targeterBridge = null)
    {
        ArgumentNullException.ThrowIfNull(deckForPlayerA);
        ArgumentNullException.ThrowIfNull(deckForPlayerB);
        ArgumentNullException.ThrowIfNull(cardDefinitions);

        if (deckForPlayerA.Count == 0)
        {
            throw new ArgumentException("玩家A的卡组名单为空（空名单在创建期被拒绝）。", nameof(deckForPlayerA));
        }

        if (deckForPlayerB.Count == 0)
        {
            throw new ArgumentException("玩家B的卡组名单为空（空名单在创建期被拒绝）。", nameof(deckForPlayerB));
        }

        ValidateDeckIds(deckForPlayerA, nameof(deckForPlayerA));
        ValidateDeckIds(deckForPlayerB, nameof(deckForPlayerB));

        var first = firstPlayerIndex ?? 0;
        if (first is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(firstPlayerIndex), firstPlayerIndex, "先手指定非法（须为 0＝玩家A 或 1＝玩家B）。");
        }

        var resolvedOptions = options ?? new MatchOptions();
        if (resolvedOptions.MaxPointSlots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), resolvedOptions.MaxPointSlots, "指挥点上限须为正整数（≥1）。");
        }

        var definitions = cardDefinitions.ToList();
        if (definitions.Any(entry => entry is null))
        {
            throw new ArgumentException("卡牌定义集含 null 条目。", nameof(cardDefinitions));
        }

        _deckForPlayerA = deckForPlayerA;
        _deckForPlayerB = deckForPlayerB;
        _cardDefinitions = definitions;
        Seed = seed ?? Random.Shared.Next();
        _random = new Random(Seed);
        _firstPlayerIndex = first;
        _options = resolvedOptions;
        _targeterBridge = targeterBridge;
        Engine = new LogicEngine();
    }

    /// <summary>对局引擎（公开；外部经此访问总线/总流以订阅更新——订阅须在 <see cref="Initialize"/> 前挂接）。</summary>
    public LogicEngine Engine { get; }

    /// <summary>对局状态（准备 / 进行）。</summary>
    public MatchState State { get; private set; } = MatchState.Preparing;

    /// <summary>本次实际使用的随机种子（传入则＝传入值；未传入则＝自动生成值，支撑事后复现）。</summary>
    public int Seed { get; }

    // ---------- 管理器群（五管理器公开；仅"进行"态可访问） ----------

    /// <summary>回合管理器（回合数、当前行动方真源）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public TurnManager TurnManager => RequireReady(_turnManager);

    /// <summary>玩家管理器（双玩家真源）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public PlayerManager PlayerManager => RequireReady(_playerManager);

    /// <summary>战场管理器（战场真源）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public BattlefieldManager BattlefieldManager => RequireReady(_battlefieldManager);

    /// <summary>资源管理器（指挥点结算规则服务）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public ResourceManager ResourceManager => RequireReady(_resourceManager);

    /// <summary>卡牌库（id → 定义注册表）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public CardLibrary CardLibrary => RequireReady(_cardLibrary);

    /// <summary>
    /// 目标选择管理器（管理器群"第六员"；随管理器群在 Initialize 内加性生成）。
    /// 独立构造路径（Manager 直建）不受影响、行为一致；未 Initialize 时经本属性访问＝沿用既有门禁模式（抛错）。
    /// </summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public TargeterManager TargeterManager => RequireReady(_targeterManager);

    // ---------- 便捷转发读面（只读、不复制状态；仅"进行"态可访问） ----------

    /// <summary>双玩家（→ 玩家管理器）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public IReadOnlyList<Player> Players => PlayerManager.Players;

    /// <summary>当前行动方（→ 回合管理器）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public Player CurrentPlayer => TurnManager.CurrentPlayer;

    /// <summary>回合数（→ 回合管理器）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public int TurnNumber => TurnManager.TurnNumber;

    /// <summary>战场（→ 战场管理器）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public Battlefield Battlefield => BattlefieldManager.Battlefield;

    /// <summary>
    /// 初始化（两步式第二步；完成后置"进行"态）：生成管理器群 → 卡牌库批量注册 → 创建双玩家 → 双方卡组洗牌 →
    /// 起手装载（静默）→ 先手回合开始序列（3 条更新）→ 置"进行"。初始化中异常直接传播（对局保持"准备"态）。
    /// </summary>
    /// <exception cref="InvalidOperationException">重复 Initialize（明确拒绝、非幂等）。</exception>
    public async Task Initialize(CancellationToken ct = default)
    {
        if (State != MatchState.Preparing)
        {
            throw new InvalidOperationException($"对局已初始化（当前状态：{State}）；重复 Initialize 被拒绝。");
        }

        // 生成并初始化管理器群
        _cardLibrary = new CardLibrary(Engine);
        foreach (var entry in _cardDefinitions)
        {
            _cardLibrary.Register(entry.Id, entry.Definition);
        }

        _resourceManager = new ResourceManager(_options.MaxPointSlots);
        _battlefieldManager = new BattlefieldManager();
        _playerManager = new PlayerManager(Engine, _cardLibrary);
        _playerManager.CreatePlayers(_deckForPlayerA, _deckForPlayerB);

        // 第六员（加性，随管理器群生成）：目标选择管理器——桥接可选注入（缺省 null＝允许无桥接装配，
        // Targeting 被调用时以失败结局暴露、不抛）；留痕经引擎既有渠道（总流）。
        _targeterManager = new TargeterManager(_targeterBridge, new EventStreamTargetingTrace(Engine.RootStream));

        // 双方卡组洗牌（初始化内自动；传对局随机源；固定顺序＝玩家索引升序，保证可复现）
        _playerManager.Players[0].Deck.Shuffle(_random);
        _playerManager.Players[1].Deck.Shuffle(_random);

        // 起手装载（静默、不发更新；先手 4 / 后手 5）
        var firstPlayer = _playerManager.Players[_firstPlayerIndex];
        var secondPlayer = _playerManager.Players[(_firstPlayerIndex + 1) % 2];
        _playerManager.LoadOpeningHand(firstPlayer, OpeningHandSizeFirstPlayer);
        _playerManager.LoadOpeningHand(secondPlayer, OpeningHandSizeSecondPlayer);

        // 先手回合开始序列（3 条更新；第 1 回合不抽牌＝无 card.drawn；顺序 await 完结后返回）
        _turnManager = new TurnManager(Engine, _playerManager, _resourceManager);
        await _turnManager.StartFirstTurn(firstPlayer, ct);

        State = MatchState.InProgress;
    }

    /// <summary>结束回合（主路径；无参、自动取当前行动方；仅"进行"态可调用）。</summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态（准备态推进被拒绝）。</exception>
    public async Task EndTurn(CancellationToken ct = default)
    {
        if (State != MatchState.InProgress)
        {
            throw new InvalidOperationException("对局尚未进入'进行'态，不能推进回合（须先成功完成 Initialize）。");
        }

        await TurnManager.EndTurn(ct);
    }

    /// <summary>管理器/转发读面的就绪门禁：仅"进行"态且管理器已生成时可用，否则抛错（准备态访问口径一致）。</summary>
    private T RequireReady<T>(T? manager)
        where T : class
        => manager is not null && State == MatchState.InProgress
            ? manager
            : throw new InvalidOperationException("对局尚未进入'进行'态：管理器群不可用（须先成功完成 Initialize）。");

    /// <summary>创建期名单格式校验：null/空白 id → 拒绝（格式错误 fail-fast；"未注册 id"不在此层校验）。</summary>
    private static void ValidateDeckIds(CardList deck, string paramName)
    {
        foreach (var id in deck)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("卡组名单含 null/空白 id（格式错误在创建期被拒绝）。", paramName);
            }
        }
    }
}
