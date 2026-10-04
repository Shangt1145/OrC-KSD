using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Triggers;

/// <summary>
/// 费用校验触发器（2B；单位/指令的预打出触发器与打出触发器共用）：
/// 合法性验证＝指挥点检查（复用引擎合法性验证机制——覆写 <see cref="Trigger{TView}.Validate"/>）。
/// 规则：卡主已加载（Owner 非空）且点数 ≥ 该卡指挥点花费（花费组件 DeployCost）＝合法；否则拒绝（「仅本次取消」语义）。
/// 预打出开始（外部调用 Validate / 触发时固定先调用）与打出段复验（触发时固定先调用）经同一判定源。
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
        return owner is not null && owner.Points >= _card.GetData<CommandPointCostData>().DeployCost;
    }
}
