using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Judicators;

/// <summary>
/// 被守护攻击资格判定器（K1；C3＝combat.guard.eligibility——交战合法性判定族之子规则之一，独立成器便于单点 moding）：
/// （攻击者）→ bool；原 <c>CommandManager.CountsAsBombard</c> 语义（资格组＝炮/轰；不含战斗机——类型组与 C2 共表，经 <see cref="CombatTypeGroups"/>）。
/// 承载「攻击者资格」；「目标是否被守护」＝维护型状态读取（仍由组合器编排环节经既有查询面消费——不在本器内）。
/// 改写＝moding（单点改写经条目栈顶替换）；组合器（combat.target.legal）编排经条目句柄消费本判定器——改写穿透编排。
/// </summary>
internal sealed class CombatGuardEligibilityJudicator : Judicator<CombatGuardEligibilityJudicator.GuardEligibilityRule>
{
    /// <summary>强类型 delegate（该判定器签名）：（攻击者）→ 是否属资格组（炮/轰）。</summary>
    public delegate bool GuardEligibilityRule(UnitCard attacker);

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(GuardEligibilityRule handler)
        => args => new object[] { handler(Unpack<UnitCard>(args, 0)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(Evaluate)(args);

    /// <summary>资格组求值（炮/轰组——存在性判定；原 <c>CountsAsBombard</c> 语义逐字保持）。</summary>
    private static bool Evaluate(UnitCard attacker) => CombatTypeGroups.CountsAsBombard(attacker);
}
