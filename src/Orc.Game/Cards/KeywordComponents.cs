using Orc.Cards;
using Orc.Core;
using Orc.Game.Commanding;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 生产内建四词条的组件实现（2C-A1 迁移；行为与迁移前逐条保真——见验收汇报「随改清单」）：
// 闪击/伏击/奋战＝能力型（运行逻辑组件的自含行为）；烟幕＝标记型（轻量组件 PlainKeywordComponent——
// 仅数据、无主动逻辑，消费方经词条管理组件查询）。四者均走同一组件基类与同一挂载/卸载机制。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 闪击（能力型）词条组件：部署链收尾（扣费后）执行——允许单位可移动和攻击（覆盖部署初值 false/false）。
/// 「加载时完成预备、运行时于部署链收尾执行」；仅部署路径生效（加入路径不置位——加入链不走部署收尾）。
/// </summary>
public sealed class BlitzKeywordComponent : KeywordComponent
{
    /// <summary>创建闪击词条组件。</summary>
    public BlitzKeywordComponent()
        : base(KeywordIds.Blitz)
    {
    }

    /// <inheritdoc />
    internal override Task OnDeployChainFinalizedAsync(Card card, CancellationToken ct)
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
/// 奋战（能力型）词条组件：保留运行逻辑承载（攻击记账 / 收尾判定 / 回合清零——非纯标记、不退化为「标记型＋外部读取」）。
/// 自含记账数据（「本轮已攻次数」——不保留通用字符串键计数器形态）：首次攻击后仍可攻、二次后不可；
/// 随回合恢复一并清零。读取方（攻击执行段/收尾/回合恢复）经 <see cref="KeywordRules"/> 静态助手面调用（转发本组件）。
/// </summary>
public sealed class FuryKeywordComponent : KeywordComponent
{
    private int _attackCount;

    /// <summary>创建奋战词条组件。</summary>
    public FuryKeywordComponent()
        : base(KeywordIds.Fury)
    {
    }

    /// <summary>攻击记账（攻击动作成功执行后、由攻击执行段调用）：「本轮已攻次数」+1。</summary>
    internal void RecordAttack() => _attackCount++;

    /// <summary>攻击动作后的 CanAttack 收尾取值：「本轮已攻次数」＜2（首次攻击后保持可攻、二次后不可）。</summary>
    internal bool CanAttackAfterAttack() => _attackCount < 2;

    /// <summary>回合恢复：清零「本轮已攻次数」（随回合恢复一并清零）。</summary>
    internal void ResetTurnCounters() => _attackCount = 0;
}

/// <summary>
/// 伏击（能力型）词条组件：装载时向「造成攻击伤害」触发器注册改写逻辑；卸载（移除/死亡注销）时注销。
/// 改写判定（先资格、后条件）：被攻击单位（目标侧，＝本卡）含伏击 ∧ 目标方按反击豁免判定表具有反击资格
/// （豁免约束改写——无资格＝不发生反击、改写不成立、按表单方结算）∧ 条件命中（被攻击单位攻击力有效值 ＞ 攻击者防御力有效值，
/// 互扣前；读修饰机制缓存有效值）→ 置改写标志（攻击者死亡、被攻击者不受伤）；
/// 资格通过但条件不成立＝正常基础互伤。不区分攻击者类型；HQ 攻击不走该流程。
/// </summary>
public sealed class AmbushKeywordComponent : KeywordComponent
{
    private Trigger<AttackDamageTriggerView>? _trigger;
    private TriggerRegistration? _registration;
    private Card? _card;

    /// <summary>创建伏击词条组件。</summary>
    public AmbushKeywordComponent()
        : base(KeywordIds.Ambush)
    {
    }

    /// <inheritdoc />
    internal override void Mount(Card card, KeywordLoadContext? context)
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
            || !attacker.TryGetData<UnitStateData>(out _)) // 攻击者须已单位化（未单位化＝不改写、不抛错）
        {
            return Task.CompletedTask;
        }

        if (view.Resolution is not AttackDamageResolution resolution || resolution.IsRewritten)
        {
            return Task.CompletedTask; // 目标侧单命中（多源不叠加；已改写＝跳过）
        }

        // 先资格：目标方（本卡）按反击豁免判定表具有反击资格（豁免约束改写——无资格＝不发生反击、改写不成立）。
        if (!CounterAttackRules.CanCounterAttack(attacker, self))
        {
            return Task.CompletedTask;
        }

        // 后条件：伏击条件命中（被攻击单位攻击力有效值 ＞ 攻击者防御力有效值——读修饰机制缓存有效值）＝改写成立。
        if (self.Modifiers.GetEffectiveValue(CardStatFields.Attack)
            > attacker.Modifiers.GetEffectiveValue(CardStatFields.Defense))
        {
            resolution.MarkRewritten();
        }

        return Task.CompletedTask;
    }
}
