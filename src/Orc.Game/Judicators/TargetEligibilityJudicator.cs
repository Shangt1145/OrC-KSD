using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Judicators;

/// <summary>
/// 目标合法性判定器（J3 示范②；判定器机制两示范之一）：「目标/单位选择」的候选合法性规则的可改写判定服务。
/// 形态＝「持有转发」：判定器内部持有默认选择规则（<see cref="TargetCandidateRule"/> 逻辑单元——
/// 一次选择所用的候选/合法性判定逻辑，与筛选链细筛谓词同域），调用时转发执行；moding 替换后该规则整体更换（纯替换）。
/// 签名（强类型面）＝（候选引用）→ bool（该候选是否可选）。
/// 默认规则＝在场（存活）单位可选：存活引用且解引用为单位卡 <see cref="UnitCard"/>——HQ/槽位等非单位候选不可选；
/// 失效引用＝false（不可选——有效性判定归筛选域）。
/// 输入契约：候选为 null＝fail-fast（参数契约错误）。
/// 改写＝moding（允许/禁止某类目标可选——经规则整体更换实现；全局生效）；注销回退。
/// 无状态：不持有对局状态，全部经候选引用只读读取。
/// 注册途径＝既有装配期注册面（外部装配段/测试装配注册）；名称＝<see cref="JudicatorNames.TargetCandidateEligibility"/>（冻结契约）。
/// </summary>
public sealed class TargetEligibilityJudicator : Judicator<TargetEligibilityJudicator.TargetCandidateRule>
{
    /// <summary>
    /// 强类型 delegate（该判定器签名）：单候选 → 是否可选（合法性）。
    /// </summary>
    /// <param name="candidate">候选引用（不得为 null——参数契约错误，fail-fast 族）。</param>
    /// <returns>该候选是否可选。</returns>
    public delegate bool TargetCandidateRule(Ref<Entity> candidate);

    /// <summary>内部持有的选择规则（targeter 逻辑单元——默认逻辑；moding 生效时被栈顶替换逻辑整体取代）。</summary>
    private readonly TargetCandidateRule _rule;

    /// <summary>创建判定器（内部持有默认选择规则——「持有转发」形态）。</summary>
    public TargetEligibilityJudicator() => _rule = DefaultRule;

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(TargetCandidateRule handler)
        => args => new object[] { handler(Unpack<Ref<Entity>>(args, 0)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(_rule)(args);

    /// <summary>
    /// 默认选择规则：在场（存活）单位可选（失效引用＝不可选）。
    /// </summary>
    /// <exception cref="ArgumentNullException">candidate 为 null（参数契约错误——fail-fast）。</exception>
    private static bool DefaultRule(Ref<Entity> candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.IsAlive && candidate.Value is UnitCard;
    }
}
