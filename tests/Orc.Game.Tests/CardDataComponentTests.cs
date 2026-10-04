using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2A 验收锚点④（数据组件全集）：六组件就位；拆分实施（指挥点花费单列＋对战三值；四合一退役、无双真源）；
/// 字段与基础读写可测（含实时值读写、词条增删查；初始值：位置 null／已毁 false／指挥 false·false／激活 false）。
/// </summary>
public class CardDataComponentTests
{
    private static UnitCard CreateUnitCard(LogicEngine? engine = null)
        => new(engine ?? new LogicEngine(), new CardDefinition("单位", 1, 2, 3, 4, CardCategory.Unit));

    [Fact]
    public void Unit_Instantiate_Attaches_Both_Split_Components_From_Definition()
    {
        var card = CreateUnitCard();

        var cost = card.GetData<CommandPointCostData>(); // 指挥点花费（部署费、单列；全类别）
        Assert.Equal(1, cost.DeployCost);
        var stats = card.GetData<BattleStatsData>(); // 对战三值（初始值；单位卡专属）
        Assert.Equal(2, stats.OperateCost);
        Assert.Equal(3, stats.Attack);
        Assert.Equal(4, stats.Defense);
    }

    [Fact]
    public void Category_Specific_Assembly_Commands_And_Counters_Carry_No_Battle_Stats()
    {
        var library = new CardLibrary(new LogicEngine());
        library.Register("c1", new CardDefinition("指令", 5, 0, 0, 0, CardCategory.Command));
        library.Register("x1", new CardDefinition("反制", 2, 0, 0, 0, CardCategory.Counter));

        // 指令＝花费（无其他数据组件）
        var command = library.Instantiate("c1");
        Assert.Equal(5, command.GetData<CommandPointCostData>().DeployCost);
        Assert.Throws<KeyNotFoundException>(() => command.GetData<BattleStatsData>());

        // 反制＝花费＋激活状态组件（2B 挂载落实：实例化路径装配；旧「挂载后置（测试内构造挂载）」→ 新「组件在位、初始未激活」）
        var counter = library.Instantiate("x1");
        Assert.Equal(2, counter.GetData<CommandPointCostData>().DeployCost);
        Assert.Throws<KeyNotFoundException>(() => counter.GetData<BattleStatsData>());
        Assert.False(counter.GetData<CounterActivationData>().IsActive);
    }

    [Fact]
    public void UnitStateData_Defaults_And_ReadWrite()
    {
        var state = new UnitStateData();

        // 初始值：位置 null／已毁 false／类型列表空（0 个合法）／实时值 0
        Assert.Null(state.Position);
        Assert.False(state.IsDestroyed);
        Assert.Empty(state.UnitTypes);
        Assert.Equal(0, state.OperateCost);
        Assert.Equal(0, state.Attack);
        Assert.Equal(0, state.Defense);

        // 基础读写：三实时值、位置（Slot 引用）、类型列表（0 个/多个均合法）
        var line = new BattleLine(4);
        state.Position = line[1];
        state.IsDestroyed = true;
        state.OperateCost = 5;
        state.Attack = 6;
        state.Defense = 7;
        state.UnitTypes.Add(UnitType.Infantry);
        state.UnitTypes.Add(UnitType.Tank);

        Assert.Same(line[1], state.Position);
        Assert.True(state.IsDestroyed);
        Assert.Equal(5, state.OperateCost);
        Assert.Equal(6, state.Attack);
        Assert.Equal(7, state.Defense);
        Assert.Equal(new[] { UnitType.Infantry, UnitType.Tank }, state.UnitTypes);
    }

    [Fact]
    public void UnitStateData_CreateInitial_Copies_From_BattleStats_And_Benchmark_Stays_Unchanged()
    {
        var stats = new BattleStatsData(2, 4, 6);

        var state = UnitStateData.CreateInitial(stats);
        // 初始＝对战组件值（一次性复制契约）
        Assert.Equal(2, state.OperateCost);
        Assert.Equal(4, state.Attack);
        Assert.Equal(6, state.Defense);

        // 效果/词条修改实时值不影响只读基准
        state.Attack += 3;
        Assert.Equal(7, state.Attack);
        Assert.Equal(4, stats.Attack);
    }

    [Fact]
    public void CommandData_Defaults_And_ReadWrite()
    {
        var command = new CommandData();
        Assert.False(command.CanMove); // 初始 false/false
        Assert.False(command.CanAttack);

        command.CanMove = true;
        command.CanAttack = true;
        Assert.True(command.CanMove);
        Assert.True(command.CanAttack);
    }

    [Fact]
    public void KeywordData_Add_Remove_Contains_Are_Idempotent_And_Ordered()
    {
        var keywords = new KeywordData();
        Assert.Empty(keywords.Keywords);

        Assert.True(keywords.Add("闪击")); // 登记
        Assert.False(keywords.Add("闪击")); // 幂等：重复登记＝false 无操作
        Assert.True(keywords.Add("奋战"));
        Assert.Equal(new[] { "闪击", "奋战" }, keywords.Keywords); // 登记序

        Assert.True(keywords.Contains("闪击"));
        Assert.False(keywords.Contains("烟幕"));
        Assert.False(keywords.Contains(null!)); // 存在性查询不抛错

        Assert.True(keywords.Remove("闪击"));
        Assert.False(keywords.Remove("闪击")); // 幂等：移除不存在＝false 无操作
        Assert.False(keywords.Contains("闪击"));
    }

    [Fact]
    public void KeywordData_Rejects_Blank_Identifiers()
    {
        var keywords = new KeywordData();
        Assert.Throws<ArgumentException>(() => keywords.Add(""));
        Assert.Throws<ArgumentException>(() => keywords.Add("  "));
        Assert.Throws<ArgumentNullException>(() => keywords.Remove(null!));
    }

    [Fact]
    public void CounterActivationData_Defaults_And_ReadWrite()
    {
        var state = new CounterActivationData();
        Assert.False(state.IsActive); // 初始 false

        state.IsActive = true;
        Assert.True(state.IsActive);
    }

    [Fact]
    public void All_Six_Components_Attach_Via_Existing_Component_System()
    {
        var card = CreateUnitCard();

        // 单位卡实例化路径已装配两件（指挥点花费＋对战）
        Assert.NotNull(card.GetData<CommandPointCostData>());
        Assert.NotNull(card.GetData<BattleStatsData>());

        // 其余四件（单位/指挥/词条/反制）：类定义就绪＋测试内构造挂载（实际挂载时机属后续批次）
        card.AddData(new UnitStateData());
        card.AddData(new CommandData());
        card.AddData(new KeywordData());
        card.AddData(new CounterActivationData());
        Assert.NotNull(card.GetData<UnitStateData>());
        Assert.NotNull(card.GetData<CommandData>());
        Assert.NotNull(card.GetData<KeywordData>());
        Assert.NotNull(card.GetData<CounterActivationData>());
    }

    [Fact]
    public void Missing_Component_Access_Throws_Along_Existing_Contract()
    {
        var card = CreateUnitCard();
        // 未挂载＝沿用既有 GetData 契约（缺失抛明确异常）
        Assert.Throws<KeyNotFoundException>(() => card.GetData<UnitStateData>());
    }
}
