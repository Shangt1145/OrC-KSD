using Orc.Core;
using Orc.Game.Cards;

namespace Orc.Game.Triggers;

/// <summary>
/// 使用反制触发器（2B；反制唯一触发器；单入口状态翻转的验证承载）：
/// 合法性验证＝反制使用检查（复用引擎合法性验证机制——覆写 <see cref="Trigger{TView}.Validate"/>，
/// 判定源与入口一致：<see cref="CounterCard.EvaluateUse"/>）：
/// ① 仅己方回合（该卡所属玩家是当前行动方；回合上下文经 <see cref="CardBase.TurnPlayerProvider"/> 读取）；
/// ② 未激活时另检查指挥点（已激活＝取消流程、无条件通过）。
/// 未激活 ⇒ 激活流程；已激活 ⇒ 取消流程（由默认事件按当前激活状态分派）。
/// 「仅己方回合」违反 / 激活点数不足＝拒绝（仅本次取消）；激活时的验证即唯一验证点（无复验概念）。
/// </summary>
public sealed class CounterUseTrigger : Trigger<CardTriggerView>
{
    private readonly CounterCard _card;

    /// <summary>创建使用反制触发器（绑定所属反制卡；验证读取其归属、激活状态与花费组件）。</summary>
    /// <param name="card">所属反制卡。</param>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    public CounterUseTrigger(CounterCard card)
        : base("使用反制的触发器")
    {
        ArgumentNullException.ThrowIfNull(card);
        _card = card;
    }

    /// <inheritdoc />
    public override bool Validate(IReadOnlyList<Ref<Entity>> refs)
        => _card.EvaluateUse(_card.TurnPlayerProvider?.Invoke()) is null;
}
