using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Triggers;

/// <summary>
/// 费用校验触发器（2B；单位/指令的预打出触发器与打出触发器共用）：
/// 合法性验证＝指挥点检查（复用引擎合法性验证机制——覆写 <see cref="Trigger{TView}.Validate"/>）。
/// 规则：卡主已加载（Owner 非空）且点数 ≥ 该卡「有效部署费」（W3-2 G5：修饰机制链输出——
/// 无修饰时＝花费组件基准）＝合法；否则拒绝（「仅本次取消」语义）。
/// 预打出开始（外部调用 Validate / 触发时固定先调用）与打出段复验（触发时固定先调用）经同一判定源
/// （同读有效值——结算与复验同口径）。
/// 契约：只读、无副作用（沿用 Validate 契约）。
/// </summary>
public sealed class CostCheckTrigger : Trigger<CardTriggerView>
{
    private readonly CardBase _card;

    /// <summary>创建费用校验触发器（绑定所属卡牌；验证读取其 Owner 与花费组件）。</summary>
    /// <param name="name">触发器名称（如「预打出触发器」/「打出触发器」）。</param>
    /// <param name="card">所属卡牌。</param>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    public CostCheckTrigger(string name, CardBase card)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
    }

    /// <inheritdoc />
    public override bool Validate(IReadOnlyList<Ref<Entity>> refs)
    {
        var owner = _card.Owner;
        // W3-2 G5：部署费读「有效值」（修饰贡献叠加后的链输出——无修饰时＝基准；读取面统一、防旁路直读）
        return owner is not null && owner.Points >= _card.Modifiers.GetEffectiveValue(CardStatFields.DeployCost);
    }
}
