using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Targeting;

namespace Orc.Game.Judicators;

/// <summary>
/// 判定器选择规则（J3 示范②「新增包装类型」——选择规则接入点承载）：把判定器（条目等价句柄）包装为「选择规则」——
/// 调用方（选择请求构造方）经本类型调用判定器（句柄经注册面按名解析/注册所得取得；每次求值经判定器统一解析点取栈顶逻辑——
/// moding 改写全局生效、注销回退），以 <see cref="IsEligible"/> 承载单候选合法性求值、
/// 以 <see cref="AsFilter"/> 包装为筛选链细筛谓词（接入 <see cref="Targeter"/> 构造）。
/// 说明：本类型不改变 <see cref="TargeterManager"/> 的任何既有语义——
/// 规则接入经「候选过滤谓词」承载（新增包装类型即接入形态；对应用户场景「判定器包装选择流程」的调用侧）。
/// S2 加性（隐蔽机制的「指令效果索敌」统一接线面＋单卡例外）：
/// ①指令效果索敌统一经本包装接入判定器（<see cref="JudicatorNames.TargetCandidateEligibility"/>——默认规则
///    「隐蔽单位不可选」随判定器默认规则生效）；
/// ②<paramref name="includeCovert"/>＝**请求级覆盖**（「可以指向隐蔽单位」类单卡例外）——仅做在交互式索敌
///    （Targeter 候选流程）；语义＝该次索敌请求把「存活在场的隐蔽单位」并入候选（仅放宽「隐蔽豁免」一项：
///    判定器拒绝的其余原因——非存活/非单位——仍拒；与默认规则组合时精确等价于「默认规则去掉隐蔽剔除」）。
///    无头选靶（<c>EffectRuntime.SelectAsync</c>）不设请求级覆盖（既有 self 分支等行为保持）。
/// </summary>
public sealed class JudicatorSelectionRule
{
    private readonly JudicatorRegistration _judicator;
    private readonly bool _includeCovert;

    /// <summary>创建选择规则（包装判定器锚）。</summary>
    /// <param name="judicator">判定器条目等价句柄（注册所得或按名解析所得——同一可寻址锚）。</param>
    /// <param name="includeCovert">请求级覆盖（S2；「可以指向隐蔽单位」类单卡例外——该次索敌请求把
    /// 「存活在场的隐蔽单位」并入候选；默认 false＝遵判定器默认规则〔隐蔽单位不可选〕）。</param>
    /// <exception cref="ArgumentNullException">judicator 为 null。</exception>
    public JudicatorSelectionRule(JudicatorRegistration judicator, bool includeCovert = false)
    {
        ArgumentNullException.ThrowIfNull(judicator);
        _judicator = judicator;
        _includeCovert = includeCovert;
    }

    /// <summary>
    /// 单候选合法性求值（经判定器委托调用——调用方不直接接触判定器内部持有的规则逻辑）：
    /// 经判定器统一面调用（载荷＝<c>[candidate]</c>；输出契约＝<c>object[]{bool}</c> 单元素）。
    /// S2：判定器拒绝时，若请求级覆盖开启且候选为「存活在场的隐蔽单位」＝并入候选（true）；
    /// 其余拒绝（非存活/非单位等）照常 false。
    /// </summary>
    /// <param name="candidate">候选引用（不得为 null——参数契约错误，fail-fast 族）。</param>
    /// <returns>该候选是否可选。</returns>
    /// <exception cref="ArgumentNullException">candidate 为 null。</exception>
    /// <exception cref="InvalidOperationException">判定器输出契约不符（调用链错误——fail-fast 族）。</exception>
    public bool IsEligible(Ref<Entity> candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var raw = _judicator.Invoke(new object[] { candidate });
        if (raw is not { Length: 1 } || raw[0] is not bool allowed)
        {
            throw new InvalidOperationException(
                $"选择规则判定器 '{_judicator.Name}' 输出契约不符：期望 object[]{{bool}}（1 元素），实际 {Describe(raw)}——fail-fast。");
        }

        if (allowed)
        {
            return true;
        }

        // S2 请求级覆盖（「可以指向隐蔽单位」）：「存活在场的隐蔽单位」并入候选——仅放宽「隐蔽豁免」一项。
        return _includeCovert
            && candidate.IsAlive
            && candidate.Value is UnitCard unit
            && CovertRules.IsCovert(unit);
    }

    /// <summary>包装为逐项谓词（组装选择器候选时使用——候选由后端构造、Q19＝a）。</summary>
    public Func<Ref<Entity>, bool> AsPredicate() => IsEligible;

    /// <summary>按规则过滤候选（组装选择器参数时使用）。</summary>
    /// <param name="candidates">候选引用集。</param>
    /// <returns>通过规则的候选（保持原顺序）。</returns>
    public IReadOnlyList<Ref<Entity>> Filter(IReadOnlyList<Ref<Entity>> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var list = new List<Ref<Entity>>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (IsEligible(candidate))
            {
                list.Add(candidate);
            }
        }

        return list.ToArray();
    }

    private static string Describe(object[]? raw)
        => raw is null
            ? "null（无输出）"
            : $"长度 {raw.Length}（首元素类型 {(raw.Length == 0 ? "—" : raw[0]?.GetType().Name ?? "null")}）";
}
