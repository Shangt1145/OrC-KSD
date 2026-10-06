using Orc.Core;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>
/// 回合开始的槽递增判定器（E1-25；<c>resource.slot.increment</c>）：
/// （玩家）→ int；默认返回 <c>1</c>（回合开始的槽递增步长）。
/// <c>ResourceManager.SettleAsync</c> 经本判定器取值——moding 改写即「改写回合开始的递增」（全局生效、注销回退）。
/// 无状态、零注入。
/// </summary>
internal sealed class PointSlotIncrementJudicator : Judicator<PointSlotIncrementJudicator.IncrementRule>
{
    /// <summary>强类型 delegate（该判定器签名）：（玩家）→ 槽递增步长。</summary>
    public delegate int IncrementRule(Player player);

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(IncrementRule handler)
        => args => new object[] { handler(Unpack<Player>(args, 0)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(DefaultIncrement)(args);

    /// <summary>默认递增步长（1——与既有「回合开始槽 +1」行为完全一致）。</summary>
    private static int DefaultIncrement(Player player) => 1;
}
