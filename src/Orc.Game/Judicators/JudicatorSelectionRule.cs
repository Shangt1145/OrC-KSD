using Orc.Core;
using Orc.Game.Targeting;

namespace Orc.Game.Judicators;

/// <summary>
/// 判定器选择规则（J3 示范②「新增包装类型」——选择规则接入点承载）：把判定器（条目等价句柄）包装为「选择规则」——
/// 调用方（选择请求构造方）经本类型调用判定器（句柄经注册面按名解析/注册所得取得；每次求值经判定器统一解析点取栈顶逻辑——
/// moding 改写全局生效、注销回退），以 <see cref="IsEligible"/> 承载单候选合法性求值、
/// 以 <see cref="AsFilter"/> 包装为筛选链细筛谓词（接入 <see cref="Targeter"/> 构造）。
/// 说明：本类型不改变 <see cref="Targeter"/>/<see cref="TargetFilter"/>/<see cref="TargeterManager"/> 的任何既有语义——
/// 规则接入经既有「筛选器由调用方构造」面承载（新增包装类型即接入形态；对应用户场景「判定器包装 Targeter」的调用侧）。
/// </summary>
public sealed class JudicatorSelectionRule
{
    private readonly JudicatorRegistration _judicator;

    /// <summary>创建选择规则（包装判定器锚）。</summary>
    /// <param name="judicator">判定器条目等价句柄（注册所得或按名解析所得——同一可寻址锚）。</param>
    /// <exception cref="ArgumentNullException">judicator 为 null。</exception>
    public JudicatorSelectionRule(JudicatorRegistration judicator)
    {
        ArgumentNullException.ThrowIfNull(judicator);
        _judicator = judicator;
    }

    /// <summary>
    /// 单候选合法性求值（经判定器委托调用——调用方不直接接触判定器内部持有的规则逻辑）：
    /// 经判定器统一面调用（载荷＝<c>[candidate]</c>；输出契约＝<c>object[]{bool}</c> 单元素）。
    /// </summary>
    /// <param name="candidate">候选引用（不得为 null——参数契约错误，fail-fast 族）。</param>
    /// <returns>该候选是否可选。</returns>
    /// <exception cref="ArgumentNullException">candidate 为 null。</exception>
    /// <exception cref="InvalidOperationException">判定器输出契约不符（调用链错误——fail-fast 族）。</exception>
    public bool IsEligible(Ref<Entity> candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var raw = _judicator.Invoke(new object[] { candidate });
        if (raw is { Length: 1 } && raw[0] is bool allowed)
        {
            return allowed;
        }

        throw new InvalidOperationException(
            $"选择规则判定器 '{_judicator.Name}' 输出契约不符：期望 object[]{{bool}}（1 元素），实际 {Describe(raw)}——fail-fast。");
    }

    /// <summary>
    /// 包装为筛选器（细筛谓词接入——选择流程筛选环节的规则求值经判定器进行）：
    /// 返回的 <see cref="TargetFilter"/> 仅含细筛（选择规则为逐项谓词形态、粗筛缺省＝全通过）。
    /// </summary>
    public TargetFilter AsFilter() => new(fineFilter: IsEligible);

    private static string Describe(object[]? raw)
        => raw is null
            ? "null（无输出）"
            : $"长度 {raw.Length}（首元素类型 {(raw.Length == 0 ? "—" : raw[0]?.GetType().Name ?? "null")}）";
}
