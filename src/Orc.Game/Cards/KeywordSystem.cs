using Orc.Core;
using Orc.Game.Commanding;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 2C 词条系统（对战词条）：
// 词条＝逻辑组件——加载时（CardBase.LoadAsync 的词条装载步骤）从卡牌定义逐条登记到 KeywordData（卡牌固有属性，
// 卡组/手牌即可被读取查询）＋装载主动词条的逻辑组件；被动词条（奋战/烟幕）仅登记、由读取方按标识查询。
// 词条标识＝固定字面值（需求指定、精确匹配）：闪击 / 奋战 / 烟幕 / 伏击。
// 死亡后：逻辑组件注销（伏击撤销注册——防残留幽灵效果）、登记保留（可查询）；不再参与任何结算。
// 无词条卡＝无副作用（不挂组件、不装载）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 词条标识常量集（2C；需求指定固定字面值、精确匹配；对外读取契约）。
/// 本批合法标识仅四枚：<see cref="Blitz"/> / <see cref="Fury"/> / <see cref="SmokeScreen"/> / <see cref="Ambush"/>；
/// 词条清单含未实现标识＝加载期明确错误（fail-fast；校验承载于 <see cref="CardDefinition"/> 定义期）。
/// </summary>
public static class KeywordIds
{
    /// <summary>闪击（主动词条）：部署链收尾（扣费后）置单位可移动＋可攻击。</summary>
    public const string Blitz = "闪击";

    /// <summary>奋战（被动词条）：攻击后记账——首次攻击后仍可攻、二次后不可（「本轮已攻次数」词条侧自维护）。</summary>
    public const string Fury = "奋战";

    /// <summary>烟幕（被动词条）：不可被攻击（对一切攻击者生效；攻击 targeting 中被筛除、不入候选）。</summary>
    public const string SmokeScreen = "烟幕";

    /// <summary>伏击（主动词条）：「造成攻击伤害」内改写——被攻击单位实时攻击力＞攻击者实时防御力时，改为攻击者死亡、被攻击者不受伤。</summary>
    public const string Ambush = "伏击";

    /// <summary>全部合法词条标识（定义期校验依据；登记序）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Blitz, Fury, SmokeScreen, Ambush };

    /// <summary>标识是否为本批已实现词条（存在性判定；null/空白＝false）。</summary>
    public static bool IsDefined(string keyword) => !string.IsNullOrWhiteSpace(keyword) && All.Contains(keyword);
}

/// <summary>
/// 词条装载上下文（2C；加载时装载主动词条逻辑所需的对局级服务面）：
/// 经卡牌库注入（延迟读取）、<see cref="CardBase.LoadAsync"/> 时按需取用；独立构造（脱离对局）＝null
/// （主动词条逻辑装载跳过注册、不抛错——功能不可用、加载不失败）。
/// </summary>
public sealed class KeywordLoadContext
{
    internal KeywordLoadContext(LogicEngine engine, Trigger<AttackDamageTriggerView> attackDamageTrigger)
    {
        Engine = engine;
        AttackDamageTrigger = attackDamageTrigger;
    }

    /// <summary>对局引擎（发射更新 / 触发子触发器所需）。</summary>
    public LogicEngine Engine { get; }

    /// <summary>「造成攻击伤害」共享流程触发器（伏击挂载点；引擎侧创建、与对局同生）。</summary>
    public Trigger<AttackDamageTriggerView> AttackDamageTrigger { get; }
}

/// <summary>
/// 词条逻辑（主动词条的逻辑组件基类；2C）：一个主动词条＝一个逻辑组件实例（加载时创建、装载）。
/// 装载＝挂到相关更新 hook（伏击→「造成攻击伤害」注册改写逻辑）；卸载＝注销（死亡清理触发，防残留幽灵效果）。
/// 闪击的落点＝部署链收尾（<see cref="OnDeployChainFinalizedAsync"/>，扣费后、链返回前执行）。
/// 被动词条（奋战/烟幕）不创建逻辑组件（仅登记＋被读取）。
/// </summary>
public abstract class KeywordLogic
{
    /// <summary>本逻辑承载的词条标识（固定字面值之一）。</summary>
    public abstract string Keyword { get; }

    /// <summary>
    /// 装载（加载时调用）：把词条效果挂到相关更新 hook。
    /// context 为 null（独立构造场景）＝跳过注册（防御、不抛错）。
    /// </summary>
    internal virtual void Mount(CardBase card, KeywordLoadContext? context)
    {
    }

    /// <summary>卸载（死亡清理调用）：注销装载期注册项（幂等；未装载＝无操作）。</summary>
    internal virtual void Unmount()
    {
    }

    /// <summary>
    /// 部署链收尾挂钩（扣费完成后、链返回前；仅部署路径调用）：闪击在此置位两 bool。
    /// 默认无操作（非闪击词条不参与部署链收尾）。
    /// </summary>
    internal virtual Task OnDeployChainFinalizedAsync(CardBase card, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// 闪击（主动词条）逻辑组件：部署链收尾（扣费后）执行——允许单位可移动和攻击（覆盖部署初值 false/false）。
/// 「加载时完成预备、运行时于部署链收尾执行」；仅部署路径生效（加入路径不置位——加入链不走部署收尾）。
/// </summary>
public sealed class BlitzKeywordLogic : KeywordLogic
{
    /// <inheritdoc />
    public override string Keyword => KeywordIds.Blitz;

    /// <inheritdoc />
    internal override Task OnDeployChainFinalizedAsync(CardBase card, CancellationToken ct)
    {
        if (card.TryGetData<CommandData>(out var command))
        {
            command.CanMove = true;
            command.CanAttack = true;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 伏击（主动词条）逻辑组件：加载时向「造成攻击伤害」触发器注册改写逻辑；死亡清理时注销。
/// 改写判定（先资格、后条件）：被攻击单位（目标侧，＝本卡）含伏击 ∧ 目标方按反击豁免判定表具有反击资格
/// （豁免约束改写——无资格＝不发生反击、改写不成立、按表单方结算）∧ 条件命中（被攻击单位实时攻击力 ＞ 攻击者实时防御力，互扣前实时值）
/// → 置改写标志（攻击者死亡、被攻击者不受伤）；资格通过但条件不成立＝正常基础互伤。不区分攻击者类型；HQ 攻击不走该流程。
/// </summary>
public sealed class AmbushKeywordLogic : KeywordLogic
{
    private Trigger<AttackDamageTriggerView>? _trigger;
    private TriggerRegistration? _registration;
    private CardBase? _card;

    /// <inheritdoc />
    public override string Keyword => KeywordIds.Ambush;

    /// <inheritdoc />
    internal override void Mount(CardBase card, KeywordLoadContext? context)
    {
        _card = card;
        if (context is null)
        {
            return; // 无装载上下文（独立构造场景）：不注册（防御、不抛错）
        }

        _trigger = context.AttackDamageTrigger;
        _registration = _trigger.Register("伏击改写", HandleAttackDamageAsync);
    }

    /// <inheritdoc />
    internal override void Unmount()
    {
        var trigger = _trigger;
        var registration = _registration;
        _trigger = null;
        _registration = null;
        if (trigger is not null && registration is not null)
        {
            trigger.Unregister(registration); // 幂等（重复注销＝无操作、不抛错）
        }
    }

    /// <summary>伏击改写判定（「造成攻击伤害」内、默认互伤之前执行——由注册优先级与改写标志保证顺序）。</summary>
    private Task HandleAttackDamageAsync(AttackDamageTriggerView view, Context ctx, CancellationToken ct)
    {
        if (_card is not UnitCard self || !self.TryGetData<UnitStateData>(out var selfState) || selfState.IsDestroyed)
        {
            return Task.CompletedTask; // 死亡后不再改写（双保险：装载已注销 + 存活判定）
        }

        if (view.Target is not { IsAlive: true } targetRef
            || targetRef.Value is not UnitCard target
            || !ReferenceEquals(target, self))
        {
            return Task.CompletedTask; // 仅处理「本卡被攻击」的目标侧
        }

        if (view.Attacker is not { IsAlive: true } attackerRef || attackerRef.Value is not UnitCard attacker
            || !attacker.TryGetData<UnitStateData>(out var attackerState))
        {
            return Task.CompletedTask;
        }

        if (view.Resolution is not AttackDamageResolution resolution || resolution.IsRewritten)
        {
            return Task.CompletedTask; // 目标侧单命中（多源不叠加；已改写＝跳过）
        }

        // 先资格：目标方（本卡）按反击豁免判定表具有反击资格（后置项 A——豁免约束改写；无资格＝不发生反击、改写不成立）。
        if (!CounterAttackRules.CanCounterAttack(attacker, self))
        {
            return Task.CompletedTask;
        }

        // 后条件：伏击条件命中（被攻击单位实时攻击力 ＞ 攻击者实时防御力）＝改写成立。
        if (selfState.Attack > attackerState.Defense)
        {
            resolution.MarkRewritten();
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 词条逻辑组件容器（2C；主动词条逻辑的登记载体；每卡至多一份）：
/// 加载时装载（按词条清单登记序）、死亡清理时整体注销（<see cref="UnmountAll"/>）。
/// 仅词条卡（且含主动词条）挂载；无词条/纯被动词条卡不挂。
/// </summary>
public sealed class KeywordLogicData
{
    private readonly List<KeywordLogic> _logics = new();

    /// <summary>已装载的逻辑组件（登记序只读快照）。</summary>
    public IReadOnlyList<KeywordLogic> Logics => _logics;

    /// <summary>登记一个逻辑组件（装载期调用；装载序＝词条清单顺序）。</summary>
    internal void Add(KeywordLogic logic) => _logics.Add(logic);

    /// <summary>注销全部逻辑组件（死亡清理调用；后进先出；幂等）。</summary>
    internal void UnmountAll()
    {
        for (var i = _logics.Count - 1; i >= 0; i--)
        {
            _logics[i].Unmount();
        }
    }
}

/// <summary>
/// 词条运行态数据（2C；词条系统自维护的存储面；每词条卡一份、加载时挂载）。
/// 承载记名计数器等运行值（如奋战「本轮已攻次数」——E4：奋战两攻由词条自行记账、随回合恢复一并清零）；
/// 「数值由效果模块维护」——本组件即效果模块的存储，不进入指挥组件。
/// </summary>
public sealed class KeywordRuntimeData
{
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    /// <summary>读取计数器（不存在＝0）。</summary>
    public int GetCounter(string name) => _counters.TryGetValue(name, out var value) ? value : 0;

    /// <summary>置位计数器。</summary>
    public void SetCounter(string name, int value) => _counters[name] = value;

    /// <summary>递增计数器（不存在＝从 0 起）。</summary>
    public void IncrementCounter(string name) => _counters[name] = GetCounter(name) + 1;
}

/// <summary>
/// 词条规则助手（2C；读取方共享的标识查询与奋战记账语义）：
/// 被动词条由读取方按标识查询（烟幕＝攻击目标筛选；奋战＝攻击动作后的两 bool 收尾）；
/// 记账由词条侧自维护（「本轮已攻次数」存于 <see cref="KeywordRuntimeData"/>、随回合恢复清零）。
/// </summary>
public static class KeywordRules
{
    /// <summary>奋战「本轮已攻次数」计数器键（词条运行态存储内）。</summary>
    internal const string FuryAttackCountKey = "fury.attacks";

    /// <summary>是否含指定词条（标识查询；无词条组件＝false）。</summary>
    public static bool HasKeyword(CardBase card, string keyword)
        => card.TryGetData<KeywordData>(out var data) && data.Contains(keyword);

    /// <summary>
    /// 奋战记账（攻击动作成功执行后、由攻击执行段调用）：含奋战时「本轮已攻次数」+1；非奋战不记账。
    /// </summary>
    public static void RecordAttack(CardBase unit)
    {
        if (!HasKeyword(unit, KeywordIds.Fury))
        {
            return;
        }

        if (unit.TryGetData<KeywordRuntimeData>(out var runtime))
        {
            runtime.IncrementCounter(FuryAttackCountKey);
        }
    }

    /// <summary>
    /// 攻击动作后的 CanAttack 收尾取值（外层收尾读取记账）：非奋战＝false（统一清位）；
    /// 奋战＝「本轮已攻次数」＜2（首次攻击后保持可攻、二次后不可）。
    /// </summary>
    public static bool ResolveCanAttackAfterAttack(CardBase unit)
    {
        if (!HasKeyword(unit, KeywordIds.Fury))
        {
            return false;
        }

        return unit.TryGetData<KeywordRuntimeData>(out var runtime)
            && runtime.GetCounter(FuryAttackCountKey) < 2;
    }

    /// <summary>回合恢复：清零词条运行态（「本轮已攻次数」随回合恢复一并清零；无运行态组件＝无操作）。</summary>
    public static void ResetTurnCounters(CardBase unit)
    {
        if (unit.TryGetData<KeywordRuntimeData>(out var runtime))
        {
            runtime.SetCounter(FuryAttackCountKey, 0);
        }
    }
}

/// <summary>词条逻辑工厂（2C；按标识创建主动词条逻辑；被动词条＝null＝仅登记）。</summary>
internal static class KeywordLogicFactory
{
    /// <summary>按词条标识创建逻辑组件（闪击/伏击＝实例；奋战/烟幕＝null；未知标识＝null——定义期已 fail-fast）。</summary>
    internal static KeywordLogic? Create(string keyword) => keyword switch
    {
        KeywordIds.Blitz => new BlitzKeywordLogic(),
        KeywordIds.Ambush => new AmbushKeywordLogic(),
        _ => null,
    };
}
