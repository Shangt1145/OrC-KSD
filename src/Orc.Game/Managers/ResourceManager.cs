using Orc.Game.Players;

namespace Orc.Game.Managers;

/// <summary>
/// 资源管理器（指挥点领域规则服务；无独立状态——结算直接作用于玩家对象）：
/// 回合开始结算＝槽 +1（至上限封顶）→ 点数＝槽值；回合结束处理＝点数清零（槽保留——"回合结束不保留"）。
/// 上限＝配置项（默认 12；须为正整数，非法配置抛参数校验异常）。
/// 资源初始口径：玩家槽 0 / 点数 0；"第 1 回合＝1 点"由统一 +1 结算达成（无特例分支）。
/// </summary>
public sealed class ResourceManager
{
    /// <summary>指挥点上限默认值（12）。</summary>
    public const int DefaultMaxPointSlots = 12;

    /// <summary>创建资源管理器（载入配置）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">maxPointSlots 非正整数（&lt;1）。</exception>
    public ResourceManager(int maxPointSlots = DefaultMaxPointSlots)
    {
        if (maxPointSlots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPointSlots), maxPointSlots, "指挥点上限须为正整数（≥1）。");
        }

        MaxPointSlots = maxPointSlots;
    }

    /// <summary>指挥点上限（配置值；默认 12）。</summary>
    public int MaxPointSlots { get; }

    /// <summary>回合开始结算：槽 +1（至上限封顶）→ 点数＝槽值。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public void Settle(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.PointSlots = Math.Min(player.PointSlots + 1, MaxPointSlots);
        player.Points = player.PointSlots;
    }

    /// <summary>回合结束处理：点数清零（槽保留）。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public void ClearPoints(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.Points = 0;
    }
}
