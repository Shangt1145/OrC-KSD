using Orc.Game.Players;

namespace Orc.Game.Managers;

/// <summary>
/// 资源管理器（指挥点领域规则服务；无独立状态——结算直接作用于玩家对象）：
/// 回合开始结算＝槽 +1（至上限封顶）→ 点数＝槽值（设为）；点数在回合结束时刻与敌方回合内保留（X3：回合结束不清零）。
/// 受控加值面＝AddPoints（"获得 N 点"类效果的最小先行；不钳制到槽——回合开始的"设为槽值"是唯一重设点）。
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

    /// <summary>
    /// 点数加值（受控写入面；"获得 N 点"类效果的最小先行）：点数 += amount（不钳制到槽——可超槽）。
    /// 硬性校验：玩家 null 拒绝、amount 非正拒绝；防御性溢出防护（加值后超 int 上限＝拒绝）。
    /// 不引入点数变化的更新/事件通知（本波最小面）。
    /// </summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">amount 非正（≤0）；或加值后超出 int 上限。</exception>
    public void AddPoints(Player player, int amount)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "加点须为严格正数（≤0 被拒绝——加值语义严格为正）。");
        }

        if (player.Points > int.MaxValue - amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "加点后点数超出 int 上限（防御性拒绝、不产生溢出写）。");
        }

        player.Points += amount;
    }
}
