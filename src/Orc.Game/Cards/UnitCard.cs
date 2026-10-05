using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Players;
using Orc.Game.Triggers;

namespace Orc.Game.Cards;

/// <summary>
/// 单位卡（三大类之一）：部署入战场、参与战斗（2B 打出链就绪；2C：参与指挥/战斗与词条）。
/// 数据组件装配（E 区差异化）：＝阵营〔国籍〕＋部署费合并组件（基类）＋对战数据（本类构造装配；行动费/攻/防初始值）；
/// 单位数据 / 指挥组件于「单位化」时挂载（2B：单位化触发器默认事件——位置＝槽位、已毁＝false、类型＝从定义填充〔2C〕、三实时值＝对战组件值）。
/// 触发器（2B）：
/// ①预打出触发器（费用校验——指挥点验证；开始＝验证＋targeter 交互由打出管理器驱动）；
/// ②打出触发器（费用校验——外层复验；默认链＝打出宣告〔card.played〕→ 部署链 → 收尾〔扣费→离手→词条落点〔闪击＝扣费后〕〕）；
/// ③部署触发器（默认链＝部署逻辑检查〔组件存在且 handler 非空〕→ 部署词条效果按序触发〔逐条异常隔离〕→ 单位化触发器 → card.placed → unit.deployed）；
/// ④加入触发器（默认链＝单位化触发器 → card.placed → unit.joined；不扣费、不走部署词条）；
/// ⑤单位化触发器（部署/加入共用：加单位组件＋指挥组件＋实际加入空槽位＋建立修饰机制初始快照〔W2b〕）。
/// 门户（W2b G3；单位数值受控变更面——防御语义）：伤害扣减 <see cref="ApplyDefenseDamageAsync"/>（损伤量增加）与
/// 修复 <see cref="RepairDefenseAsync"/>（恢复到上限）＝运行期数值本体的合规变更入口（配合卡侧修饰容器＝修饰加值/撤销）；
/// 变更一律经「门户 → 跑链（修饰机制管线）→ 有变更集中触发」。
/// 门户（S9 G13；类型增补）：<see cref="AddUnitTypeAsync"/>＝单位类型集合的唯一合规运行期增补路径
/// （去重幂等/非法值拒绝/变更信号——实际改变集合恰发一次 unit.types.changed）。
/// 触发数据约定：Card＝本卡、Player＝所有者（可缺省/可空——加入路径不要求归属）、Position＝目标槽位（Slot 对象）。
/// 加载模板与其余装配沿用基类（<see cref="CardBase"/>）；持久化重建经基类扩展点。
/// </summary>
public class UnitCard : CardBase
{
    private readonly LogicEngine _chainEngine;

    /// <summary>创建单位卡（对战数据组件＋触发器与默认链事件在构造期装配——实例化后即可读、可驱动；
    /// 费用校验触发器按名绑定费用检查判定器——解析器缺省＝内置默认〔独立构造即可用〕）。</summary>
    /// <param name="engine">引擎（发射/触发）。</param>
    /// <param name="definition">卡牌定义。</param>
    /// <param name="validationJudicatorResolver">验证判定器解析器（按名解析——对局路径＝注册表解析；
    /// 缺省＝null＝独立构造路径——内置默认解析）。</param>
    /// <exception cref="ArgumentNullException">engine 或 definition 为 null。</exception>
    public UnitCard(
        LogicEngine engine,
        CardDefinition definition,
        Func<string, JudicatorBinding>? validationJudicatorResolver = null)
        : base(engine, definition)
    {
        _chainEngine = engine;

        // 对战数据组件（单位专属；E 区：「单位＝对战数据组件」——指令无其他组件、反制为激活状态组件）。
        AddData(new BattleStatsData(definition.OperateCost, definition.Attack, definition.Defense));

        // 触发器（预打出/打出＝费用校验触发器——合法性验证承载；部署/加入/单位化＝链触发器）。
        PrePlayTrigger = new CostCheckTrigger("预打出触发器", this, validationJudicatorResolver);
        PlayTrigger = new CostCheckTrigger("打出触发器", this, validationJudicatorResolver);
        DeployTrigger = new Trigger<CardTriggerView>("部署触发器");
        JoinTrigger = new Trigger<CardTriggerView>("加入触发器");
        UnitizeTrigger = new Trigger<CardTriggerView>("单位化触发器");

        // 默认链事件（构造期装配；装配方扩展事件按注册序追加于其后——闪击等词条注入属 2C）。
        PlayTrigger.Register("打出链", HandlePlayChainAsync);
        DeployTrigger.Register("部署链", HandleDeployChainAsync);
        JoinTrigger.Register("加入链", HandleJoinChainAsync);
        UnitizeTrigger.Register("单位化", HandleUnitizeAsync);
    }

    /// <summary>预打出触发器（费用校验：指挥点验证；开始/交互编排由打出管理器驱动；装配方扩展点保留）。</summary>
    public Trigger<CardTriggerView> PrePlayTrigger { get; }

    /// <summary>打出触发器（费用校验＋默认链：外层复验＋打出宣告＋部署链＋收尾〔扣费→离手〕）。</summary>
    public Trigger<CardTriggerView> PlayTrigger { get; }

    /// <summary>部署触发器（部署链：部署逻辑检查＋词条效果按序 → 单位化 → unit.deployed）。</summary>
    public Trigger<CardTriggerView> DeployTrigger { get; }

    /// <summary>加入触发器（加入链：单位化 → unit.joined；不扣费、不走部署词条）。</summary>
    public Trigger<CardTriggerView> JoinTrigger { get; }

    /// <summary>单位化触发器（部署/加入共用：加单位组件＋指挥组件＋实际加入空槽位）。</summary>
    public Trigger<CardTriggerView> UnitizeTrigger { get; }

    // ---------- 默认链（构造期装配；链只经公开触发器面驱动） ----------

    /// <summary>打出链（外层；仅部署路径）：card.played → 部署链 → 收尾（扣费 → 离手）。</summary>
    private async Task HandlePlayChainAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not UnitCard unit || view.Player is not Player player || view.Position is not { } slot)
        {
            ctx.Interrupt(); // 载荷缺失（结构性错误）：链中止、收尾不发生
            return;
        }

        // ① 打出宣告（card.played；W4-1 升级版载荷＝{ Card, Player }——被使用卡实例＋使用方）
        await GameUpdates.EmitCardPlayed(_chainEngine, unit, player, ct);

        // ② 内层部署链（部署词条 → 单位化 → unit.deployed）
        await unit.DeployTrigger.InvokeAsync(_chainEngine, BuildUnitData(unit, player, slot), ct);
        if (ctx.Interrupted)
        {
            return; // 部署链未完成（防御：单位化失败等）：不进入收尾（不扣费、不离手）
        }

        // ③ 收尾：扣费（仅部署扣费——恰一次；W3-2 G5：读有效部署费——与校验/复验同口径）→ 离手（扣费之后、链尾前最后一步）→ 词条落点（2C-A1：闪击＝扣费后；经静态助手收口——组件遍历在其内）
        player.Points -= unit.Modifiers.GetEffectiveValue(CardStatFields.DeployCost);
        player.Hand.Remove(unit);
        await KeywordRules.OnDeployChainFinalizedAsync(unit, ct);
    }

    /// <summary>部署链（内层）：默认检查〔部署逻辑组件存在且 handler 非空〕→ 部署词条效果按序触发 → 单位化 → card.placed → unit.deployed。</summary>
    private async Task HandleDeployChainAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not UnitCard unit || view.Position is not { } slot)
        {
            ctx.Interrupt();
            return;
        }

        // ① 部署逻辑检查＋部署词条效果：单源执行段（A4：DeploymentLogicRules——部署链①段与再触发「部署重放」共享
        //    同一执行实现；同序〔登记序〕／同「handler 非空」检查／同逐条异常隔离／同上下文／同留痕形态）。
        //    无组件或有效条目为空＝跳过（部署不因此失败）。
        await DeploymentLogicRules.RunEffectSegmentAsync(unit, slot, _chainEngine, ct);

        // ② 单位化（共用：加组件＋入槽）
        await unit.UnitizeTrigger.InvokeAsync(_chainEngine, BuildUnitData(unit, view.Player as Player, slot), ct);
        if (ctx.Interrupted)
        {
            return; // 单位化未完成（防御：装配失败不留矛盾中间态）：不发射 card.placed / unit.deployed
        }

        // ③ W3-A3：放置驱动装载信号（card.placed）——单位化（组件挂载＋槽位占用）成功之后、完成信号之前
        //    （策略 1 效果以此驱动延迟挂载；装载链放置处理器幂等吸收；失败/中止不发射——「每次成功完成的部署恰发射一次」）。
        await GameUpdates.EmitCardPlaced(_chainEngine, unit, ct);

        // ④ 部署完成：unit.deployed（单位化之后——组件挂载＋槽位占用已成立）
        await GameUpdates.EmitUnitDeployed(_chainEngine, unit, slot, ct);
    }

    /// <summary>加入链：单位化 → card.placed → unit.joined（不扣费、不走部署词条）。</summary>
    private async Task HandleJoinChainAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not UnitCard unit || view.Position is not { } slot)
        {
            ctx.Interrupt();
            return;
        }

        await unit.UnitizeTrigger.InvokeAsync(_chainEngine, BuildUnitData(unit, view.Player as Player, slot), ct);
        if (ctx.Interrupted)
        {
            return; // 单位化未完成（防御）：不发射 card.placed / unit.joined
        }

        // W3-A3：放置驱动装载信号（card.placed）——单位化成功之后、完成信号之前（每次成功完成的加入恰发射一次）。
        await GameUpdates.EmitCardPlaced(_chainEngine, unit, ct);

        await GameUpdates.EmitUnitJoined(_chainEngine, unit, slot, ct);
    }

    /// <summary>
    /// 单位化（部署/加入共用；链尾共用段）：加单位组件（位置＝槽位、已毁＝false、类型＝从定义填充〔2C 加性〕、三实时值＝对战组件值）
    /// ＋指挥组件（初始 false/false）＋实际加入空槽位＋请求跑链一次（W2b：装配完成点建立修饰机制「初始快照」——
    /// 无修饰/无损伤＝零变化、零发射；此后任何数值变更（伤害/修饰）均可被检测与集中触发）。
    /// 结构不变量（矛盾中间态防护）：仅空槽、未单位化的卡可单位化——违反＝链中止（不放置、不挂组件、零中间态残留）。
    /// </summary>
    private async Task HandleUnitizeAsync(CardTriggerView view, Context ctx, CancellationToken ct)
    {
        if (view.Card is not UnitCard unit || view.Position is not { } slot
            || !slot.IsEmpty || unit.TryGetData<UnitStateData>(out _))
        {
            ctx.Interrupt(); // 载荷缺失 / 槽位非空 / 二次单位化：链中止（留痕由 Interrupt 写入）
            return;
        }

        var state = UnitStateData.CreateInitial(unit.GetData<BattleStatsData>());
        state.FillInitialTypes(unit.Definition.UnitTypes); // 2C：单位类型清单从定义填充（部署/加入两路径一致）；S9：装配期填充＝列明例外（静默面）
        state.Position = slot;
        unit.AddData(state);
        unit.AddData(new CommandData());
        slot.Place(unit);

        // W2b G3 接线：装配完成点请求跑链一次（首轮——以基准状态建立比较基线；本时点无修饰/无损伤＝零变化、零发射）。
        await unit.Modifiers.RequestRerunAsync(ct);
    }

    // ---------- 门户（W2b G3；单位数值受控变更面——防御语义） ----------

    /// <summary>
    /// 门户：防御伤害扣减（受控变更——数值本体的运行期唯一合规入口之一；配合修饰容器＝修饰加值/撤销）：
    /// 损伤量增加（伤害＝即时变更：只扣当前、不减上限；不经修饰器）→ 跑链（修饰机制管线）→ 有变更才发
    /// （card.stat.changed——先落定、后发射）；返回即终态（致死时含死亡判定与死亡流程完成）。
    /// 死亡后数值面冻结：已死亡/已毁＝明确拒绝（fail-fast、零副作用）；未单位化＝明确异常（装配性错误）。
    /// </summary>
    /// <param name="amount">伤害量（≥0；0＝合法——跑链无变化时零发射）。</param>
    /// <exception cref="ArgumentOutOfRangeException">amount 为负。</exception>
    /// <exception cref="InvalidOperationException">未单位化；或已死亡/已毁（死亡后数值面冻结——明确拒绝）。</exception>
    public async Task ApplyDefenseDamageAsync(int amount, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var state = RequireUnitState();
        if (state.IsDestroyed)
        {
            throw new InvalidOperationException(
                $"单位 '{Name}' 已死亡/已毁，不能进行伤害操作（死亡后数值面冻结——明确拒绝）。");
        }

        state.DefenseLoss += amount; // 受控写入（门户面；运行期直写收窄）

        // A2：动员——「受到伤害」（净伤害＞0、防御实际扣减）后失去（伤害被完全吸收/归零〔amount=0〕＝不算；
        // 失去走词条移除链、既得 +1/+1 保留）。先记伤害、后失去、末跑链（数值落定）。
        if (amount > 0)
        {
            await MobilizeRules.OnUnitDamagedAsync(this);
        }

        await Modifiers.RequestRerunAsync(ct); // 变更经门户 → 跑链 → 变化时集中触发
    }

    /// <summary>
    /// 门户：修复（受控变更——恢复到上限）：损伤量清零（＝当前值恢复到有效上限）→ 跑链 → 有变更才发。
    /// 仅对存活单位有效：已死亡/已毁＝明确拒绝（fail-fast）；满血（无损伤）＝幂等无变化（零发射）。
    /// （G17 完整语义后续批次；本单＝动作面＋模拟验证。）
    /// </summary>
    /// <exception cref="InvalidOperationException">未单位化；或已死亡/已毁（明确拒绝）。</exception>
    public async Task RepairDefenseAsync(CancellationToken ct = default)
    {
        var state = RequireUnitState();
        if (state.IsDestroyed)
        {
            throw new InvalidOperationException(
                $"单位 '{Name}' 已死亡/已毁，不能修复（仅对存活单位有效——明确拒绝）。");
        }

        state.DefenseLoss = 0;
        await Modifiers.RequestRerunAsync(ct);
    }

    /// <summary>
    /// 门户：类型增补（S9；单位类型集合的唯一合规运行期增补路径——「也算作」类效果的受控入口）：
    /// 集合语义（「确保该类型在集合中」）——已含该类型＝幂等无操作（不重复登记、不重复发信号）；null 不可达（值类型）；
    /// 未定义枚举值＝拒绝（非法值）；未单位化（无单位数据组件）＝明确异常（装配性错误）；
    /// 实际改变集合＝恰发射一次 <see cref="GameUpdates.UnitTypesChanged"/>（载荷＝本单位＋新增类型——增量；
    /// 完整类型集由监听者从单位读取「读取现势」）。
    /// 生效即时（判定读点动态读取当前实例类型集——无需修改读点代码）；实例级（仅作用于本实例、不写回定义；
    /// 不迁移同定义其他实例）；转换产生的静态新实例不携带本增补（新实例＝从定义重建）。
    /// </summary>
    /// <param name="type">单位类型（须为已定义枚举值；非法值＝拒绝）。</param>
    /// <exception cref="ArgumentOutOfRangeException">type 为未定义枚举值（非法值——明确拒绝）。</exception>
    /// <exception cref="InvalidOperationException">尚未单位化（缺单位数据组件——装配性错误）。</exception>
    public async Task AddUnitTypeAsync(UnitType type, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type), type, "未定义的单位类型枚举值（非法值——明确拒绝）。");
        }

        var state = RequireUnitState();
        if (!state.TryAddRuntimeType(type))
        {
            return; // 幂等（已含该类型）：无操作、不重复发信号——集合语义「确保在集合中」、结果成功
        }

        await GameUpdates.EmitUnitTypesChanged(_chainEngine, this, type, ct); // 实际变更：恰一次（先落定、后发射）
    }

    /// <summary>
    /// 读取「防御有效上限」（只读查询面；W2b）：有效上限＝对战组件基准＋Σ加防修饰（现算；只读、不落定不发射）。
    /// 与有效值读取面并列；不提供任意写（变更仍经门户/修饰）。死亡后查询可用（数值本体保留、只读）。
    /// </summary>
    /// <exception cref="InvalidOperationException">未单位化（缺单位数据组件——基准来源未就绪）。</exception>
    public int GetEffectiveDefenseCap() => Modifiers.GetEffectiveCapValue(CardStatFields.Defense);

    // ---------- 在场回合数（G14补 S10；回合事件驱动计数） ----------

    /// <summary>
    /// 读取当前在场回合数（G14补 S10 读取面——最小读取集「读取当前在场回合数」唯一能力；
    /// 「是否处于在场第 N 回合」＝读取值比较派生、不单独立面）。
    /// 语义：入场即第 1 回合；己方回合正式开始（turn.start）递增（单方步进、静默）；
    /// 死亡后停止递增（值保持、可读）；未入场（手牌/卡组）＝不适用（null、不抛错——沿用降级先例）。
    /// </summary>
    public int? TurnsInPlay => TryGetData<UnitStateData>(out var state) ? state.TurnsInPlay : null;

    /// <summary>
    /// 在场回合数递增（G14补 S10 内部执行面；回合事件驱动——由对局在「单位归属玩家的回合正式开始」时调用；
    /// 单方步进：仅归属==回合开始方时递增——调用方负责归属过滤）：未单位化/已死亡＝false 无操作
    /// （死亡停止；死亡单位已离场、通常不可达——防御双保险）；递增＝true。
    /// 静默数据变更（不发射/不通知——不新增信号）。
    /// </summary>
    internal bool AdvanceTurnsInPlay()
    {
        if (!TryGetData<UnitStateData>(out var state) || state.IsDestroyed)
        {
            return false;
        }

        state.TurnsInPlay += 1;
        return true;
    }

    /// <summary>单位数据组件就绪校验（门户操作前提；未单位化＝明确异常、不静默）。</summary>
    private UnitStateData RequireUnitState()
        => TryGetData<UnitStateData>(out var state)
            ? state
            : throw new InvalidOperationException(
                $"单位 '{Name}' 尚未单位化（缺单位数据组件），不能执行数值变更操作（装配性错误）。");

    /// <summary>单位链触发数据（Card / Player / Position；Player 可缺省/可空——加入路径不要求归属）。</summary>
    private static Dictionary<string, object?> BuildUnitData(UnitCard unit, Player? player, Slot slot)
        => new()
        {
            [GameUpdates.PayloadCard] = unit,
            [GameUpdates.PayloadPlayer] = player,
            [GameUpdates.PayloadPosition] = slot,
        };
}
