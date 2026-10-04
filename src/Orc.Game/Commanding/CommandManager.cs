using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Players;
using Orc.Game.Targeting;
using Orc.Game.Triggers;

namespace Orc.Game.Commanding;

// ─────────────────────────────────────────────────────────────────────────────
// 2C 指挥管理器（指挥系统承载；后置项 A/B/C 加性：反击豁免 / 胜负判定 / 前线阻挡）：
// ①内置流程触发器（引擎侧创建——指挥触发器 / 单位移动触发器 / 单位攻击触发器 / 造成攻击伤害触发器；
//   由对局装配期注册为底层触发器〔进注册表、可查询〕）；
// ②指挥入口（BeginCommandAsync）：一次拖拽＝一次调用链、单一公开入口方法驱动；输入＝被拖动单位引用；
//   发起判定（非己方回合 / 归属无效 / 已死亡 / 两动作均不可用 / 对局已结束）→ 指挥触发器（流程编排：读判定 → 交互 →
//   分派〔空槽→移动；敌方单位/敌方 HQ 实体引用→攻击〕→ 嵌套执行 → 外层收尾〔扣费＋两 bool 更新〕）；
// ③动作可用性聚合判定公开面（GetCommandAvailability）：bool＋原因＋候选；与流程内部同源、纯查询；
//   攻击目标筛选含轰炸机拦截（后置项 C：目标所在战线存在敌方战斗机时该战线非战斗机目标置黑——含 HQ）；
// ④移动执行（仅推进：支援线→前线；前置＝前线不存在存活敌方单位〔后置项 C；候选＋执行复验双保险〕；
//   候选＝前线空槽；无后撤/横移候选；发射 unit.position.changed——恰一次）；
// ⑤攻击执行（单位 vs 单位必经「造成攻击伤害」触发器中转；HQ 简路＝伤害经 HQ 数值路径与管线
//   〔改写→介入→应用→跑链集中触发〕、不反击、不走「造成攻击伤害」）；攻击结算＝按反击豁免判定表
//   （后置项 A：目标轰炸机永不反击 / 攻击者炮兵不受反击 / 攻击者轰炸机不受反击〔目标战斗机例外〕/ 其余正常；
//   多类型逐条适用、豁免优先；同时互伤框架——豁免方不结算反击伤害）→ HP≤0 死亡（统一死亡流程：清位＋置毁＋
//   效果注销＋Position 置空＋card.died 恰一次）；HQ≤0 → HQ 侧统一响应（W3-3：终局判定迁至 HQ——归零检查
//   〔响应 card.stat.changed〕执行终局记录：状态置结束＋胜者＝HQ 归零方之对手；其后动作入口拒绝，
//   当次结算收尾照常完成）；
// ⑥守护维护（被守护状态：相邻〔同线槽位索引差 1〕守护者 → 获被守护；仅能被炮/轰攻击；守护者自身不可被守护；
//   由位置/入场/死亡更新驱动重算——底层链负责）；⑦回合恢复（turn.start 处理段：行动方在场单位重置两 bool）。
// 「两 bool 只在外层更新」：内层（移动/攻击/伤害/词条）一律不写——法定写入点＝指挥收尾 / 部署链收尾（闪击）/ 回合恢复。
// W2b G3 接线（加性）：数值读取面接改——攻击力/防御力/行动费结算读点改读「有效值」（修饰机制缓存；
//   伤害值/HQ 伤害＝攻击有效值、扣减经门户〔ApplyDefenseDamageAsync〕、死亡判定＝防御有效值、可用性/扣费/复验＝行动费有效值）；
//   防御归零统一死亡衔接——被动触发器（响应 card.stat.changed；外部订阅通知完成之后检查）「防御有效值 ≤0 且未死亡」
//   → 统一死亡流程〔死亡清理＝效果/修饰器注销＋数值整合零新发射；先数值变化、后死亡信号〕（含修饰撤销/到期等
//   非伤害来源的 ≤0 边界；伤害路径判定为防护兜底——已死亡跳过，死亡恰一次）；互伤结算含死亡冻结防护（尸体不结算）。
// W2c X1/X2 加性：
// ①X1 触发者卡牌（TriggerCard）——4 个内置流程触发器视图承载「引发该操作的效果宿主卡引用」；填充规则：
//   主动指挥（含其嵌套调用）＝缺省空（不携带）；效果引发＝调用方显式携带（BeginCommandAsync 可选参数 / 触发器数据键）
//   且随同一效果链传递（指挥→移动/攻击；攻击→伤害链内一致）；复验只消费操作角色引用（前位、插入序），
//   触发者引用置于尾部、不参与复验（去重后计数允许多余项）。
// ②X2 效果卸载（死亡链）——修饰器清理之后、card.died 之前逐效果走统一卸载链（容器移除＋OnUnmount＋撤销登记
//   ＋总线卸载；托管清理幂等——修饰器已清、零发射），保持「先数值变化→清理→card.died」观察序。
// W3-3 G11 加性（HQ 实体化）：
// ①HQ 目标承载＝HQ 实体引用（hq.Ref）——候选/交互/执行以实体引用为准（槽位引用不再作为 HQ 目标产出；
//   槽位关系仅用于布局语义判定〔占位/邻位/守护/轰炸机拦截/范围矩阵基准〕）；
// ②攻击 HQ（简路骨架保持：不反击、不经过「造成攻击伤害」、不触发死亡/防御归零单位语义）：伤害数值改走
//   HQ 数值路径与管线（改写→介入→应用→跑链集中触发）；终局判定不内联——HQ 侧统一响应（归零检查触发器：
//   任何来源致 HQ 血量 ≤0 均经同一判定；「先数值变化、后终局记录」观察序）；
// ③被守护状态以 HQ 实体为单元维护（守护者/拦截基准仍为槽位关系与既有语义）；
// ④HQ 终局响应装配注入（延迟读取；胜者＝HQ 归零方之对手；脱局＝防御降级）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 指挥管理器（2C；对局级服务）：指挥流程入口与编排、KARDS 规则矩阵、
/// 攻击结算与统一死亡流程、守护维护、回合恢复。
/// 依赖：引擎（发射/触发）、战场（候选计算与布局）、目标选择管理器（交互承载）、
/// 玩家对（敌我判定）、当前行动方提供器（延迟读取——装配顺序：回合管理器后置）、
/// 对局生命周期（终局门禁与胜者记录——后置项 B；可空＝独立构造场景无门禁）。
/// </summary>
public sealed class CommandManager
{
    /// <summary>默认基础互伤在「造成攻击伤害」触发器内的事件优先级（数值大者后执行——伏击改写先判定）。</summary>
    private const int DefaultDamagePriority = 100;

    private readonly LogicEngine _engine;
    private readonly Battlefield _battlefield;
    private readonly TargeterManager _targeterManager;
    private readonly IReadOnlyList<Player> _players;
    private readonly Func<Player?> _currentPlayerProvider;
    private readonly MatchLifecycle? _lifecycle;
    private readonly Trigger<CardTriggerView> _defenseDepletionTrigger; // W2b：防御归零检查（被动；挂载于更新总线）
    private readonly Trigger<CardTriggerView> _hqZeroTrigger; // W3-3：HQ 归零检查（被动；挂载于更新总线）

    private readonly HashSet<UnitCard> _guardedUnits = new();
    private readonly HashSet<Hq> _guardedHqs = new(); // W3-3：被守护 HQ 以 HQ 实体为单元

    /// <summary>
    /// 创建指挥管理器（创建四个内置流程触发器并装配默认链；订阅位置/入场/死亡更新以维护被守护状态）。
    /// </summary>
    /// <param name="engine">对局引擎（发射更新 / 触发子触发器）。</param>
    /// <param name="battlefield">战场（候选计算与布局真源）。</param>
    /// <param name="targeterManager">目标选择管理器（指挥交互承载——一次拖拽＝一次请求）。</param>
    /// <param name="players">双玩家（敌我判定；行动方＋其对手）。</param>
    /// <param name="currentPlayerProvider">当前行动方提供器（延迟读取；缺省＝null＝回合未就绪——发起校验将拒绝）。</param>
    /// <param name="lifecycle">对局生命周期（终局门禁＋胜者记录——后置项 B；缺省＝null＝独立构造场景无门禁）。</param>
    /// <exception cref="ArgumentNullException">engine / battlefield / targeterManager / players 为 null。</exception>
    /// <exception cref="ArgumentException">players 不是两名玩家。</exception>
    public CommandManager(
        LogicEngine engine,
        Battlefield battlefield,
        TargeterManager targeterManager,
        IReadOnlyList<Player> players,
        Func<Player?>? currentPlayerProvider = null,
        MatchLifecycle? lifecycle = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(targeterManager);
        ArgumentNullException.ThrowIfNull(players);
        if (players.Count != 2)
        {
            throw new ArgumentException("指挥管理器需要恰两名玩家（敌方判定依据）。", nameof(players));
        }

        _engine = engine;
        _battlefield = battlefield;
        _targeterManager = targeterManager;
        _players = players;
        _currentPlayerProvider = currentPlayerProvider ?? (() => null);
        _lifecycle = lifecycle;

        // ① 指挥触发器（内置；默认链＝指挥流程编排）
        CommandTrigger = new Trigger<CommandTriggerView>("指挥触发器");
        CommandTrigger.Register("指挥流程", HandleCommandFlowAsync);

        // ② 单位移动触发器（执行前复验＝验证机制承载；默认链＝移动执行）
        UnitMoveTrigger = new DelegateCheckTrigger<UnitMoveTriggerView>("单位移动触发器", RevalidateMove);
        UnitMoveTrigger.Register("移动执行", HandleUnitMoveAsync);

        // ③ 单位攻击触发器（执行前复验＝验证机制承载；默认链＝攻击执行）
        UnitAttackTrigger = new DelegateCheckTrigger<UnitAttackTriggerView>("单位攻击触发器", RevalidateAttack);
        UnitAttackTrigger.Register("攻击执行", HandleUnitAttackAsync);

        // ④ 造成攻击伤害触发器（与对局同生的内置共享流程触发器；伏击加载时在此注册改写逻辑）
        //    默认基础互伤＝高优先级（后执行）——伏击改写（注册默认优先级）先判定；改写生效＝替代默认。
        AttackDamageTrigger = new Trigger<AttackDamageTriggerView>("造成攻击伤害触发器");
        AttackDamageTrigger.Register("默认基础互伤", HandleDefaultAttackDamageAsync, DefaultDamagePriority);

        // 词条装载上下文（卡牌加载时取用；伏击注册「造成攻击伤害」改写）
        KeywordLoadContext = new KeywordLoadContext(engine, AttackDamageTrigger);

        // 守护维护：由位置/入场/死亡更新驱动（凡影响占用布局的变动均等效触达；词条效果不直接发射更新）
        _engine.Subscribe((updateType, _, _) =>
        {
            if (updateType == GameUpdates.UnitPositionChanged
                || updateType == GameUpdates.UnitJoined
                || updateType == GameUpdates.UnitDeployed
                || updateType == GameUpdates.CardDied)
            {
                MaintainGuardState();
            }

            return Task.CompletedTask;
        });

        // G3 修饰机制（W2b 加性）：防御归零的统一死亡衔接——被动触发器（挂载更新总线、响应 card.stat.changed）：
        // 检查在「外部订阅通知完成之后」（总线触发器阶段）执行——「先数值变化、后死亡信号」：
        // card.stat.changed（致死结算段、数值落定）→ 死亡清理 → card.died（清理就绪后、恰一次）。
        _defenseDepletionTrigger = new Trigger<CardTriggerView>(
            "防御归零检查触发器",
            TriggerKind.Passive,
            events: new[] { new TriggerEvent<CardTriggerView>("防御归零检查", HandleDefenseDepletionAsync) },
            hooks: new[] { GameUpdates.CardStatChanged });
        _engine.Bus.Mount(_defenseDepletionTrigger);

        // W3-3（G11 HQ 实体化）：HQ 归零的统一响应——被动触发器（挂载更新总线、响应 card.stat.changed）：
        // 「终局判定迁至 HQ」的触发侧——与防御归零检查同构（检查在「外部订阅通知完成之后」〔总线触发器阶段〕执行；
        // 「先数值变化（通用数据改变更新）、后终局记录」观察序）；任何来源（攻击伤害/效果/修饰撤销等）使 HQ 血量 ≤0
        // 均经同一判定：执行主体＝HQ 侧统一响应（RespondToZero——不内联于攻击流程）。
        _hqZeroTrigger = new Trigger<CardTriggerView>(
            "HQ 归零检查触发器",
            TriggerKind.Passive,
            events: new[] { new TriggerEvent<CardTriggerView>("HQ 归零检查", HandleHqZeroCheckAsync) },
            hooks: new[] { GameUpdates.CardStatChanged });
        _engine.Bus.Mount(_hqZeroTrigger);

        // W3-3：终局响应装配注入（延迟读取）——HQ 随玩家创建，对局服务在此绑定：
        // 胜者＝HQ 归零方之对手；脱局场景（本对象独立构造、无生命周期提供）＝HQ 侧防御降级（归零不记录、不抛错）。
        foreach (var player in players)
        {
            player.Hq.ConfigureTerminalResponse(() => EnemyOf(player), () => _lifecycle);
        }
    }

    // ---------- 内置流程触发器（公开只读；对局装配期注册为底层触发器） ----------

    /// <summary>指挥触发器（内置；一次指挥流程的编排承载——读判定 → 交互 → 分派 → 嵌套执行 → 外层收尾）。</summary>
    public Trigger<CommandTriggerView> CommandTrigger { get; }

    /// <summary>单位移动触发器（内置；执行段＝槽位变更＋发射 unit.position.changed；复验＝执行前兜底）。</summary>
    public Trigger<UnitMoveTriggerView> UnitMoveTrigger { get; }

    /// <summary>单位攻击触发器（内置；执行段＝HQ 简路〔伤害经 HQ 数值路径〕/ 单位互伤经「造成攻击伤害」）。</summary>
    public Trigger<UnitAttackTriggerView> UnitAttackTrigger { get; }

    /// <summary>「造成攻击伤害」共享流程触发器（内置；单位 vs 单位攻击结算必经；伏击挂载点）。</summary>
    public Trigger<AttackDamageTriggerView> AttackDamageTrigger { get; }

    /// <summary>词条装载上下文（卡牌加载时取用；经对局装配注入卡牌库）。</summary>
    public KeywordLoadContext KeywordLoadContext { get; }

    /// <summary>防御归零检查触发器（内置；被动——挂载更新总线、响应 card.stat.changed；公开只读）。
    /// 用途：内置事件（「防御归零检查」）的寻址面——注册项句柄经 <see cref="Trigger{TView}.InitialRegistrations"/> 供给（moding（逻辑替换）等场景）。</summary>
    public Trigger<CardTriggerView> DefenseDepletionTrigger => _defenseDepletionTrigger;

    /// <summary>HQ 归零检查触发器（内置；被动——挂载更新总线、响应 card.stat.changed；公开只读）。
    /// 用途：内置事件（「HQ 归零检查」）的寻址面——注册项句柄经 <see cref="Trigger{TView}.InitialRegistrations"/> 供给（moding（逻辑替换）等场景）。</summary>
    public Trigger<CardTriggerView> HqZeroTrigger => _hqZeroTrigger;

    // ---------- ① 指挥入口（一次拖拽＝一次调用链、单一公开入口方法） ----------

    /// <summary>
    /// 指挥单位（一次调用链：发起判定 → 指挥触发器〔交互 → 分派 → 嵌套执行 → 收尾〕→ 结果）：
    /// 输入＝被拖动单位引用（行动方从单位归属与对局状态推导——无需额外上下文参数）。
    /// 发起拒绝（非己方回合 / 归属无效 / 已死亡 / 两动作均不可用）＝不进入交互、零副作用、不发射更新；
    /// 取消＝零副作用；成功＝完整执行（含以攻击者死亡告终的攻击结算——照常扣费、照常清位）。
    /// X1 加性：<paramref name="triggerCard"/>＝触发者卡牌（效果引发情形——引发本次操作的效果宿主卡引用；
    /// 显式携带、缺省＝null＝玩家主动操作）；携带后随指挥链透传（含嵌套的移动/攻击与伤害调用）。
    /// 装配性错误（未单位化）＝明确异常（fail-fast）。
    /// </summary>
    /// <exception cref="ArgumentNullException">unit 为 null。</exception>
    /// <exception cref="InvalidOperationException">单位尚未单位化（装配性错误、fail-fast）。</exception>
    public async Task<CommandResult> BeginCommandAsync(
        UnitCard unit, CancellationToken ct = default, Ref<Entity>? triggerCard = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        RequireUnitized(unit);

        // 终局门禁（后置项 B）：对局已结束＝动作入口拒绝（零副作用、状态不推进；只读查询面不经此入口）
        if (_lifecycle?.IsEnded == true)
        {
            return CommandResult.Failure(CommandFailureReason.GameEnded);
        }

        // 发起判定（纯查询、与流程内部同源）
        var availability = GetCommandAvailability(unit);
        if (availability.IneligibleReason is { } ineligible)
        {
            return CommandResult.Failure(MapIneligible(ineligible));
        }

        if (!availability.AnyActionAvailable)
        {
            return CommandResult.Failure(CommandFailureReason.NoActionAvailable);
        }

        var box = new CommandFlowBox();
        var data = new Dictionary<string, object?>
        {
            [CommandDataKeys.Card] = unit.Ref,
            [CommandDataKeys.Player] = unit.Owner,
            [CommandDataKeys.FlowBox] = box,
        };
        if (triggerCard is not null)
        {
            data[CommandDataKeys.TriggerCard] = triggerCard; // X1：效果引发情形显式携带（玩家主动操作＝缺省空）
        }

        await CommandTrigger.InvokeAsync(_engine, data, ct);

        return box.Result ?? CommandResult.Failure(CommandFailureReason.CommandFlowFault);
    }

    // ---------- ② 动作可用性聚合判定（公开面；纯查询、与流程内部同源） ----------

    /// <summary>
    /// 动作可用性聚合判定（纯查询——无副作用、不发更新、不启动交互、不改变任何状态）：
    /// 流程级资格（己方回合 ∧ Owner＝当前行动方 ∧ 在场未死亡）＋ 动作级（bool ∧ 费用 ∧ 候选可行性——一次聚合）。
    /// 移动候选＝前线任意空槽引用；攻击候选＝合法敌方单位/敌方 HQ 引用（范围矩阵 ∧ 烟幕 ∧ 守护筛选后）。
    /// 矩阵断言可直接经本公开面进行（不必驱动完整拖拽交互）。
    /// </summary>
    /// <exception cref="ArgumentNullException">unit 为 null。</exception>
    /// <exception cref="InvalidOperationException">单位尚未单位化（装配性错误、fail-fast）。</exception>
    public CommandAvailability GetCommandAvailability(UnitCard unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        RequireUnitized(unit);

        var ineligible = ComputeIneligibility(unit);
        return new CommandAvailability(
            ineligible,
            ComputeMoveAvailability(unit, ineligible),
            ComputeAttackAvailability(unit, ineligible));
    }

    private CommandBlockReason? ComputeIneligibility(UnitCard unit)
    {
        var current = _currentPlayerProvider();
        if (current is null)
        {
            return CommandBlockReason.NonOwnerTurn; // 回合未就绪（当前行动方未定）
        }

        var owner = unit.Owner;
        if (owner is null)
        {
            return CommandBlockReason.OwnerInvalid; // 无归属单位不可被指挥
        }

        if (!ReferenceEquals(owner, current))
        {
            return CommandBlockReason.NonOwnerTurn; // 仅限己方回合操作己方单位
        }

        if (unit.GetData<UnitStateData>().IsDestroyed)
        {
            return CommandBlockReason.UnitDead; // 尸体不可被指挥
        }

        return null;
    }

    private CommandActionAvailability ComputeMoveAvailability(UnitCard unit, CommandBlockReason? ineligible)
    {
        if (ineligible is { } blocked)
        {
            return CommandActionAvailability.Blocked(blocked);
        }

        var owner = unit.Owner!;
        var state = unit.GetData<UnitStateData>();
        var command = unit.GetData<CommandData>();

        if (!command.CanMove)
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.FlagFalse);
        }

        // W2b：行动费读「有效值」（修饰贡献叠加后的缓存有效值——读取面统一、防旁路直读）
        if (owner.Points < unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost))
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.PointShortage);
        }

        // 仅推进：仅支援线单位可移动（前线单位无移动候选）
        if (state.Position is null || !_battlefield.GetSupportLine(owner).Contains(state.Position))
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.NoCandidates);
        }

        // 推进前置（后置项 C）：前线不存在存活敌方单位方可推进（空前线或己方已占；敌方清空后实时恢复）
        if (HasLivingEnemyOnFrontLine(owner))
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.NoCandidates);
        }

        // 移动候选＝前线任意空槽（不被邻位动态规则约束；无后撤/横移候选）
        var candidates = new List<Ref<Entity>>();
        foreach (var slot in _battlefield.FrontLine)
        {
            if (slot.IsEmpty)
            {
                candidates.Add(slot.Ref);
            }
        }

        return candidates.Count == 0
            ? CommandActionAvailability.Blocked(CommandBlockReason.NoCandidates)
            : CommandActionAvailability.Available(candidates);
    }

    private CommandActionAvailability ComputeAttackAvailability(UnitCard unit, CommandBlockReason? ineligible)
    {
        if (ineligible is { } blocked)
        {
            return CommandActionAvailability.Blocked(blocked);
        }

        var owner = unit.Owner!;
        var command = unit.GetData<CommandData>();

        if (!command.CanAttack)
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.FlagFalse);
        }

        // W2b：行动费读「有效值」（修饰贡献叠加后的缓存有效值——读取面统一、防旁路直读）
        if (owner.Points < unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost))
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.PointShortage);
        }

        var candidates = CollectAttackCandidates(unit);
        return candidates.Count == 0
            ? CommandActionAvailability.Blocked(CommandBlockReason.NoCandidates)
            : CommandActionAvailability.Available(candidates);
    }

    /// <summary>
    /// 攻击候选收集（合法敌方单位/敌方 HQ 引用；索引序＝敌支援线 → 前线 → 敌 HQ）：
    /// 范围矩阵（步/坦仅相邻；炮/战斗机/轰炸机任意）∧ 烟幕（不可被攻击）∧ 守护（被守护仅能被炮/轰）∧ 目标归属。
    /// </summary>
    private List<Ref<Entity>> CollectAttackCandidates(UnitCard attacker)
    {
        var result = new List<Ref<Entity>>();
        var owner = attacker.Owner;
        if (owner is null)
        {
            return result;
        }

        var enemy = EnemyOf(owner);
        if (enemy is null)
        {
            return result;
        }

        var enemyLine = _battlefield.GetSupportLine(enemy);
        for (var i = 0; i < enemyLine.Count; i++)
        {
            if (enemyLine[i].Occupant is UnitCard target && IsLegalUnitTarget(attacker, target))
            {
                result.Add(target.Ref);
            }
        }

        var frontLine = _battlefield.FrontLine;
        for (var i = 0; i < frontLine.Count; i++)
        {
            if (frontLine[i].Occupant is UnitCard target && IsLegalUnitTarget(attacker, target))
            {
                result.Add(target.Ref);
            }
        }

        // HQ（W3-3 实体化）：目标以 HQ 实体引用承载——候选产出 hq.Ref（不留双承载：槽位引用不再作为
        // HQ 目标产出；槽位关系仅用于布局语义判定〔守护/轰炸机拦截/范围矩阵——经 HQ 占位槽〕）。
        if (enemyLine[0].Occupant is Hq hq && IsLegalHqTarget(attacker, hq))
        {
            result.Add(hq.Ref);
        }

        return result;
    }

    /// <summary>单位为目标的合法性（归属正确 ∧ 在场未死 ∧ 不被烟幕 ∧ 被守护仅炮/轰可攻 ∧ 范围矩阵）。</summary>
    private bool IsLegalUnitTarget(UnitCard attacker, UnitCard target)
    {
        var enemy = attacker.Owner is { } owner ? EnemyOf(owner) : null;
        if (enemy is null || !ReferenceEquals(target.Owner, enemy))
        {
            return false;
        }

        if (!target.TryGetData<UnitStateData>(out var targetState)
            || targetState.IsDestroyed
            || targetState.Position is null)
        {
            return false;
        }

        if (KeywordRules.HasKeyword(target, KeywordIds.SmokeScreen))
        {
            return false; // 烟幕：不可被攻击（对一切攻击者生效）
        }

        if (_guardedUnits.Contains(target) && !CountsAsBombard(attacker))
        {
            return false; // 被守护：仅能被炮/轰攻击
        }

        // 轰炸机拦截（后置项 C）：目标所在战线存在存活敌方战斗机时，该战线的非战斗机目标置黑
        var targetPosition = targetState.Position!;
        if (IsBlockedByEnemyFighter(attacker, targetPosition, HasUnitType(target, UnitType.Fighter)))
        {
            return false;
        }

        return IsLineInRange(attacker, targetPosition);
    }

    /// <summary>HQ 为目标的合法性（仍为敌方 HQ〔引用同一性〕∧ 被守护仅炮/轰可攻 ∧ 范围矩阵；HQ 无死亡/烟幕语义）。
    /// W3-3：HQ 实体引用承载——布局判定（守护/轰炸机拦截/范围矩阵）经 HQ 占位槽（布局语义基准）。</summary>
    private bool IsLegalHqTarget(UnitCard attacker, Hq hq)
    {
        var enemy = attacker.Owner is { } owner ? EnemyOf(owner) : null;
        if (enemy is null || !ReferenceEquals(hq.Owner, enemy))
        {
            return false;
        }

        if (hq.Position is not { } hqSlot)
        {
            return false; // 异常布局（防御：HQ 未入槽）
        }

        if (_guardedHqs.Contains(hq) && !CountsAsBombard(attacker))
        {
            return false; // HQ 被守护（相邻守护者）：仅能被炮/轰攻击
        }

        // 轰炸机拦截（后置项 C）：HQ 位于敌方支援线——该战线存在存活敌方战斗机时不可选（须先攻击战斗机）
        if (IsBlockedByEnemyFighter(attacker, hqSlot, targetIsFighter: false))
        {
            return false;
        }

        return IsLineInRange(attacker, hqSlot);
    }

    /// <summary>
    /// 范围矩阵（目标线判定）：炮/战斗机/轰炸机任一＝任意线全组合允许（含 HQ、含同线）；
    /// 其余（步兵/坦克/零类型）＝仅相邻（跨线紧邻）：支援线→敌前线单位；前线→敌支援线单位/HQ；同线不允许。
    /// </summary>
    private bool IsLineInRange(UnitCard attacker, Slot targetSlot)
    {
        var attackerState = attacker.GetData<UnitStateData>();
        var owner = attacker.Owner;
        if (owner is null || attackerState.Position is null)
        {
            return false;
        }

        var enemy = EnemyOf(owner);
        if (enemy is null)
        {
            return false;
        }

        if (HasAnyLineRange(attacker))
        {
            return true;
        }

        if (_battlefield.GetSupportLine(owner).Contains(attackerState.Position))
        {
            return _battlefield.FrontLine.Contains(targetSlot); // 支援线 → 仅敌方前线单位
        }

        if (_battlefield.FrontLine.Contains(attackerState.Position))
        {
            return _battlefield.GetSupportLine(enemy).Contains(targetSlot); // 前线 → 仅敌方支援线（单位/HQ）
        }

        return false; // 异常布局（防御）
    }

    // ---------- ③ 守护状态（维护型；由更新驱动重算） ----------

    /// <summary>
    /// 被守护状态查询（单位）：相邻（同线槽位索引差 1）存在同方存活守护者 ＝ 被守护
    /// （仅能被炮/轰攻击；守护者自身永不获被守护；多守护源不叠加＝存在性判定）。
    /// 读取形式＝维护型状态查询（由位置/入场/死亡更新驱动维护）。
    /// </summary>
    /// <exception cref="ArgumentNullException">unit 为 null。</exception>
    public bool IsUnitGuarded(UnitCard unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return _guardedUnits.Contains(unit);
    }

    /// <summary>被守护状态查询（HQ；W3-3：HQ 实体承载——查询以 HQ 实体为单元）。</summary>
    /// <exception cref="ArgumentNullException">hq 为 null。</exception>
    public bool IsHqGuarded(Hq hq)
    {
        ArgumentNullException.ThrowIfNull(hq);
        return _guardedHqs.Contains(hq);
    }

    /// <summary>被守护状态查询（HQ；按玩家转发——转发读面，读 player.Hq 的守护状态）。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public bool IsHqGuarded(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return IsHqGuarded(player.Hq);
    }

    /// <summary>
    /// 被守护状态重算（全量；存在性判定——「至少一个相邻守护者」）：
    /// 逐线（双方支援线＋前线）对每个在场单位检查物理邻位；HQ 检查同支援线槽 1；
    /// 守护者存活状态计入（死亡即不构成保护来源）；守护者自身跳过（永不获被守护）；同方限定。
    /// </summary>
    private void MaintainGuardState()
    {
        _guardedUnits.Clear();
        _guardedHqs.Clear();

        MaintainGuardForLine(_battlefield.PlayerASupportLine);
        MaintainGuardForLine(_battlefield.FrontLine);
        MaintainGuardForLine(_battlefield.PlayerBSupportLine);

        MaintainHqGuard(_battlefield.PlayerASupportLine);
        MaintainHqGuard(_battlefield.PlayerBSupportLine);
    }

    private void MaintainGuardForLine(BattleLine line)
    {
        for (var i = 0; i < line.Count; i++)
        {
            if (line[i].Occupant is not UnitCard unit || IsDead(unit))
            {
                continue;
            }

            if (unit.Definition.IsGuard)
            {
                continue; // 守护者自身永不获被守护（任何情形，含相邻另一守护者）
            }

            if (unit.Owner is not { } owner)
            {
                continue; // 无归属单位不获（同方限定不成立）
            }

            if (IsGuardianAt(line, i - 1, owner) || IsGuardianAt(line, i + 1, owner))
            {
                _guardedUnits.Add(unit);
            }
        }
    }

    private void MaintainHqGuard(BattleLine supportLine)
    {
        if (supportLine[0].Occupant is not Hq hq)
        {
            return;
        }

        if (IsGuardianAt(supportLine, 1, hq.Owner))
        {
            _guardedHqs.Add(hq); // HQ 计入保护（守护者在槽 1 → HQ 获被守护）
        }
    }

    private static bool IsGuardianAt(BattleLine line, int index, Player owner)
    {
        if (index < 0 || index >= line.Count)
        {
            return false;
        }

        return line[index].Occupant is UnitCard guardian
            && guardian.Definition.IsGuard
            && !IsDead(guardian)
            && ReferenceEquals(guardian.Owner, owner);
    }

    // ---------- ④ 回合恢复（turn.start 处理段接线：行动方在场单位重置两 bool） ----------

    /// <summary>
    /// 单位行动状态恢复（回合开始处理段调用；turn.start.after 前完成）：
    /// 对当前行动方在场单位（HQ 除外）重置两 bool＝true ＋ 词条运行态清零（「本轮已攻次数」随回合恢复一并清零——词条侧）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public void RefreshActionStates(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        RefreshLine(_battlefield.PlayerASupportLine, player);
        RefreshLine(_battlefield.FrontLine, player);
        RefreshLine(_battlefield.PlayerBSupportLine, player);
    }

    private static void RefreshLine(BattleLine line, Player player)
    {
        for (var i = 0; i < line.Count; i++)
        {
            if (line[i].Occupant is not UnitCard unit || IsDead(unit))
            {
                continue;
            }

            if (!ReferenceEquals(unit.Owner, player))
            {
                continue;
            }

            var command = unit.GetData<CommandData>();
            command.CanMove = true;
            command.CanAttack = true;
            KeywordRules.ResetTurnCounters(unit);
        }
    }

    // ---------- ⑤ 指挥流程（指挥触发器默认链） ----------

    private async Task HandleCommandFlowAsync(CommandTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not { IsAlive: true } unitRef || unitRef.Value is not UnitCard unit
            || view.FlowBox is not CommandFlowBox box)
        {
            ctx.Interrupt(); // 载荷缺失（结构性错误）：链中止、结局不发生
            return;
        }

        // 读判定（复算；同源）
        var availability = GetCommandAvailability(unit);
        if (availability.IneligibleReason is { } ineligible)
        {
            box.Result = CommandResult.Failure(MapIneligible(ineligible));
            return;
        }

        if (!availability.AnyActionAvailable)
        {
            box.Result = CommandResult.Failure(CommandFailureReason.NoActionAvailable);
            return;
        }

        // 交互：一次拖拽＝一次请求；候选＝可用动作的候选并集（候选面之外＝后端筛除、不构成确认）
        var candidates = new List<Ref<Entity>>(availability.Move.Candidates.Count + availability.Attack.Candidates.Count);
        candidates.AddRange(availability.Move.Candidates);
        candidates.AddRange(availability.Attack.Candidates);
        var allowedSet = new HashSet<Ref<Entity>>(candidates);
        var filter = new TargetFilter(coarseFilter: refs => refs.Where(allowedSet.Contains).ToList());
        var targeter = _targeterManager.CreateTargeter(filter, new TargetSlot[] { new SingleSelectSlot() });
        var targeting = await targeter.Targeting();

        if (targeting.Status == TargetingStatus.Cancelled)
        {
            box.Result = CommandResult.Cancelled(targeting); // 取消＝零副作用
            return;
        }

        if (targeting.Status != TargetingStatus.Success)
        {
            box.Result = CommandResult.Failure(CommandFailureReason.TargetingFailed, targeting);
            return;
        }

        var selected = targeting.Outcome!.Single;
        if (selected is null || !selected.IsAlive)
        {
            box.Result = CommandResult.Failure(CommandFailureReason.TargetingFailed);
            return;
        }

        // 分派：选中空槽 → 移动触发器；选中敌方单位 / 敌方 HQ → 攻击触发器（X1：触发者随指挥链透传）
        await DispatchSelectedAsync(unit, selected, box, ctx, ct, view.TriggerCard);
    }

    private async Task DispatchSelectedAsync(
        UnitCard unit, Ref<Entity> selected, CommandFlowBox box, Context ctx,
        CancellationToken ct, Ref<Entity>? triggerCard)
    {
        var owner = unit.Owner;
        var enemy = owner is null ? null : EnemyOf(owner);
        var selectedEntity = selected.Value;

        // 分派：选中敌方 HQ 实体引用 / 敌方单位 → 攻击触发器；选中空槽 → 移动触发器
        // （X1：触发者随指挥链透传）。W3-3：HQ 目标以实体引用承载——不再有「槽位→HQ」分派
        // （槽位引用不再作为 HQ 目标产出；HQ 占位槽引用落入无效目标分派）。
        if (selectedEntity is Hq hq)
        {
            if (enemy is not null && ReferenceEquals(hq.Owner, enemy))
            {
                await DispatchAttackAsync(unit, selected, box, ctx, ct, triggerCard);
                return;
            }
        }
        else if (selectedEntity is UnitCard)
        {
            await DispatchAttackAsync(unit, selected, box, ctx, ct, triggerCard);
            return;
        }
        else if (selectedEntity is Slot slot && slot.IsEmpty)
        {
            await DispatchMoveAsync(unit, selected, box, ctx, ct, triggerCard);
            return;
        }

        // 无效目标分派（防御；正常流程不可达——候选面之外者不产生终局）
        box.Result = CommandResult.Failure(CommandFailureReason.TargetingFailed);
    }

    private async Task DispatchMoveAsync(
        UnitCard unit, Ref<Entity> newPositionRef, CommandFlowBox box, Context ctx,
        CancellationToken ct, Ref<Entity>? triggerCard)
    {
        var state = unit.GetData<UnitStateData>();
        var oldSlot = state.Position;
        if (oldSlot is null)
        {
            box.Result = CommandResult.Failure(CommandFailureReason.CommandFlowFault);
            return;
        }

        // 嵌套执行（复验＝移动触发器验证承载——拒绝＝仅本次取消、零副作用＋留痕）
        var data = new Dictionary<string, object?>
        {
            [CommandDataKeys.Unit] = unit.Ref,
            [CommandDataKeys.OldPosition] = oldSlot.Ref,
            [CommandDataKeys.NewPosition] = newPositionRef,
        };
        if (triggerCard is not null)
        {
            data[CommandDataKeys.TriggerCard] = triggerCard; // X1：效果引发情形随指挥链透传（主动指挥＝缺省空）
        }

        var stream = await UnitMoveTrigger.InvokeAsync(_engine, data, ct);

        if (ctx.Interrupted || stream.Outcome != ExecutionOutcome.Normal)
        {
            box.Result = stream.Outcome == ExecutionOutcome.ValidationRejected
                ? CommandResult.Failure(CommandFailureReason.ExecutionRejected)
                : CommandResult.Failure(CommandFailureReason.CommandFlowFault);
            return;
        }

        // 外层收尾（执行成功后）：扣费 → 两 bool 更新（恰一次、只在外层）
        FinalizeMove(unit);
        box.Result = CommandResult.Success();
    }

    private async Task DispatchAttackAsync(
        UnitCard unit, Ref<Entity> targetRef, CommandFlowBox box, Context ctx,
        CancellationToken ct, Ref<Entity>? triggerCard)
    {
        // 嵌套执行（复验＝攻击触发器验证承载——拒绝＝仅本次取消、零副作用＋留痕）
        var data = new Dictionary<string, object?>
        {
            [CommandDataKeys.Attacker] = unit.Ref,
            [CommandDataKeys.Target] = targetRef,
        };
        if (triggerCard is not null)
        {
            data[CommandDataKeys.TriggerCard] = triggerCard; // X1：效果引发情形随指挥链透传（主动指挥＝缺省空）
        }

        var stream = await UnitAttackTrigger.InvokeAsync(_engine, data, ct);

        if (ctx.Interrupted || stream.Outcome != ExecutionOutcome.Normal)
        {
            box.Result = stream.Outcome == ExecutionOutcome.ValidationRejected
                ? CommandResult.Failure(CommandFailureReason.ExecutionRejected)
                : CommandResult.Failure(CommandFailureReason.CommandFlowFault);
            return;
        }

        // 外层收尾（执行成功后；攻击者死亡不豁免）：扣费 → 两 bool 更新（恰一次、只在外层）
        FinalizeAttack(unit);
        box.Result = CommandResult.Success();
    }

    /// <summary>移动收尾（外层）：扣费（行动方扣除、恰一次；W2b：读行动费有效值）→ 两 bool 更新（非坦克＝二选一：另一动作同被清）。</summary>
    private static void FinalizeMove(UnitCard unit)
    {
        var owner = unit.Owner!;
        var command = unit.GetData<CommandData>();

        owner.Points -= unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost);
        command.CanMove = false;
        if (!HasUnitType(unit, UnitType.Tank))
        {
            command.CanAttack = false;
        }
    }

    /// <summary>攻击收尾（外层）：扣费（行动方扣除、恰一次；W2b：读行动费有效值）→ CanAttack 更新（奋战读取记账）→ 非坦克二选一清 CanMove。</summary>
    private static void FinalizeAttack(UnitCard unit)
    {
        var owner = unit.Owner!;
        var command = unit.GetData<CommandData>();

        owner.Points -= unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost);
        command.CanAttack = KeywordRules.ResolveCanAttackAfterAttack(unit);
        if (!HasUnitType(unit, UnitType.Tank))
        {
            command.CanMove = false;
        }
    }

    // ---------- ⑥ 单位移动触发器默认链（执行段） ----------

    private async Task HandleUnitMoveAsync(UnitMoveTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Unit is not { IsAlive: true } unitRef || unitRef.Value is not UnitCard unit
            || view.OldPosition is not { IsAlive: true } oldRef || oldRef.Value is not Slot oldSlot
            || view.NewPosition is not { IsAlive: true } newRef || newRef.Value is not Slot newSlot)
        {
            ctx.Interrupt(); // 载荷缺失（结构性错误）：链中止、移动不发生
            return;
        }

        // 执行段：槽位变更（旧槽移除 → 放入目标空槽；原槽变为空槽、参与后续邻位计算）→ 发射更新（恰一次；先于收尾段）
        var state = unit.GetData<UnitStateData>();
        oldSlot.Clear();
        newSlot.Place(unit);
        state.Position = newSlot;
        await GameUpdates.EmitUnitPositionChanged(_engine, unit, oldSlot, newSlot, ct);
        // 守护维护由本更新驱动（订阅重算——布局已变更、收尾未发生）
    }

    // ---------- ⑦ 单位攻击触发器默认链（执行段） ----------

    private async Task HandleUnitAttackAsync(UnitAttackTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Attacker is not { IsAlive: true } attackerRef || attackerRef.Value is not UnitCard attacker
            || view.Target is not { IsAlive: true } targetRef)
        {
            ctx.Interrupt();
            return;
        }

        var targetEntity = targetRef.Value;
        if (targetEntity is Hq hq && !ReferenceEquals(hq.Owner, attacker.Owner))
        {
            // HQ 简路（W3-3 实体化）：伤害＝攻击者攻击力有效值；HQ 不反击；不走「造成攻击伤害」（无单位互伤链）；
            // 伤害数值改走 HQ 数值路径与管线（改写→介入→应用→跑链集中触发——不建专用 HQ 伤害流程）。
            // 归零→终局：不内联于攻击流程——HQ 侧统一响应（数值变化下游；与「先数值变化、后终局记录」
            // 观察序一致）。当次结算收尾照常完成（内部步骤不经门禁）。
            var damage = attacker.Modifiers.GetEffectiveValue(CardStatFields.Attack);
            await hq.ApplyDamageAsync(damage, ct);
        }
        else if (targetEntity is UnitCard)
        {
            // 单位 vs 单位：必经「造成攻击伤害」（伏击挂载点；同一目标承载）
            // X1：触发者随链路传递（同一效果链内一致——攻击链读数透传至伤害链；主动指挥＝空、照常透传）
            var damageData = new Dictionary<string, object?>
            {
                [CommandDataKeys.Attacker] = attackerRef,
                [CommandDataKeys.Target] = targetRef,
                [CommandDataKeys.Resolution] = new AttackDamageResolution(),
            };
            if (view.TriggerCard is not null)
            {
                damageData[CommandDataKeys.TriggerCard] = view.TriggerCard;
            }

            await AttackDamageTrigger.InvokeAsync(_engine, damageData, ct);
        }
        else
        {
            ctx.Interrupt();
            return;
        }

        // 执行段记账（词条侧）：奋战——本轮已攻次数 +1（先执行（含记账）→ 后更新）
        KeywordRules.RecordAttack(attacker);
    }

    // ---------- ⑧ 「造成攻击伤害」默认链（基础互伤；伏击改写的替代面） ----------

    private async Task HandleDefaultAttackDamageAsync(AttackDamageTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Attacker is not { IsAlive: true } attackerRef || attackerRef.Value is not UnitCard attacker
            || view.Target is not { IsAlive: true } targetRef || targetRef.Value is not UnitCard target
            || view.Resolution is not AttackDamageResolution resolution)
        {
            ctx.Interrupt(); // 载荷缺失（HQ 不走本流程——防御）
            return;
        }

        // 死亡冻结（W2b）：任一方已死亡＝尸体不参与数值结算（对已死单位的伤害门户亦将明确拒绝——此处为流程侧防护；
        // 真实对局不可达〔候选过滤〕——防御于测试/异常驱动路径）。
        if (attacker.GetData<UnitStateData>().IsDestroyed || target.GetData<UnitStateData>().IsDestroyed)
        {
            return;
        }

        if (resolution.IsRewritten)
        {
            // 伏击改写（已判定成立）：替代默认——攻击者死亡（统一死亡流程、无 HP 逐步扣减语义）、被攻击者不受伤
            await ProcessDeathAsync(attacker, ct);
            return;
        }

        // 默认基础互伤：按反击豁免判定表（后置项 A）——同时结算、以互扣前有效值为基准；豁免方不结算反击伤害
        // （判定表：目标轰炸机永不反击 / 攻击者炮兵不受反击 / 攻击者轰炸机不受反击〔目标战斗机例外〕/ 其余正常）
        // W2b：伤害值与扣减一律接改——伤害读「攻击力有效值」；扣减经门户（损伤量→跑链→有变更集中触发）。
        var damageToTarget = attacker.Modifiers.GetEffectiveValue(CardStatFields.Attack);
        var damageToAttacker = target.Modifiers.GetEffectiveValue(CardStatFields.Attack);
        var counterAttacks = CounterAttackRules.CanCounterAttack(attacker, target);

        await target.ApplyDefenseDamageAsync(damageToTarget, ct); // 门户：伤害＝即时变更（只扣当前、不减上限）
        if (counterAttacks)
        {
            await attacker.ApplyDefenseDamageAsync(damageToAttacker, ct);
        }

        // 死亡判定（互扣后防御有效值 ≤0；数值表现钳制后 ≤0 即 ==0）；同归于尽＝两枚 card.died 均发射，顺序：被攻击者在前、攻击者在后。
        // 防御归零的统一死亡衔接由 stat.changed 订阅先行处理（含修饰撤销/到期等非伤害来源）；此处判定＝结算路径内防护兜底——
        // 已死亡＝跳过（保证死亡恰一次、发射顺序稳定）。
        if (!target.GetData<UnitStateData>().IsDestroyed
            && target.Modifiers.GetEffectiveValue(CardStatFields.Defense) <= 0)
        {
            await ProcessDeathAsync(target, ct);
        }

        if (counterAttacks && !attacker.GetData<UnitStateData>().IsDestroyed
            && attacker.Modifiers.GetEffectiveValue(CardStatFields.Defense) <= 0)
        {
            await ProcessDeathAsync(attacker, ct);
        }
    }

    /// <summary>
    /// 统一死亡流程（攻击结算判定死亡后调用；伏击改写的攻击者死亡同走此流程）。
    /// 清理最小口径：①槽位释放（变空槽）→ ②IsDestroyed 置位 → ③词条效果注销 ＋ 修饰器清理
    /// （W2b：含期限订阅随销；数值整合至最终态、零新发射——死亡清理为内部特殊路径）＋ 效果卸载
    /// （X2：统一卸载链收口——容器移除＋OnUnmount＋撤销登记＋总线卸载；托管清理幂等）→
    /// ④UnitStateData.Position 置空（实例保留可查询）→ ⑤card.died 发射（恰一次；死亡状态就绪后）。
    /// 不发 unit.position.changed（该更新专属移动语义）。
    /// </summary>
    private async Task ProcessDeathAsync(UnitCard unit, CancellationToken ct)
    {
        var state = unit.GetData<UnitStateData>();

        state.Position?.Clear();
        state.IsDestroyed = true;
        if (unit.TryGetData<KeywordLogicData>(out var keywordLogics))
        {
            keywordLogics.UnmountAll();
        }

        await unit.Modifiers.ClearAllForDeathAsync(ct); // W2b：注销全部修饰器（含期限订阅随销）＋数值整合至最终态（零新发射）

        // X2：效果卸载（统一卸载链收口——容器移除＋OnUnmount＋撤销登记＋总线卸载；含托管清理〔幂等：
        // 修饰器已清、零操作〕）——清理就绪后方发 card.died（「先数值变化→清理→card.died」观察序保持）。
        foreach (var effect in unit.Effects.ToArray())
        {
            unit.RemoveEffect(effect);
        }

        state.Position = null;
        await GameUpdates.EmitCardDied(_engine, unit, ct);
        // 守护维护由本更新驱动（清位后等效触达——被守护状态可观测变化）
    }

    /// <summary>
    /// 防御归零的统一死亡衔接（W2b；card.stat.changed 更新驱动的被动检查——总线触发器阶段）：
    /// 任一单位的防御有效值经数值变更（伤害/修饰/撤销/到期）降至 ≤0（数值表现钳制后 ≤0 即 ==0）且尚未死亡
    /// → 走既有统一死亡流程（无攻击者归因）。执行时机＝card.stat.changed 的外部订阅通知完成之后——
    /// 「先数值变化、后死亡信号」（card.stat.changed → 死亡清理 → card.died）。
    /// 已死亡/已毁＝跳过（幂等防护——死亡恰一次）；非单位卡（无单位数据组件）＝跳过。
    /// </summary>
    private async Task HandleDefenseDepletionAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not UnitCard unit
            || !unit.TryGetData<UnitStateData>(out var state)
            || state.IsDestroyed)
        {
            return;
        }

        if (unit.Modifiers.GetEffectiveValue(CardStatFields.Defense) <= 0)
        {
            await ProcessDeathAsync(unit, ct);
        }
    }

    /// <summary>
    /// HQ 归零的统一响应（W3-3 G11；card.stat.changed 更新驱动的被动检查——总线触发器阶段）：
    /// 任一 HQ 的有效血量经数值变更（伤害/修饰/撤销等）降至 ≤0 → 执行 HQ 侧统一响应
    /// （终局记录：状态置结束＋胜者＝HQ 归零方之对手——「终局判定迁至 HQ」的落点：判定为 HQ 侧响应，
    /// 不内联于攻击流程）。执行时机＝card.stat.changed 的外部订阅通知完成之后——
    /// 「先数值变化（通用数据改变更新）、后终局记录」。非 HQ 卡＝跳过；有效值 &gt;0＝无操作；
    /// 生命周期侧重复调用幂等（End 自身幂等）。
    /// </summary>
    private Task HandleHqZeroCheckAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is Hq hq)
        {
            hq.RespondToZero(); // HQ 侧统一响应（≤0 才生效；内部幂等；脱局防御降级在 HQ 侧）
        }

        return Task.CompletedTask;
    }

    // ---------- ⑨ 执行前复验（分派后、执行前兜底；验证机制承载——ValidationRejected、零副作用＋留痕） ----------

    private bool RevalidateMove(IReadOnlyList<Ref<Entity>> refs)
    {
        // X1：refs＝触发数据第一层引用收集（插入序）——操作角色引用（Unit/OldPosition/NewPosition）位于前位，
        // 效果引发情形尾部可携带「触发者卡牌」引用（TriggerCard；不参与复验——去重后计数允许多余项）。
        if (refs.Count < 3)
        {
            return false;
        }

        if (_lifecycle?.IsEnded == true)
        {
            return false; // 终局后移动流程拒绝（含触发器级对外入口；零副作用、状态不推进）
        }

        if (!refs[0].IsAlive || refs[0].Value is not UnitCard unit)
        {
            return false;
        }

        if (!refs[1].IsAlive || refs[1].Value is not Slot oldSlot)
        {
            return false;
        }

        if (!refs[2].IsAlive || refs[2].Value is not Slot newSlot)
        {
            return false;
        }

        var state = unit.GetData<UnitStateData>();
        var command = unit.GetData<CommandData>();
        var owner = unit.Owner;
        var current = _currentPlayerProvider();

        if (current is null || owner is null || !ReferenceEquals(owner, current))
        {
            return false;
        }

        if (state.IsDestroyed || !command.CanMove)
        {
            return false;
        }

        // W2b：行动费复验读「有效值」（与可用性/扣费同源——读取面统一）
        if (owner.Points < unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost))
        {
            return false;
        }

        if (!ReferenceEquals(state.Position, oldSlot))
        {
            return false;
        }

        if (!_battlefield.GetSupportLine(owner).Contains(oldSlot))
        {
            return false; // 仅推进：源位置须为支援线
        }

        if (!_battlefield.FrontLine.Contains(newSlot) || !newSlot.IsEmpty)
        {
            return false; // 目标须为前线空槽
        }

        if (HasLivingEnemyOnFrontLine(owner))
        {
            return false; // 推进前置复验（后置项 C；防御性双保险）：前线存在存活敌方单位＝拒绝
        }

        return true;
    }

    private bool RevalidateAttack(IReadOnlyList<Ref<Entity>> refs)
    {
        // X1：refs＝触发数据第一层引用收集（插入序）——操作角色引用（Attacker/Target）位于前位，
        // 效果引发情形尾部可携带「触发者卡牌」引用（TriggerCard；不参与复验——去重后计数允许多余项）。
        if (refs.Count < 2)
        {
            return false;
        }

        if (_lifecycle?.IsEnded == true)
        {
            return false; // 终局后攻击流程拒绝（含触发器级对外入口；零副作用、状态不推进）
        }

        if (!refs[0].IsAlive || refs[0].Value is not UnitCard attacker)
        {
            return false;
        }

        var targetRef = refs[1];
        if (!targetRef.IsAlive)
        {
            return false;
        }

        var state = attacker.GetData<UnitStateData>();
        var command = attacker.GetData<CommandData>();
        var owner = attacker.Owner;
        var current = _currentPlayerProvider();

        if (current is null || owner is null || !ReferenceEquals(owner, current))
        {
            return false;
        }

        if (state.IsDestroyed || !command.CanAttack)
        {
            return false;
        }

        // W2b：行动费复验读「有效值」（与可用性/扣费同源——读取面统一）
        if (owner.Points < attacker.Modifiers.GetEffectiveValue(CardStatFields.OperateCost))
        {
            return false;
        }

        return IsAttackTargetLegal(attacker, targetRef);
    }

    /// <summary>攻击目标有效性复验（覆盖「目标引用有效性」：单位仍存活在场 / HQ 引用仍为敌方 HQ——W3-3 实体承载）。</summary>
    private bool IsAttackTargetLegal(UnitCard attacker, Ref<Entity> targetRef)
    {
        if (!targetRef.IsAlive)
        {
            return false;
        }

        var targetEntity = targetRef.Value;
        if (targetEntity is Hq hq)
        {
            return IsLegalHqTarget(attacker, hq);
        }

        if (targetEntity is UnitCard target)
        {
            return IsLegalUnitTarget(attacker, target);
        }

        return false;
    }

    // ---------- 辅助 ----------

    private Player? EnemyOf(Player player)
    {
        foreach (var candidate in _players)
        {
            if (!ReferenceEquals(candidate, player))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool HasUnitType(UnitCard unit, UnitType type)
        => unit.TryGetData<UnitStateData>(out var state) && state.UnitTypes.Contains(type);

    /// <summary>任意线射程组（炮兵/战斗机/轰炸机任一——存在性判定；含同线、含 HQ 全组合允许）。</summary>
    private static bool HasAnyLineRange(UnitCard unit)
        => HasUnitType(unit, UnitType.Artillery)
            || HasUnitType(unit, UnitType.Fighter)
            || HasUnitType(unit, UnitType.Bomber);

    /// <summary>炮/轰组（炮兵/轰炸机任一——被守护攻击资格：不含战斗机）。</summary>
    private static bool CountsAsBombard(UnitCard unit)
        => HasUnitType(unit, UnitType.Artillery) || HasUnitType(unit, UnitType.Bomber);

    /// <summary>
    /// 推进前置判定（后置项 C）：前线是否存在存活敌方单位（空前线或己方已占＝false；敌方清空后实时恢复）。
    /// </summary>
    private bool HasLivingEnemyOnFrontLine(Player owner)
    {
        var enemy = EnemyOf(owner);
        if (enemy is null)
        {
            return false;
        }

        foreach (var slot in _battlefield.FrontLine)
        {
            if (slot.Occupant is UnitCard unit && !IsDead(unit) && ReferenceEquals(unit.Owner, enemy))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 轰炸机拦截判定（后置项 C）：攻击者含轰炸机 ∧ 目标非战斗机 ∧ 目标所在战线存在存活敌方战斗机 → 拦截（置黑）。
    /// 跨战线其他目标不受影响；以存活为限；多条战斗机共存＝无额外优先级（存在性判定）。
    /// </summary>
    private bool IsBlockedByEnemyFighter(UnitCard attacker, Slot targetSlot, bool targetIsFighter)
    {
        if (!HasUnitType(attacker, UnitType.Bomber) || targetIsFighter)
        {
            return false;
        }

        var enemy = attacker.Owner is { } owner ? EnemyOf(owner) : null;
        if (enemy is null)
        {
            return false;
        }

        if (ResolveLineOf(targetSlot) is not { } line)
        {
            return false; // 异常布局（防御）
        }

        foreach (var slot in line)
        {
            if (slot.Occupant is UnitCard unit && !IsDead(unit)
                && ReferenceEquals(unit.Owner, enemy) && HasUnitType(unit, UnitType.Fighter))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>槽位所属战线（三线判定；异常布局＝null——防御）。</summary>
    private BattleLine? ResolveLineOf(Slot slot)
    {
        if (_battlefield.FrontLine.Contains(slot))
        {
            return _battlefield.FrontLine;
        }

        if (_battlefield.PlayerASupportLine.Contains(slot))
        {
            return _battlefield.PlayerASupportLine;
        }

        if (_battlefield.PlayerBSupportLine.Contains(slot))
        {
            return _battlefield.PlayerBSupportLine;
        }

        return null;
    }

    private static bool IsDead(UnitCard unit)
        => !unit.TryGetData<UnitStateData>(out var state) || state.IsDestroyed;

    private static CommandFailureReason MapIneligible(CommandBlockReason reason) => reason switch
    {
        CommandBlockReason.NonOwnerTurn => CommandFailureReason.NonOwnerTurn,
        CommandBlockReason.OwnerInvalid => CommandFailureReason.OwnerInvalid,
        CommandBlockReason.UnitDead => CommandFailureReason.UnitDead,
        _ => CommandFailureReason.CommandFlowFault,
    };

    private static void RequireUnitized(UnitCard unit)
    {
        if (!unit.TryGetData<UnitStateData>(out _) || !unit.TryGetData<CommandData>(out _))
        {
            throw new InvalidOperationException(
                $"单位 '{unit.Name}' 尚未单位化（缺少单位/指挥组件），不能发起指挥流程（装配性错误）。");
        }
    }
}

/// <summary>
/// 复验触发器（2C；框架内部）：验证委托化的触发器子类——<see cref="Trigger{TView}.Validate"/> 转发至注入的复验函数
/// （「执行前复验（分派后、执行前兜底）以验证机制表意」的承载：拒绝＝ValidationRejected、仅本次取消、零副作用＋留痕）。
/// 复验函数经 refs（触发数据第一层引用：单位/槽位引用面）恢复本次触发的目标上下文。
/// </summary>
internal sealed class DelegateCheckTrigger<TView> : Trigger<TView>
    where TView : class
{
    private readonly Func<IReadOnlyList<Ref<Entity>>, bool> _validate;

    /// <summary>创建复验触发器。</summary>
    /// <exception cref="ArgumentNullException">validate 为 null。</exception>
    internal DelegateCheckTrigger(string name, Func<IReadOnlyList<Ref<Entity>>, bool> validate)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _validate = validate;
    }

    /// <inheritdoc />
    public override bool Validate(IReadOnlyList<Ref<Entity>> refs) => _validate(refs);
}
