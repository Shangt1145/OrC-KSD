using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Judicators;

/// <summary>
/// 伏击条件判定器（K2；C6＝combat.ambush.condition——伏击条件单源，独立成器便于 moding）：
/// （被攻击单位, 攻击者）→ bool；原 <c>AmbushKeywordComponent</c> 内联条件迁移（规则外提）。
/// 规则（默认）：被攻击单位攻击有效值 ＞ 攻击者防御有效值（严格大于——相等＝不命中）；
/// 双方有效值读取经既有修饰链「有效值」读取口（<c>GetEffectiveValue</c>——不得直读基准值）。
/// 仅承载条件比较本身（不含任何前置防护——前置保留组件侧：存活/目标侧归属/攻击者单位化/已改写跳过等）。
/// 改写＝moding（combat.ambush.condition——伏击条件判定同步生效）；无状态、零注入。
/// </summary>
internal sealed class CombatAmbushConditionJudicator : Judicator<CombatAmbushConditionJudicator.AmbushConditionRule>
{
    /// <summary>强类型 delegate（该判定器签名）：（被攻击单位, 攻击者）→ 伏击条件是否命中。</summary>
    public delegate bool AmbushConditionRule(UnitCard self, UnitCard attacker);

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(AmbushConditionRule handler)
        => args => new object[] { handler(Unpack<UnitCard>(args, 0), Unpack<UnitCard>(args, 1)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(Evaluate)(args);

    /// <summary>伏击条件求值（原组件内联比较逐字迁移）：被攻击单位攻击有效值 ＞ 攻击者防御有效值（严格大于、相等不命中）。</summary>
    private static bool Evaluate(UnitCard self, UnitCard attacker)
        => self.Modifiers.GetEffectiveValue(CardStatFields.Attack)
            > attacker.Modifiers.GetEffectiveValue(CardStatFields.Defense);
}
