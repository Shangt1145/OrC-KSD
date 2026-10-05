using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Judicators;

/// <summary>
/// 费用检查判定器（J2；费用验证点专属——逐点专属判定器之一）：预打出/打出共用的合法性验证承载。
/// 规则：卡主已加载（Owner 非空）且点数 ≥ 该卡「有效部署费」（W3-2 G5：修饰机制链输出——无修饰时＝花费组件基准）
/// ＝合法；否则拒绝（无类别——费用验证点的拒绝原因不参与入口映射区分）。
/// 输入：refs（沿用收集面——本验证点不消费）＋subject（被判定卡牌引用——显式、Ref 形态）。
/// 无状态：零装配期注入（全部经被判定对象只读读取）；改写＝moding（费用验证改写——其全部引用点生效）。
/// </summary>
internal sealed class CostCheckJudicator : ValidationJudicator
{
    /// <inheritdoc />
    protected override ValidationVerdict ValidateLogic(IReadOnlyList<Ref<Entity>> refs, Ref<Entity>? subject)
    {
        if (subject is null || !subject.IsAlive || subject.Value is not CardBase card)
        {
            // 载体缺失/不符＝契约不符（fail-fast 族；运行期由触发路径契约兜底承接）——绑定方固定提供所属卡引用，运行期不可达。
            throw new ArgumentException("费用检查判定器载荷不符：被判定对象须为存活卡牌引用（fail-fast）。");
        }

        var owner = card.Owner;
        // W3-2 G5：部署费读「有效值」（修饰贡献叠加后的链输出——无修饰时＝基准；读取面统一、防旁路直读）
        return owner is not null && owner.Points >= card.Modifiers.GetEffectiveValue(CardStatFields.DeployCost)
            ? ValidationVerdict.Valid
            : ValidationVerdict.Invalid();
    }
}
