using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2A 验收锚点⑥（战线槽位模型）：三线槽位化（容量 4/5/4）；HQ 初始占位（支援线槽 0、占容量格；前线不占）；
/// 邻位动态计算（含 HQ 被占位、重叠去重、索引升序稳定、端点越界侧跳过）；槽位读面（索引/占用者/是否空；类型可判）；
/// 越界明确错误；「整线空槽枚举」经槽位序列读面派生（不单列）。
/// </summary>
public class SlotModelTests
{
    private static UnitCard CreateUnit(LogicEngine engine)
        => new(engine, new CardDefinition("单位", 1, 2, 3, 4, faction: Faction.Germany, rarity: Rarity.Standard));

    [Fact]
    public void Slot_Read_Surface_Exposes_Index_Occupant_And_Emptiness()
    {
        var line = new BattleLine(4);
        Assert.Equal(4, line.Count);
        Assert.Equal(4, line.Capacity);

        for (var i = 0; i < line.Count; i++)
        {
            Assert.Equal(i, line[i].Index);
            Assert.Null(line[i].Occupant);
            Assert.True(line[i].IsEmpty);
        }
    }

    [Fact]
    public void Place_And_Clear_Are_Guarded_And_Occupant_Type_Is_Distinguishable()
    {
        var line = new BattleLine(2);
        var slot = line[0];
        var unit = CreateUnit(new LogicEngine());

        slot.Place(unit);
        Assert.False(slot.IsEmpty);
        var occupant = slot.Occupant;
        Assert.IsType<UnitCard>(occupant); // 占用者「单位」＝对象类型可判（单位卡实例）

        Assert.Throws<InvalidOperationException>(() => slot.Place(new object())); // 已占＝明确拒绝（放置仅空槽）

        slot.Clear();
        Assert.True(slot.IsEmpty);
        slot.Clear(); // 幂等：已空＝无操作、不抛错

        Assert.Throws<ArgumentNullException>(() => slot.Place(null!)); // null 占用者被拒绝
    }

    [Fact]
    public void Index_Out_Of_Range_And_Invalid_Capacity_Are_Explicit_Errors()
    {
        var line = new BattleLine(4);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = line[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = line[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleLine(0));
    }

    [Fact]
    public void Adjacent_Candidates_Of_Empty_Line_Are_Empty()
    {
        var line = new BattleLine(5);
        Assert.Empty(line.GetAdjacentEmptySlots()); // 无被占槽位＝无候选
    }

    [Fact]
    public void Adjacent_Candidates_Extend_From_Single_Occupant_And_Skip_Out_Of_Range()
    {
        var line = new BattleLine(3);
        line[0].Place(new object()); // 端点被占：越界侧（左）跳过
        Assert.Equal(new[] { 1 }, line.GetAdjacentEmptySlots().Select(s => s.Index));
    }

    [Fact]
    public void Adjacent_Candidates_Merge_Overlaps_And_Keep_Ascending_Order()
    {
        var line = new BattleLine(4);
        line[0].Place(new object());
        line[2].Place(new object());
        // 槽 1 同时为 0 的右邻与 2 的左邻（重叠去重、只出现一次）；槽 3 为 2 的右邻；结果按索引升序
        Assert.Equal(new[] { 1, 3 }, line.GetAdjacentEmptySlots().Select(s => s.Index));
    }

    [Fact]
    public async Task Adjacent_Candidates_Include_HQ_Occupancy_And_All_Three_Lines_Are_Slotted()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();

        var lineA = match.Battlefield.PlayerASupportLine;
        var frontLine = match.Battlefield.FrontLine;
        var lineB = match.Battlefield.PlayerBSupportLine;

        // 三线槽位化：容量 4/5/4
        Assert.Equal(4, lineA.Capacity);
        Assert.Equal(5, frontLine.Capacity);
        Assert.Equal(4, lineB.Capacity);

        // HQ 占位：支援线槽 0＝对应玩家的总部实体（占容量格——恰 1 格被占、余 3 格空）；前线 0 占位
        Assert.Same(match.Players[0].Hq, lineA[0].Occupant);
        Assert.Same(match.Players[1].Hq, lineB[0].Occupant);
        Assert.Single(lineA, slot => !slot.IsEmpty);
        Assert.Single(lineB, slot => !slot.IsEmpty);
        Assert.All(frontLine, slot => Assert.True(slot.IsEmpty));

        // 邻位候选（含 HQ 被占位）：支援线＝[1]；前线（无被占）＝[]
        Assert.Equal(new[] { 1 }, lineA.GetAdjacentEmptySlots().Select(s => s.Index));
        Assert.Equal(new[] { 1 }, lineB.GetAdjacentEmptySlots().Select(s => s.Index));
        Assert.Empty(frontLine.GetAdjacentEmptySlots());
    }

    [Fact]
    public void Empty_Slot_Enumeration_Is_Derivable_From_Slot_Sequence()
    {
        var line = new BattleLine(4);
        line[0].Place(new object());
        // 整线空槽枚举＝槽位序列读面派生（不单列）
        var emptyIndexes = line.Where(slot => slot.IsEmpty).Select(slot => slot.Index).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, emptyIndexes);
    }
}
