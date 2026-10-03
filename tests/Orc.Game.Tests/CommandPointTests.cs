using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 验收锚点③：指挥点——回合开始槽 +1、点数＝槽值（含上限行为与"回合结束不保留"）。
/// 覆盖：默认序列结算数值、上限封顶（小上限路径，同时覆盖配置入口）、回合结束点数清零（槽保留）、默认上限 12。
/// </summary>
public class CommandPointTests
{
    [Fact]
    public async Task Settle_Increments_Slot_And_Sets_Points_To_Slot_Value()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();

        var first = match.Players[0];
        var second = match.Players[1];
        // 初始化（回合 1）：先手槽 0 → +1 → 1；点数＝槽值＝1
        Assert.Equal(1, first.PointSlots);
        Assert.Equal(1, first.Points);

        await match.EndTurn(); // 回合 2（后手）：后手 1/1；先手回合结束清零（槽保留）
        Assert.Equal(1, second.PointSlots);
        Assert.Equal(1, second.Points);
        Assert.Equal(1, first.PointSlots); // 槽保留
        Assert.Equal(0, first.Points); // 点数不保留（回合结束清零）

        await match.EndTurn(); // 回合 3（先手）：2/2
        Assert.Equal(2, first.PointSlots);
        Assert.Equal(2, first.Points);
        Assert.Equal(1, second.PointSlots);
        Assert.Equal(0, second.Points);
    }

    [Fact]
    public async Task Point_Slots_Cap_At_Configured_Maximum()
    {
        // 小上限路径：配置上限 2 → 多回合推进 → 断言封顶（快、确定；同时覆盖配置入口本身）
        var match = GameTestData.CreateStandardMatch(seed: 42, options: new MatchOptions { MaxPointSlots = 2 });
        await match.Initialize();

        var a = match.Players[0];
        var b = match.Players[1];

        await match.EndTurn(); // 回合 2：b = 1
        Assert.Equal(1, b.PointSlots);

        await match.EndTurn(); // 回合 3：a = 2（封顶）
        Assert.Equal(2, a.PointSlots);
        Assert.Equal(2, a.Points);

        await match.EndTurn(); // 回合 4：b = 2（封顶）
        Assert.Equal(2, b.PointSlots);
        Assert.Equal(2, b.Points);

        await match.EndTurn(); // 回合 5：a 维持 2（封顶稳定）；b 为结束方、点数清零（槽保留）
        Assert.Equal(2, a.PointSlots);
        Assert.Equal(2, a.Points);
        Assert.Equal(2, b.PointSlots); // 封顶后槽保留
        Assert.Equal(0, b.Points); // 回合结束点数清零、不保留
    }

    [Fact]
    public async Task Default_Max_Point_Slots_Is_Twelve()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();

        Assert.Equal(12, match.ResourceManager.MaxPointSlots);
    }
}
