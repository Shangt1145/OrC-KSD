using Orc.Core;
using Orc.Game.Triggers;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2A 验收锚点⑦（底层/外部触发器标注与注册机制）：注册（本体＋分层分类必填；重复/null/非法分层明确拒绝；注册后不可变、无移除）；
/// 查询（按分层枚举：全部底层／全部外部；全量＝登记序）；注册与挂载/运行完全解耦（未注册照常运行；注册不影响运行）；
/// 对局级归属（每对局一份）；2C 起内置流程触发器注册落地（指挥 / 单位移动 / 单位攻击 / 造成攻击伤害——底层四项）。
/// </summary>
public class TriggerRegistryTests
{
    private static Trigger<CardTriggerView> CreateTrigger(string name = "测试触发器") => new(name);

    [Fact]
    public void Register_And_Enumerate_By_Layer()
    {
        var registry = new TriggerRegistry();
        var lowLevel = CreateTrigger("底层甲");
        var externalA = CreateTrigger("外部甲");
        var externalB = CreateTrigger("外部乙");

        registry.Register(lowLevel, TriggerLayer.LowLevel);
        registry.Register(externalA, TriggerLayer.External);
        registry.Register(externalB, TriggerLayer.External);

        // 全量（登记序；登记信息面＝本体＋分层分类）
        Assert.Equal(3, registry.Entries.Count);
        Assert.Same(lowLevel, registry.Entries[0].Trigger);
        Assert.Equal(TriggerLayer.LowLevel, registry.Entries[0].Layer);
        Assert.Equal(TriggerLayer.External, registry.Entries[1].Layer);

        // 按分层枚举：全部底层 / 全部外部
        var lowLevelEntries = registry.GetByLayer(TriggerLayer.LowLevel);
        Assert.Single(lowLevelEntries);
        Assert.Same(lowLevel, lowLevelEntries[0].Trigger);
        var externalEntries = registry.GetByLayer(TriggerLayer.External);
        Assert.Equal(new object[] { externalA, externalB }, externalEntries.Select(e => e.Trigger));
    }

    [Fact]
    public void Duplicate_And_Invalid_Registrations_Are_Rejected()
    {
        var registry = new TriggerRegistry();
        var trigger = CreateTrigger();
        registry.Register(trigger, TriggerLayer.External);

        Assert.Throws<InvalidOperationException>(() => registry.Register(trigger, TriggerLayer.External)); // 重复登记
        Assert.Throws<InvalidOperationException>(() => registry.Register(trigger, TriggerLayer.LowLevel)); // 换层同一对象同样拒绝
        Assert.Throws<ArgumentNullException>(() => registry.Register<CardTriggerView>(null!, TriggerLayer.External)); // null 拒绝
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Register(CreateTrigger("新"), (TriggerLayer)99)); // 非法分层拒绝
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.GetByLayer((TriggerLayer)99)); // 查询侧同样校验
        Assert.Single(registry.Entries); // 拒绝路径不改注册表
    }

    [Fact]
    public async Task Registration_Is_Fully_Decoupled_From_Trigger_Running()
    {
        var engine = new LogicEngine();
        var registry = new TriggerRegistry();
        var trigger = CreateTrigger("解耦验证");
        trigger.Register("事件", (view, ctx, ct) => Task.CompletedTask);

        // 未注册触发器照常运行（注册表不参与运行、不做运行期检查）
        var before = await trigger.InvokeAsync(engine);
        Assert.Equal(ExecutionOutcome.Normal, before.Outcome);

        // 注册（只记账）不影响运行
        registry.Register(trigger, TriggerLayer.External);
        var after = await trigger.InvokeAsync(engine);
        Assert.Equal(ExecutionOutcome.Normal, after.Outcome);
    }

    [Fact]
    public async Task Match_Registry_Is_Per_Match_And_LowLevel_Flow_Triggers_Are_Registered()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();

        // 2C：内置流程触发器注册为底层（指挥 / 单位移动 / 单位攻击 / 造成攻击伤害——分层可查询、测试可断言）
        var lowLevel = match.TriggerRegistry.GetByLayer(TriggerLayer.LowLevel);
        Assert.Equal(4, lowLevel.Count);
        Assert.Contains(lowLevel, entry => ReferenceEquals(entry.Trigger, match.CommandManager.CommandTrigger));
        Assert.Contains(lowLevel, entry => ReferenceEquals(entry.Trigger, match.CommandManager.UnitMoveTrigger));
        Assert.Contains(lowLevel, entry => ReferenceEquals(entry.Trigger, match.CommandManager.UnitAttackTrigger));
        Assert.Contains(lowLevel, entry => ReferenceEquals(entry.Trigger, match.CommandManager.AttackDamageTrigger));
        Assert.Empty(match.TriggerRegistry.GetByLayer(TriggerLayer.External));

        // 对局级：每对局一份，互不相通（另一对局未初始化＝注册表为空）
        var other = GameTestData.CreateStandardMatch(seed: 43);
        Assert.NotSame(match.TriggerRegistry, other.TriggerRegistry);
        Assert.Empty(other.TriggerRegistry.Entries);
    }
}
