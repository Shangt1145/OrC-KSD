using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Judicators;

/// <summary>
/// 反击资格判定器（K2；C5＝combat.counter.eligibility——反击豁免表，独立成器便于 moding）：
/// （攻击者, 目标）→ bool；原 <c>CounterAttackRules.CanCounterAttack</c> 逐字迁移（规则外提；私有类型判定一并收敛至共表）。
/// 规则（默认；豁免优先——条款冲突时以「豁免（不发生反击）」优先，仅当全部相关条款均允许时才有反击机会）：
/// ① 目标＝轰炸机 → 永不反击（绝对豁免、最高优先）；② 攻击者＝炮兵 → 不受任何反击（绝对豁免——对轰炸机条款的例外亦优先）；
/// ③ 攻击者＝轰炸机 → 不受反击、例外＝目标战斗机可反击；④ 其余 → 正常反击。
/// 类型判定经 <see cref="CombatTypeGroups"/>（交战类型组共表——单源；轰炸机/战斗机复用既有判定＋炮兵加性）；
/// 多类型＝存在性判定（任一相关条款适用即适用）。
/// 改写＝moding（combat.counter.eligibility——默认互伤区＋伏击资格全部调用点同步生效）；无状态、零注入。
/// </summary>
internal sealed class CombatCounterEligibilityJudicator : Judicator<CombatCounterEligibilityJudicator.CounterEligibilityRule>
{
    /// <summary>强类型 delegate（该判定器签名）：（攻击者, 目标）→ 目标方是否对攻击者发生反击。</summary>
    public delegate bool CounterEligibilityRule(UnitCard attacker, UnitCard target);

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(CounterEligibilityRule handler)
        => args => new object[] { handler(Unpack<UnitCard>(args, 0), Unpack<UnitCard>(args, 1)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(Evaluate)(args);

    /// <summary>
    /// 反击资格求值（原 <c>CounterAttackRules.CanCounterAttack</c> 逐字迁移——顺序/短路语义保持：豁免优先）。
    /// </summary>
    /// <param name="attacker">攻击者（含类型清单——存在性判定）。</param>
    /// <param name="target">被攻击单位（含类型清单——存在性判定）。</param>
    /// <exception cref="ArgumentNullException">attacker / target 为 null（原静态入口语义保持）。</exception>
    private static bool Evaluate(UnitCard attacker, UnitCard target)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        // ① 目标＝轰炸机 → 永不反击（绝对豁免、最高优先）
        if (CombatTypeGroups.IsBomber(target))
        {
            return false;
        }

        // ② 攻击者＝炮兵 → 不受任何反击（绝对豁免——对轰炸机条款的例外亦优先）
        if (CombatTypeGroups.IsArtillery(attacker))
        {
            return false;
        }

        // ③ 攻击者＝轰炸机 → 不受反击；例外：目标＝战斗机可反击
        if (CombatTypeGroups.IsBomber(attacker))
        {
            return CombatTypeGroups.IsFighter(target);
        }

        // ④ 其余 → 正常反击
        return true;
    }
}
