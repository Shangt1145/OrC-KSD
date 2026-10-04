using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Orc.Game.Commanding;
using Orc.Game.Managers;
using Orc.Game.Players;
using Orc.Game.Targeting;
using Orc.Game.Triggers;

namespace Orc.Game;

/// <summary>
/// 对局（KARDS 模仿；第一批对局骨架＋2A 结构层＋2B 打出链＋2C 指挥与词条＋后置项补全）：持有逻辑引擎、双玩家、回合序、战场、管理器群（回合 / 玩家 / 战场 / 资源 / 卡牌库 / 目标选择 / 打出 / 指挥）
/// 与对局级触发器注册表（2A 机制：登记本体＋分层分类；2C 起内置流程触发器注册为底层）。
/// 装配模式＝调用方提供数据、Match 负责装配：创建输入＝双方卡组名单（CardList×2）、卡牌定义集、可选种子（可复现；G8：确定性随机服务的显式化入口——对局内随机消费统一经随机服务）、可选先手指定（默认第一位玩家）、可选规则配置（指挥点上限）、可选目标选择桥接（第六员装配输入）、可选效果工厂注册表（X2 加性装配输入——卡牌加载时效果装载的装配源）。
/// 两步式：创建（准备态）→ 显式 <see cref="Initialize"/>（初始化完成置"进行"态）→〔HQ≤0 时〕"结束"态（立即终局：状态置结束＋胜者记录）。
/// 状态门禁：回合推进仅"进行"态允许；准备态访问管理器与转发属性抛错；重复 <see cref="Initialize"/> 抛错（明确拒绝、非幂等）；
/// 终局后（"结束"态）：所有游戏动作入口拒绝（指挥/打出/移动/攻击/回合推进/初始化等——对外面拒绝、零副作用）、
/// 只读查询面（状态/胜者/玩家与 HQ/战场/管理器/集合）保持可用、更新流不再增长（其后所有效果不再处理）。
/// 失败模式：无效创建参数 → 创建期抛参数校验异常；初始化中异常直接传播（不承诺回滚；失败可重建对局）。
/// 初始化流程（2A 固定链＋2C 加性＋W1-1 加性＋W4-1 加性）：管理器群（卡牌库批量注册 → 资源 → 玩家 → 战场〔构造期 HQ 占位〕→ 指挥管理器〔2C：流程触发器创建＋底层注册〕）→
/// 双方卡组洗切（W4-1：经统一洗切动作面 <see cref="ShuffleDeckAsync"/>——传对局随机服务〔G8：确定性单流〕、各发一条 deck.shuffled 信号）→ 加载（逐张 card.load；含对局级 ID 分配与元数据装配〔W1-1〕、词条装载〔2C〕；A 组后 B 组、组内洗牌后顺序）→
/// ID 水位线快照〔W1-1：起手装载之前〕→ 起手装载（静默、不发更新；先手 4 / 后手 5）→
/// 〔2C 接线：回合恢复钩子〕→ 先手回合开始序列（3 条更新入总流）→ 置"进行"。
/// </summary>
public sealed class Match
{
    private const int OpeningHandSizeFirstPlayer = 4;
    private const int OpeningHandSizeSecondPlayer = 5;

    private readonly CardList _deckForPlayerA;
    private readonly CardList _deckForPlayerB;
    private readonly IReadOnlyList<CardDefinitionEntry> _cardDefinitions;
    private readonly MatchRandomService _randomService;
    private readonly int _firstPlayerIndex;
    private readonly MatchOptions _options;

    private TurnManager? _turnManager;
    private PlayerManager? _playerManager;
    private BattlefieldManager? _battlefieldManager;
    private ResourceManager? _resourceManager;
    private CardLibrary? _cardLibrary;
    private TargeterManager? _targeterManager;
    private PlayManager? _playManager;
    private CommandManager? _commandManager;
    private readonly ITargeterBridge? _targeterBridge;
    private readonly CardEffectRegistry? _effectRegistry;
    private readonly MatchLifecycle _lifecycle = new();
    private GameEnvironment? _environment; // W3-1 G4：游戏环境（对局装配期创建；多对局相互独立）

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
    /// <param name="effectRegistry">可选效果工厂注册表（X2 加性装配输入；卡牌加载时效果装载的装配源；缺省＝null＝无效果源——卡加载时无「声明效果」装载，装载语境照常〔卡上手动装配的效果仍被装载〕）。</param>
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
        ITargeterBridge? targeterBridge = null,
        CardEffectRegistry? effectRegistry = null)
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
        Seed = seed ?? Random.Shared.Next(); // 创建期一次性种子生成（未显式传入时）——不构成对局内随机消费（对局内统一经随机服务）
        _randomService = new MatchRandomService(Seed, () => _lifecycle.State);
        _firstPlayerIndex = first;
        _options = resolvedOptions;
        _targeterBridge = targeterBridge;
        _effectRegistry = effectRegistry;
        Engine = new LogicEngine();
    }

    /// <summary>对局引擎（公开；外部经此访问总线/总流以订阅更新——订阅须在 <see cref="Initialize"/> 前挂接）。</summary>
    public LogicEngine Engine { get; }

    /// <summary>对局状态（准备 / 进行 / 结束——经生命周期对象读；"结束"＝HQ≤0 立即终局，不可逆）。</summary>
    public MatchState State => _lifecycle.State;

    /// <summary>
    /// 胜者（只读面；仅终局后非 null：＝使对方 HQ 归零的一方；非终局＝null）。
    /// 平局不处理（本批无同时归零路径、不定义平局值）。
    /// </summary>
    public Player? Winner => _lifecycle.Winner;

    /// <summary>本次实际使用的随机种子（传入则＝传入值；未传入则＝自动生成值，支撑事后复现）。</summary>
    public int Seed { get; }

    // ---------- 触发器注册表（对局级机制；2A） ----------

    /// <summary>
    /// 触发器注册表（对局级；与对局生命周期一致——创建即就绪、随对局回收；每对局一份）。
    /// 登记「触发器对象本体＋分层分类」；注册与挂载/运行完全解耦；本批声明位不注册（真实注册自 2C）。
    /// </summary>
    public TriggerRegistry TriggerRegistry { get; } = new();

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

    /// <summary>
    /// 打出管理器（2B 加性：打出链服务与交互入口——单位预打出 / 单位打出链 / 加入 / 指令 / 反制；
    /// 随管理器群在 Initialize 内加性生成；回合管理器为其反制「仅己方回合」的真源）。
    /// 独立构造路径（Manager 直建）不受影响（回合上下文可缺省、反制使用将抛明确异常）；
    /// 未 Initialize 时经本属性访问＝沿用既有门禁模式（抛错）。
    /// </summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public PlayManager PlayManager => RequireReady(_playManager);

    /// <summary>
    /// 指挥管理器（2C 加性：指挥流程入口 / 动作可用性聚合判定 / 移动·攻击触发器 / 「造成攻击伤害」共享触发器 /
    /// 守护维护 / 回合恢复；随管理器群在 Initialize 内加性生成——创建时点为卡牌加载之前〔词条装载依赖〕；
    /// 其四个内置流程触发器在 Initialize 内注册为底层触发器〔进注册表、可查询〕）。
    /// 独立构造路径（Manager 直建）不受影响；未 Initialize 时经本属性访问＝沿用既有门禁模式（抛错）。
    /// </summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public CommandManager CommandManager => RequireReady(_commandManager);

    // ---------- 构筑外判定（W1-1 G12 加性面） ----------

    /// <summary>
    /// 构筑外判定（W1-1；访问口——转发玩家管理器判定）：卡牌对局级 ID ＞ 初始化水位线＝true（构筑外生成牌）；
    /// ≤＝false（初始化内卡）。判定为纯读、无副作用、不触发更新。
    /// 未分配 ID 的卡（未加载 / 独立构造）＝明确失败（拒绝——不静默返回 false）；水位线未快照＝明确失败。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">card 未分配对局级 ID；或水位线尚未快照；或对局尚未进入"进行"态。</exception>
    public bool IsOutsideDeck(CardBase card) => PlayerManager.IsOutsideDeck(card);

    // ---------- 随机服务（第 2 批 G8） ----------

    /// <summary>
    /// 随机服务（对外取用面；第 2 批 G8 加性）：数值/取样原语（Next / PickOne / PickN——确定性 PRNG、
    /// 单流、顺序确定；效果运行期经「卡 → 玩家 → 服务」接入面取用）。**仅"进行"态可用**：准备态对外请求
    /// 与终局后取用＝明确拒绝（抛错——风格与既有管理器门禁对齐）；对局内部链路（初始化洗切等）不经本属性、
    /// 直接经服务本体消费（准备态合法）。
    /// </summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态；或对局已结束（终局）。</exception>
    public MatchRandomService RandomService => State switch
    {
        MatchState.InProgress => _randomService,
        MatchState.Ended => throw new InvalidOperationException("对局已结束（终局），随机服务对外取用被拒绝。"),
        _ => throw new InvalidOperationException("对局尚未进入'进行'态：随机服务对外取用不可用（须先成功完成 Initialize）。"),
    };

    // ---------- 对局受控动作面（W4-1 G14 收尾） ----------

    /// <summary>
    /// 洗切动作（G14 收尾；对指定玩家卡组执行一次洗切＋发射 <see cref="GameUpdates.DeckShuffled"/> 信号——恰一次）；
    /// 「统一经此」的最小公共受控入口：初始化路径（双方卡组自动洗切）与后续「洗切卡组」类效果路径共用本动作面。
    /// 随机源＝对局随机服务（G8：确定性 PRNG、单流——与效果取样共流；同种子可复现）；发射时机＝洗切动作生效处（就地打乱之后）。
    /// 拒绝：player 为 null；玩家管理器尚未创建（未进入初始化）；指定玩家不属于本对局；对局已结束（终局后动作入口拒绝）。
    /// 初始化链内调用（"准备"态）＝合法（对局内部动作面）；对外调用仍受终局门禁。
    /// </summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="InvalidOperationException">玩家管理器尚未创建；或对局已结束（终局）。</exception>
    /// <exception cref="ArgumentException">指定玩家不属于本对局（洗切动作被拒绝）。</exception>
    public async Task ShuffleDeckAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var manager = _playerManager
            ?? throw new InvalidOperationException("玩家管理器尚未创建（洗切动作不可用——须先进入初始化）。");
        if (State == MatchState.Ended)
        {
            throw new InvalidOperationException("对局已结束（终局），洗切动作被拒绝。");
        }

        if (!manager.Players.Contains(player))
        {
            throw new ArgumentException("指定玩家不属于本对局（洗切动作被拒绝）。", nameof(player));
        }

        player.Deck.Shuffle(_randomService);
        await GameUpdates.EmitDeckShuffled(Engine, player, player.Deck, ct);
    }

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
    /// 游戏环境（W3-1 G4 加性面；对局装配期创建）：环境现算查询面（相邻/前线/战线定位——纯只读）＋
    /// 场级收集面（<see cref="Board.GameEnvironment.Auras"/>——光环声明注册/注销）＋全域重跑传播
    /// （环境类事件 → 全卡链重跑；事件驱动自动面＋<see cref="Board.GameEnvironment.RerunAllCardsAsync"/> 显式驱动面）。
    /// 仅"进行"与"结束"态可读（准备态＝抛错——沿用管理器门禁模式）。
    /// </summary>
    /// <exception cref="InvalidOperationException">对局尚未进入"进行"态。</exception>
    public GameEnvironment Environment => RequireReady(_environment);

    /// <summary>
    /// 初始化（两步式第二步；完成后置"进行"态）：管理器群（卡牌库批量注册 → 资源 → 玩家 → 战场〔构造期 HQ 占位〕→ 指挥管理器〔2C 加性：流程触发器创建＋底层注册〕）→
    /// 双方卡组洗切（W4-1：经 <see cref="ShuffleDeckAsync"/> 各发一条 deck.shuffled 信号）→ 加载（逐张 card.load；含对局级 ID 分配与元数据装配〔W1-1〕、词条装载〔2C〕）→ ID 水位线快照〔W1-1：起手装载之前〕→
    /// 起手装载（静默）→〔2C 接线：回合恢复钩子〕→ 先手回合开始序列（3 条更新）→ 置"进行"。初始化中异常直接传播（对局保持"准备"态）。
    /// </summary>
    /// <exception cref="InvalidOperationException">重复 Initialize（明确拒绝、非幂等）。</exception>
    public async Task Initialize(CancellationToken ct = default)
    {
        if (State != MatchState.Preparing)
        {
            throw new InvalidOperationException($"对局已初始化（当前状态：{State}）；重复 Initialize 被拒绝。");
        }

        // 生成并初始化管理器群（2B：卡牌库注入回合上下文提供器——反制「仅己方回合」验证的延迟读取来源；
        // 2C：加注词条装载上下文提供器——词条装载〔伏击挂载〕的延迟读取来源；
        // W1-1：加注对局级卡牌 ID 提供器——加载时分配自增 ID〔构筑外判定基础〕的延迟读取来源；
        // X2：加注效果装载上下文提供器〔对局装载语境——效果源可空；注册表缺省时声明为空、装载照常〕；
        // lambda 延迟求值：加载期读取可能早于目标对象创建，运行期（使用）时已就绪）
        _cardLibrary = new CardLibrary(
            Engine,
            () => _turnManager?.CurrentPlayer,
            () => _commandManager?.KeywordLoadContext,
            () => _playerManager?.NextCardId()
                ?? throw new InvalidOperationException("卡牌 ID 分配不可用：玩家管理器尚未创建（加载链时序错误）。"),
            cardId => new CardEffectLoadContext(_effectRegistry, cardId));
        foreach (var entry in _cardDefinitions)
        {
            _cardLibrary.Register(entry.Id, entry.Definition);
        }

        _resourceManager = new ResourceManager(_options.MaxPointSlots);
        _playerManager = new PlayerManager(Engine, _cardLibrary);
        _playerManager.CreatePlayers(_deckForPlayerA, _deckForPlayerB);
        // 2A 受控变更：战场构造期含 HQ 占位（各支援线槽 0＝对应玩家），创建顺序随之为玩家先、战场后。
        _battlefieldManager = new BattlefieldManager(_playerManager.Players[0], _playerManager.Players[1]);

        // W3-1 G4 加性：游戏环境（环境现算查询面＋场级收集面＋全域重跑传播）——随对局装配创建
        // （战场创建后、卡加载前），并注入各玩家（「卡 → 玩家 → 对局/战场」读取路径的玩家环节；
        // 时序：须先于卡加载——效果装载注册与链/条件环境读取依赖环境就绪）。
        _environment = new GameEnvironment(_battlefieldManager.Battlefield, Engine);
        foreach (var player in _playerManager.Players)
        {
            player.ConfigureEnvironment(_environment);
        }

        // G8 加性：对局随机服务注入各玩家——「卡 → 玩家 → 服务」读取路径的玩家环节（效果运行期取用）；
        // 时序与装配一致（先于卡加载；显式、可测试——无隐藏全局单例）。
        foreach (var player in _playerManager.Players)
        {
            player.ConfigureRandomService(_randomService);
        }

        // 第六员（加性，随管理器群生成）：目标选择管理器——桥接可选注入（缺省 null＝允许无桥接装配，
        // Targeting 被调用时以失败结局暴露、不抛）；留痕经引擎既有渠道（总流）。
        // 终局门禁（后置项 B）：装配终局读取提供器——对局已结束＝发起（新入队）即时失败、零副作用。
        _targeterManager = new TargeterManager(_targeterBridge, new EventStreamTargetingTrace(Engine.RootStream));
        _targeterManager.GameEndedProvider = () => _lifecycle.IsEnded;

        // 2C 加性：指挥管理器（引擎侧创建四个内置流程触发器——指挥 / 单位移动 / 单位攻击 / 造成攻击伤害）
        // ＋注册为底层触发器（进注册表、分层可查询）；创建时点＝卡牌加载之前（词条装载依赖「造成攻击伤害」就绪）。
        // 后置项 B 加性：注入对局生命周期（终局门禁＋HQ≤0 胜者记录）。
        _commandManager = new CommandManager(
            Engine,
            _battlefieldManager.Battlefield,
            _targeterManager,
            _playerManager.Players,
            () => _turnManager?.CurrentPlayer,
            _lifecycle);
        TriggerRegistry.Register(_commandManager.CommandTrigger, TriggerLayer.LowLevel);
        TriggerRegistry.Register(_commandManager.UnitMoveTrigger, TriggerLayer.LowLevel);
        TriggerRegistry.Register(_commandManager.UnitAttackTrigger, TriggerLayer.LowLevel);
        TriggerRegistry.Register(_commandManager.AttackDamageTrigger, TriggerLayer.LowLevel);

        // W3-3（G11 HQ 实体化）：HQ 数值路径「装配完成点」——初始基线快照（与单位化先例一致：
        // 以基准状态建立修饰机制比较基线；本时点无修饰/无损伤＝零变化、零发射）。
        // 此后一切 HQ 数值变更（伤害/修饰/撤销）均可被检测与集中触发（「改变才传播」）。
        foreach (var player in _playerManager.Players)
        {
            await player.Hq.Modifiers.RequestRerunAsync(ct);
        }

        // 双方卡组洗切（初始化内自动；W4-1 G14 收尾：经统一「洗切动作」面——每副各发恰一条 deck.shuffled 信号〔就地打乱之后〕；
        // 固定顺序＝玩家索引升序，保证可复现；G8：随机源＝对局随机服务——洗切与效果取样共用同一流〔单流〕）
        await ShuffleDeckAsync(_playerManager.Players[0], ct);
        await ShuffleDeckAsync(_playerManager.Players[1], ct);

        // 对局开始卡牌加载（2A 新增；固定链：洗牌 → 加载 → 起手装载 → 三连）：
        // 逐张经卡牌基类加载模板（逐张广播 card.load，载荷 {Card, Player}；A 组先、B 组后、组内洗牌后顺序；加载不改计数）
        await _playerManager.LoadDecksAsync(ct);

        // W1-1：卡牌 ID 水位线快照（＝已分配 ID 的最大值；语义＝初始化卡牌加载完成后、任何后续动作〔起手装载等〕之前）
        _playerManager.SnapshotCardIdWatermark();

        // 起手装载（静默、不发更新；先手 4 / 后手 5）
        var firstPlayer = _playerManager.Players[_firstPlayerIndex];
        var secondPlayer = _playerManager.Players[(_firstPlayerIndex + 1) % 2];
        _playerManager.LoadOpeningHand(firstPlayer, OpeningHandSizeFirstPlayer);
        _playerManager.LoadOpeningHand(secondPlayer, OpeningHandSizeSecondPlayer);

        // 先手回合开始序列（3 条更新；第 1 回合不抽牌＝无 card.drawn；顺序 await 完结后返回）
        _turnManager = new TurnManager(Engine, _playerManager, _resourceManager, _lifecycle);
        // 2C 接线：单位行动状态恢复（回合开始处理段——行动方在场单位重置两 bool＋词条运行态清零）
        _turnManager.ActionStateRefresher = _commandManager.RefreshActionStates;
        // 打出管理器（2B 加性：随管理器群生成——打出链服务与交互入口；回合管理器为反制「仅己方回合」真源；
        // 后置项 B 加性：注入对局生命周期——终局门禁）
        _playManager = new PlayManager(Engine, _targeterManager, _battlefieldManager.Battlefield, _turnManager, _lifecycle);
        await _turnManager.StartFirstTurn(firstPlayer, ct);

        _lifecycle.MarkInProgress();
    }

    /// <summary>结束回合（主路径；无参、自动取当前行动方；仅"进行"态可调用；终局后拒绝）。</summary>
    /// <exception cref="InvalidOperationException">对局已结束（终局，不能推进回合）；或对局尚未进入"进行"态（准备态推进被拒绝）。</exception>
    public async Task EndTurn(CancellationToken ct = default)
    {
        if (State == MatchState.Ended)
        {
            throw new InvalidOperationException("对局已结束（终局），不能推进回合。");
        }

        if (State != MatchState.InProgress)
        {
            throw new InvalidOperationException("对局尚未进入'进行'态，不能推进回合（须先成功完成 Initialize）。");
        }

        await TurnManager.EndTurn(ct);
    }

    /// <summary>
    /// 管理器/转发读面的就绪门禁：准备态不可用（抛错）；"进行"与"结束"态均可读（终局后只读查询面保持可用）。
    /// </summary>
    private T RequireReady<T>(T? manager)
        where T : class
        => manager is not null && State != MatchState.Preparing
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
