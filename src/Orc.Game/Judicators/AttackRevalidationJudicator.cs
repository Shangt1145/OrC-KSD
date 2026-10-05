using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>
/// 攻击复验判定器（J2；单位攻击验证点专属——逐点专属判定器之一；K3 改造：leg 共享消重）：
/// 执行前兜底复验的验证承载（原 <c>CommandManager.RevalidateAttack</c> 逐字迁移）。
/// 规则：终局后拒绝；操作角色引用（攻击者/目标）有效性、leg 资格（K3：owner==current／!destroyed／
/// CanAttack／被压制／行动费——经共享 leg 条目单源取用）、目标合法性（单位/HQ——守护/轰炸机拦截/范围矩阵；
/// 转发至攻击规则读取面——与可用性侧单源）。
/// 输入：refs（触发数据第一层引用收集——本验证点：攻击者/目标位于前位；尾部可携带触发者卡引用、不参与复验）
/// ＋subject（被判定对象＝攻击者引用——显式、Ref 形态）。
/// 装配期注入对局级只读设施（对局加载时加载、生命随对局、仅只读使用）：
/// 生命周期（终局门禁）、leg 资格通道（K3 共享条目——每次调用经统一解析点）、攻击目标合法性查询（转发——与可用性侧单源）。
/// 无状态：不持有跨调用可变状态；改写＝moding（攻击复验改写——与移动复验相互独立；attack leg 条目改写＝可用性与复验同步）。
/// </summary>
internal sealed class AttackRevalidationJudicator : ValidationJudicator
{
    private readonly MatchLifecycle? _lifecycle;
    private readonly Func<UnitCard, LegEligibilityFailure?> _attackLegEligibility;
    private readonly Func<UnitCard, Ref<Entity>, bool> _isAttackTargetLegal;

    /// <summary>创建攻击复验判定器（装配期注入对局级只读设施引用）。</summary>
    /// <exception cref="ArgumentNullException">attackLegEligibility / isAttackTargetLegal 为 null。</exception>
    internal AttackRevalidationJudicator(
        MatchLifecycle? lifecycle,
        Func<UnitCard, LegEligibilityFailure?> attackLegEligibility,
        Func<UnitCard, Ref<Entity>, bool> isAttackTargetLegal)
    {
        ArgumentNullException.ThrowIfNull(attackLegEligibility);
        ArgumentNullException.ThrowIfNull(isAttackTargetLegal);

        _lifecycle = lifecycle;
        _attackLegEligibility = attackLegEligibility;
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

        // K3：leg 资格（共享条件源——原内联的 owner/destroyed/can/suppressed/cost 检查迁至 leg 条目；
        // 复验按 bool 语义消费：非 null＝Invalid；编排保持——leg 整体判定位于 refs/终局/subject 检查之后）。
        if (_attackLegEligibility(attacker) is not null)
        {
            return ValidationVerdict.Invalid();
        }

        return _isAttackTargetLegal(attacker, targetRef) ? ValidationVerdict.Valid : ValidationVerdict.Invalid();
    }
}
