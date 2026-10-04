using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Players;
using Orc.Game.Targeting;

namespace Orc.Game.Commanding;

// ─────────────────────────────────────────────────────────────────────────────
// 2C 指挥管理器（指挥系统承载）：
// ①内置流程触发器（引擎侧创建——指挥触发器 / 单位移动触发器 / 单位攻击触发器 / 造成攻击伤害触发器；
//   由对局装配期注册为底层触发器〔进注册表、可查询〕）；
// ②指挥入口（BeginCommandAsync）：一次拖拽＝一次调用链、单一公开入口方法驱动；输入＝被拖动单位引用；
//   发起判定（非己方回合 / 归属无效 / 已死亡 / 两动作均不可用）→ 指挥触发器（流程编排：读判定 → 交互 →
//   分派〔空槽→移动；敌方单位/HQ→攻击〕→ 嵌套执行 → 外层收尾〔扣费＋两 bool 更新〕）；
// ③动作可用性聚合判定公开面（GetCommandAvailability）：bool＋原因＋候选；与流程内部同源、纯查询；
// ④移动执行（仅推进：支援线→前线；发射 unit.position.changed——恰一次）；
// ⑤攻击执行（单位 vs 单位必经「造成攻击伤害」触发器中转；HQ 简路＝直接扣血）；攻击结算＝同时互伤 →
//   HP≤0 死亡（统一死亡流程：清位＋置毁＋效果注销＋Position 置空＋card.died 恰一次）；反击豁免体系零特例（后置）；
// ⑥守护维护（被守护状态：相邻〔同线槽位索引差 1〕守护者 → 获被守护；仅能被炮/轰攻击；守护者自身不可被守护；
//   由位置/入场/死亡更新驱动重算——底层链负责）；⑦回合恢复（turn.start 处理段：行动方在场单位重置两 bool）。
// 「两 bool 只在外层更新」：内层（移动/攻击/伤害/词条）一律不写——法定写入点＝指挥收尾 / 部署链收尾（闪击）/ 回合恢复。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 指挥管理器（2C；对局级服务）：指挥流程入口与编排、KARDS 规则矩阵、
/// 攻击结算与统一死亡流程、守护维护、回合恢复。
/// 依赖：引擎（发射/触发）、战场（候选计算与布局）、目标选择管理器（交互承载）、
/// 玩家对（敌我判定）、当前行动方提供器（延迟读取——装配顺序：回合管理器后置）。
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

    private readonly HashSet<UnitCard> _guardedUnits = new();
    private readonly HashSet<Player> _guardedHqs = new();

    /// <summary>
    /// 创建指挥管理器（创建四个内置流程触发器并装配默认链；订阅位置/入场/死亡更新以维护被守护状态）。
    /// </summary>
    /// <param name="engine">对局引擎（发射更新 / 触发子触发器）。</param>
    /// <param name="battlefield">战场（候选计算与布局真源）。</param>
    /// <param name="targeterManager">目标选择管理器（指挥交互承载——一次拖拽＝一次请求）。</param>
    /// <param name="players">双玩家（敌我判定；行动方＋其对手）。</param>
    /// <param name="currentPlayerProvider">当前行动方提供器（延迟读取；缺省＝null＝回合未就绪——发起校验将拒绝）。</param>
    /// <exception cref="ArgumentNullException">engine / battlefield / targeterManager / players 为 null。</exception>
    /// <exception cref="ArgumentException">players 不是两名玩家。</exception>
    public CommandManager(
        LogicEngine engine,
        Battlefield battlefield,
        TargeterManager targeterManager,
        IReadOnlyList<Player> players,
        Func<Player?>? currentPlayerProvider = null)
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
    }

    // ---------- 内置流程触发器（公开只读；对局装配期注册为底层触发器） ----------

    /// <summary>指挥触发器（内置；一次指挥流程的编排承载——读判定 → 交互 → 分派 → 嵌套执行 → 外层收尾）。</summary>
    public Trigger<CommandTriggerView> CommandTrigger { get; }

    /// <summary>单位移动触发器（内置；执行段＝槽位变更＋发射 unit.position.changed；复验＝执行前兜底）。</summary>
    public Trigger<UnitMoveTriggerView> UnitMoveTrigger { get; }

    /// <summary>单位攻击触发器（内置；执行段＝HQ 扣血直路 / 单位互伤经「造成攻击伤害」）。</summary>
    public Trigger<UnitAttackTriggerView> UnitAttackTrigger { get; }

    /// <summary>「造成攻击伤害」共享流程触发器（内置；单位 vs 单位攻击结算必经；伏击挂载点）。</summary>
    public Trigger<AttackDamageTriggerView> AttackDamageTrigger { get; }

    /// <summary>词条装载上下文（卡牌加载时取用；经对局装配注入卡牌库）。</summary>
    public KeywordLoadContext KeywordLoadContext { get; }

    // ---------- ① 指挥入口（一次拖拽＝一次调用链、单一公开入口方法） ----------

    /// <summary>
    /// 指挥单位（一次调用链：发起判定 → 指挥触发器〔交互 → 分派 → 嵌套执行 → 收尾〕→ 结果）：
    /// 输入＝被拖动单位引用（行动方从单位归属与对局状态推导——无需额外上下文参数）。
    /// 发起拒绝（非己方回合 / 归属无效 / 已死亡 / 两动作均不可用）＝不进入交互、零副作用、不发射更新；
    /// 取消＝零副作用；成功＝完整执行（含以攻击者死亡告终的攻击结算——照常扣费、照常清位）。
    /// 装配性错误（未单位化）＝明确异常（fail-fast）。
    /// </summary>
    /// <exception cref="ArgumentNullException">unit 为 null。</exception>
    /// <exception cref="InvalidOperationException">单位尚未单位化（装配性错误、fail-fast）。</exception>
    public async Task<CommandResult> BeginCommandAsync(UnitCard unit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        RequireUnitized(unit);

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
        await CommandTrigger.InvokeAsync(
            _engine,
            new Dictionary<string, object?>
            {
                [CommandDataKeys.Card] = unit.Ref,
                [CommandDataKeys.Player] = unit.Owner,
                [CommandDataKeys.FlowBox] = box,
            },
            ct);

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

        if (owner.Points < state.OperateCost)
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.PointShortage);
        }

        // 仅推进：仅支援线单位可移动（前线单位无移动候选）
        if (state.Position is null || !_battlefield.GetSupportLine(owner).Contains(state.Position))
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.NoCandidates);
        }

        // 移动候选＝前线任意空槽（不被邻位动态规则约束）
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
        var state = unit.GetData<UnitStateData>();
        var command = unit.GetData<CommandData>();

        if (!command.CanAttack)
        {
            return CommandActionAvailability.Blocked(CommandBlockReason.FlagFalse);
        }

        if (owner.Points < state.OperateCost)
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

        var hqSlot = enemyLine[0];
        if (hqSlot.Occupant is Player hq && ReferenceEquals(hq, enemy) && IsLegalHqTarget(attacker, hqSlot))
        {
            result.Add(hqSlot.Ref);
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

        return IsLineInRange(attacker, targetState.Position);
    }

    /// <summary>HQ 为目标的合法性（仍为敌方 HQ ∧ 被守护仅炮/轰可攻 ∧ 范围矩阵；HQ 无死亡/烟幕语义）。</summary>
    private bool IsLegalHqTarget(UnitCard attacker, Slot hqSlot)
    {
        var enemy = attacker.Owner is { } owner ? EnemyOf(owner) : null;
        if (enemy is null || !ReferenceEquals(hqSlot.Occupant, enemy))
        {
            return false;
        }

        if (_guardedHqs.Contains(enemy) && !CountsAsBombard(attacker))
        {
            return false; // HQ 被守护（相邻守护者）：仅能被炮/轰攻击
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

    /// <summary>被守护状态查询（HQ）：同方支援线（HQ 占位槽的索引差 1 邻位）存在存活守护者 ＝ 被守护。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public bool IsHqGuarded(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return _guardedHqs.Contains(player);
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
        if (supportLine[0].Occupant is not Player hq)
        {
            return;
        }

        if (IsGuardianAt(supportLine, 1, hq))
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

        // 分派：选中空槽 → 移动触发器；选中敌方单位 / 敌方 HQ → 攻击触发器
        await DispatchSelectedAsync(unit, selected, box, ctx, ct);
    }

    private async Task DispatchSelectedAsync(
        UnitCard unit, Ref<Entity> selected, CommandFlowBox box, Context ctx, CancellationToken ct)
    {
        var owner = unit.Owner;
        var enemy = owner is null ? null : EnemyOf(owner);
        var selectedEntity = selected.Value;

        if (selectedEntity is Slot slot)
        {
            if (slot.IsEmpty)
            {
                await DispatchMoveAsync(unit, selected, box, ctx, ct);
                return;
            }

            if (enemy is not null && slot.Occupant is Player hq && ReferenceEquals(hq, enemy))
            {
                await DispatchAttackAsync(unit, selected, box, ctx, ct);
                return;
            }
        }
        else if (selectedEntity is UnitCard)
        {
            await DispatchAttackAsync(unit, selected, box, ctx, ct);
            return;
        }

        // 无效目标分派（防御；正常流程不可达——候选面之外者不产生终局）
        box.Result = CommandResult.Failure(CommandFailureReason.TargetingFailed);
    }

    private async Task DispatchMoveAsync(
        UnitCard unit, Ref<Entity> newPositionRef, CommandFlowBox box, Context ctx, CancellationToken ct)
    {
        var state = unit.GetData<UnitStateData>();
        var oldSlot = state.Position;
        if (oldSlot is null)
        {
            box.Result = CommandResult.Failure(CommandFailureReason.CommandFlowFault);
            return;
        }

        // 嵌套执行（复验＝移动触发器验证承载——拒绝＝仅本次取消、零副作用＋留痕）
        var stream = await UnitMoveTrigger.InvokeAsync(
            _engine,
            new Dictionary<string, object?>
            {
                [CommandDataKeys.Unit] = unit.Ref,
                [CommandDataKeys.OldPosition] = oldSlot.Ref,
                [CommandDataKeys.NewPosition] = newPositionRef,
            },
            ct);

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
        UnitCard unit, Ref<Entity> targetRef, CommandFlowBox box, Context ctx, CancellationToken ct)
    {
        // 嵌套执行（复验＝攻击触发器验证承载——拒绝＝仅本次取消、零副作用＋留痕）
        var stream = await UnitAttackTrigger.InvokeAsync(
            _engine,
            new Dictionary<string, object?>
            {
                [CommandDataKeys.Attacker] = unit.Ref,
                [CommandDataKeys.Target] = targetRef,
            },
            ct);

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

    /// <summary>移动收尾（外层）：扣费（行动方扣除、恰一次）→ 两 bool 更新（非坦克＝二选一：另一动作同被清）。</summary>
    private static void FinalizeMove(UnitCard unit)
    {
        var owner = unit.Owner!;
        var state = unit.GetData<UnitStateData>();
        var command = unit.GetData<CommandData>();

        owner.Points -= state.OperateCost;
        command.CanMove = false;
        if (!HasUnitType(unit, UnitType.Tank))
        {
            command.CanAttack = false;
        }
    }

    /// <summary>攻击收尾（外层）：扣费（行动方扣除、恰一次）→ CanAttack 更新（奋战读取记账）→ 非坦克二选一清 CanMove。</summary>
    private static void FinalizeAttack(UnitCard unit)
    {
        var owner = unit.Owner!;
        var state = unit.GetData<UnitStateData>();
        var command = unit.GetData<CommandData>();

        owner.Points -= state.OperateCost;
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
        if (targetEntity is Slot hqSlot && hqSlot.Occupant is Player hq && !ReferenceEquals(hq, attacker.Owner))
        {
            // HQ 简路：直接扣血（伤害＝攻击者实时攻击力；HQ 不反击、胜负担后置；钳制到 0；不走「造成攻击伤害」）
            var damage = attacker.GetData<UnitStateData>().Attack;
            hq.HqHealth = Math.Max(0, hq.HqHealth - damage);
        }
        else if (targetEntity is UnitCard)
        {
            // 单位 vs 单位：必经「造成攻击伤害」（伏击挂载点；同一目标承载）
            await AttackDamageTrigger.InvokeAsync(
                _engine,
                new Dictionary<string, object?>
                {
                    [CommandDataKeys.Attacker] = attackerRef,
                    [CommandDataKeys.Target] = targetRef,
                    [CommandDataKeys.Resolution] = new AttackDamageResolution(),
                },
                ct);
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

        if (resolution.IsRewritten)
        {
            // 伏击改写（已判定成立）：替代默认——攻击者死亡（统一死亡流程、无 HP 逐步扣减语义）、被攻击者不受伤
            await ProcessDeathAsync(attacker, ct);
            return;
        }

        // 默认基础互伤：同时结算——双方伤害以互扣前实时值为基准、同时生效（可同归于尽）
        var attackerState = attacker.GetData<UnitStateData>();
        var targetState = target.GetData<UnitStateData>();
        var damageToTarget = attackerState.Attack;
        var damageToAttacker = targetState.Attack;

        targetState.Defense -= damageToTarget;
        attackerState.Defense -= damageToAttacker;

        // 死亡判定（互扣后实时值 ≤ 0）；同归于尽＝两枚 card.died 均发射，顺序：被攻击者在前、攻击者在后
        if (targetState.Defense <= 0)
        {
            await ProcessDeathAsync(target, ct);
        }

        if (attackerState.Defense <= 0)
        {
            await ProcessDeathAsync(attacker, ct);
        }
    }

    /// <summary>
    /// 统一死亡流程（攻击结算判定死亡后调用；伏击改写的攻击者死亡同走此流程）。
    /// 清理最小口径：①槽位释放（变空槽）→ ②IsDestroyed 置位 → ③已挂载效果（含词条效果）注销 →
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

        state.Position = null;
        await GameUpdates.EmitCardDied(_engine, unit, ct);
        // 守护维护由本更新驱动（清位后等效触达——被守护状态可观测变化）
    }

    // ---------- ⑨ 执行前复验（分派后、执行前兜底；验证机制承载——ValidationRejected、零副作用＋留痕） ----------

    private bool RevalidateMove(IReadOnlyList<Ref<Entity>> refs)
    {
        if (refs.Count != 3)
        {
            return false;
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

        if (owner.Points < state.OperateCost)
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

        return true;
    }

    private bool RevalidateAttack(IReadOnlyList<Ref<Entity>> refs)
    {
        if (refs.Count != 2)
        {
            return false;
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

        if (owner.Points < state.OperateCost)
        {
            return false;
        }

        return IsAttackTargetLegal(attacker, targetRef);
    }

    /// <summary>攻击目标有效性复验（覆盖「目标引用有效性」：单位仍存活在场 / HQ 占位仍为敌方玩家）。</summary>
    private bool IsAttackTargetLegal(UnitCard attacker, Ref<Entity> targetRef)
    {
        if (!targetRef.IsAlive)
        {
            return false;
        }

        var targetEntity = targetRef.Value;
        if (targetEntity is Slot hqSlot)
        {
            return IsLegalHqTarget(attacker, hqSlot);
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
