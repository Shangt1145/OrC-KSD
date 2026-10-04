using Orc.Game;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// UI 消费桥接的游戏层验收（S6）：对局级动作入口（Initialize / EndTurn）各自产出一段；
/// 初始化＝一次大动作（内部洗切/加载/起始回合合并为单段）。
/// </summary>
public class SegmentBridgeGameTests
{
    [Fact]
    public async Task Initialize_Produces_Single_Merged_Segment()
    {
        var match = GameTestData.CreateStandardMatch();

        await match.Initialize();

        var segment = Assert.Single(match.Engine.TakeSegments()); // 内部子动作不单独成段
        Assert.Contains(segment.Entries, e => e.Keywords.Contains(GameUpdates.TurnStart));
        Assert.Contains(segment.Entries, e => e.Keywords.Contains(GameUpdates.CardLoad));
    }

    [Fact]
    public async Task EndTurn_Produces_Segment()
    {
        var match = GameTestData.CreateStandardMatch();
        await match.Initialize();
        match.Engine.TakeSegments(); // 清空初始化段

        await match.EndTurn();

        var segment = Assert.Single(match.Engine.TakeSegments());
        Assert.Contains(segment.Entries, e => e.Keywords.Contains(GameUpdates.TurnEnd));
    }
}
