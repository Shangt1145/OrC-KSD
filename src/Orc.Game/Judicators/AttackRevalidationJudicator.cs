using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>
/// 攻击复验判定器（J2；单位攻击验证点专属——逐点专属判定器之一）：
/// 执行前兜底复验的验证承载（原 <c>CommandManager.RevalidateAttack</c> 逐字迁移）。
/// 规则：终局后拒绝；操作角色引用（攻击者/目标）有效性、当前行动方、可攻标记、被压制、行动费（有效值）、
/// 目标合法性（单位/HQ——守护/轰炸机拦截/范围矩阵；转发至攻击规则读取面——与可用性侧单源）。
/// 输入：refs（触发数据第一层引用收集——本验证点：攻击者/目标位于前位；尾部可携带触发者卡引用、不参与复验）
/// ＋subject（被判定对象＝攻击者引用——显式、Ref 形态）。
/// 装配期注入对局级只读设施（对局加载时加载、生命随对局、仅只读使用）：
/// 生命周期（终局门禁）、当前行动方提供器、攻击目标合法性查询（转发——与可用性侧单源）。
/// 无状态：不持有跨调用可变状态；改写＝moding（攻击复验改写——与移动复验相互独立）。
/// </summary>
internal sealed class AttackRevalidationJudicator : ValidationJudicator
{
    private readonly MatchLifecycle? _lifecycle;
    private readonly Func<Player?> _currentPlayerProvider;
    private readonly Func<UnitCard, Ref<Entity>, bool> _isAttackTargetLegal;

    /// <summary>创建攻击复验判定器（装配期注入对局级只读设施引用）。</summary>
    /// <exception cref="ArgumentNullException">currentPlayerProvider / isAttackTargetLegal 为 null。</exception>
    internal AttackRevalidationJudicator(
        MatchLifecycle? lifecycle,
        Func<Player?> currentPlayerProvider,
        Func<UnitCard, Ref<Entity>, bool> isAttackTargetLegal)
    {
        ArgumentNullException.ThrowIfNull(currentPlayerProvider);
        ArgumentNullException.ThrowIfNull(isAttackTargetLegal);

        _lifecycle = lifecycle;
        _currentPlayerProvider = currentPlayerProvider;
        _isAttackTargetLegal = isAttackTargetLegal;
    }

    /// <inheritdoc />
    protected override ValidationVerdict ValidateLogic(IReadOnlyList<Ref<Entity>> refs, Ref<Entity>? subject)
    {
        // X1：refs＝触发数据第一层引用收集（插入序）——操作角色引用（Attacker/Target）位于前位，
        // 效果引发情形尾部可携带「触发者卡牌」引用（TriggerCard；不参与复验——去重后计数允许多余项）。
        if (refs.Count < 2)
        {
            return ValidationVerdict.Invalid();
        }

        if (_lifecycle?.IsEnded == true)
        {
            return ValidationVerdict.Invalid(); // 终局后攻击流程拒绝（含触发器级对外入口；零副作用、状态不推进）
        }

        if (subject is null || !subject.IsAlive || subject.Value is not UnitCard attacker)
        {
            return ValidationVerdict.Invalid();
        }

        var targetRef = refs[1];
        if (!targetRef.IsAlive)
        {
            return ValidationVerdict.Invalid();
        }

        var state = attacker.GetData<UnitStateData>();
        var command = attacker.GetData<CommandData>();
        var owner = attacker.Owner;
        var current = _currentPlayerProvider();

        if (current is null || owner is null || !ReferenceEquals(owner, current))
        {
            return ValidationVerdict.Invalid();
        }

        if (state.IsDestroyed || !command.CanAttack)
        {
            return ValidationVerdict.Invalid();
        }

        // A2：被压制（不能移动或攻击——复验与可用性同源）
        if (KeywordRules.HasKeyword(attacker, KeywordIds.Suppressed))
        {
            return ValidationVerdict.Invalid();
        }

        // W2b：行动费复验读「有效值」（与可用性/扣费同源——读取面统一）
        if (owner.Points < attacker.Modifiers.GetEffectiveValue(CardStatFields.OperateCost))
        {
            return ValidationVerdict.Invalid();
        }

        return _isAttackTargetLegal(attacker, targetRef) ? ValidationVerdict.Valid : ValidationVerdict.Invalid();
    }
}
