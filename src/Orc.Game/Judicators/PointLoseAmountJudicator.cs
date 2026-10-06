using Orc.Core;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>
/// 失去点数的数字包裹判定器（E1-25 后续；<c>resource.point.lose</c>）：
/// （玩家，请求值）→ int；默认返回入参**原值**（恒等——「失去 n 个指挥点」的实际数字＝n）。
/// <c>ResourceManager.LosePointsAsync</c> 在落值之前经本判定器取值——
/// moding 改写即「改写失去的实际数字」（全局生效、注销回退原值）。无状态、零注入。
/// </summary>
internal sealed class PointLoseAmountJudicator : Judicator<PointLoseAmountJudicator.AmountRule>
{
    /// <summary>强类型 delegate（该判定器签名）：（玩家，请求值）→ 实际数字。</summary>
    public delegate int AmountRule(Player player, int requested);

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(AmountRule handler)
        => args => new object[] { handler(Unpack<Player>(args, 0), Unpack<int>(args, 1)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(Identity)(args);

    /// <summary>默认实现（恒等——返回入参原值）。</summary>
    private static int Identity(Player player, int requested) => requested;
}
