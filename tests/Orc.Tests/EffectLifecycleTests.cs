using Orc.Cards;
using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收 B（生命周期边界）与 D（钩子）：
/// 静态组装/即时装载/重复放置与重复移除与重复销毁的幂等/未装载移除仅容器面/两路径收敛；
/// 多效果隔离/多卡牌隔离/顺序确定性；钩子次数与次序、失败隔离、上下文可获得、主动效果不调用钩子。
/// </summary>
public class EffectLifecycleTests
{
    // ---------- 放置链路边界 ----------

    [Fact]
    public async Task Add_Before_Place_Is_Static_Assembly_And_Place_Drives_Loadout()
    {
        var engine = new LogicEngine();
        var attacker = new Card(engine, "战士");
        attacker.AddData(new HealthData { Hp = 10 });
        var defender = new Card(engine, "哥布林");
        defender.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        attacker.AddEffect(heal);

        // 放置前：静态组装——未装载、无注入
        Assert.False(heal.IsMounted);
        Assert.Equal(0, heal.MountCount);
        Assert.DoesNotContain("攻击时回血", engine.Bus.GetSubscribers(Updates.EffectRemoved));

        await engine.AttackFlow.ExecuteAsync(attacker, defender, 3); // 注入未完成：不回血
        Assert.Equal(0, heal.HealCount);
        Assert.Equal(10, attacker.GetData<HealthData>().Hp);

        // 放置驱动装载
        await S4TestHelpers.Place(engine, attacker);
        Assert.True(attacker.IsPlaced);
        Assert.True(heal.IsMounted);
        Assert.Equal(1, heal.MountCount);

        await engine.AttackFlow.ExecuteAsync(attacker, defender, 3); // 已生效
        Assert.Equal(1, heal.HealCount);
        Assert.Equal(15, attacker.GetData<HealthData>().Hp);
    }

    [Fact]
    public async Task Add_After_Place_Loads_Immediately()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        await S4TestHelpers.Place(engine, card); // 先放置（当刻无效果）

        var heal = new AttackHealEffect(engine, 5);
        card.AddEffect(heal); // 已放置卡即时装载（与放置驱动装载行为一致）

        Assert.True(heal.IsMounted);
        Assert.Equal(1, heal.MountCount);
        Assert.Single(S4TestHelpers.EntriesWith(engine, "loadout", "mount", "攻击时回血"));

        var defender = new Card(engine, "哥布林");
        defender.AddData(new HealthData { Hp = 10 });
        await engine.AttackFlow.ExecuteAsync(card, defender, 3);
        Assert.Equal(1, heal.HealCount); // 即时装载后立即生效
    }

    [Fact]
    public async Task Repeated_Place_Is_Idempotent()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        card.AddEffect(heal);

        await S4TestHelpers.Place(engine, card);
        await S4TestHelpers.Place(engine, card); // 重复放置

        Assert.Equal(1, heal.MountCount); // 不重复 OnMount
        Assert.Single(S4TestHelpers.EntriesWith(engine, "loadout", "mount", "攻击时回血")); // 不重复装载
        Assert.Contains(
            engine.RootStream.Entries,
            e => e.Keywords.Contains("skip") && e.Keywords.Contains("战士")); // 幂等留痕
    }

    [Fact]
    public async Task Repeated_Remove_And_Remove_Of_Unknown_Are_Idempotent()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        card.AddEffect(heal);
        await S4TestHelpers.Place(engine, card);

        card.RemoveEffect(heal);
        card.RemoveEffect(heal); // 重复移除：幂等、不抛错
        card.RemoveEffect(new RecordingPassiveEffect("从未添加", "x", new List<string>())); // 不存在：幂等、不抛错

        Assert.Equal(1, heal.UnmountCount);
        Assert.Single(S4TestHelpers.EntriesWith(engine, "loadout", "cleanup", "攻击时回血"));
    }

    [Fact]
    public async Task Repeated_Destroy_Is_Idempotent()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        card.AddEffect(heal);
        await S4TestHelpers.Place(engine, card);

        await engine.DestroyCard(card);
        await engine.DestroyCard(card); // 重复销毁：幂等

        Assert.False(heal.IsMounted);
        Assert.Empty(card.Effects);
        Assert.Equal(1, heal.UnmountCount);
        Assert.Single(S4TestHelpers.EntriesWith(engine, "loadout", "cleanup", "攻击时回血"));
    }

    [Fact]
    public async Task Remove_Before_Place_Is_Container_Only()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        card.AddEffect(heal); // 未放置（未装载）

        card.RemoveEffect(heal); // 仅容器面移除

        Assert.Empty(card.Effects);
        Assert.False(heal.IsMounted);
        Assert.Equal(0, heal.UnmountCount); // 未装载不触 OnUnmount（幂等边界）
        Assert.Equal(0, heal.MountCount);
        Assert.Contains(
            engine.RootStream.Entries,
            e => e.Keywords.Contains("detach") && e.Keywords.Contains("攻击时回血")); // 留痕：未装载移除
        Assert.DoesNotContain(
            engine.RootStream.Entries,
            e => e.Keywords.Contains("cleanup") && e.Keywords.Contains("攻击时回血")); // 无运行态清理
    }

    // ---------- 两路径收敛 ----------

    [Fact]
    public async Task RemoveEffect_Path_Removes_Container_And_Runtime()
    {
        var engine = new LogicEngine();
        var attacker = new Card(engine, "战士");
        attacker.AddData(new HealthData { Hp = 10 });
        var defender = new Card(engine, "哥布林");
        defender.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        attacker.AddEffect(heal);
        await S4TestHelpers.Place(engine, attacker);

        attacker.RemoveEffect(heal); // 路径一：容器规范入口

        Assert.Empty(attacker.Effects);
        Assert.False(heal.IsMounted);
        Assert.Equal(1, heal.UnmountCount);
        Assert.True(heal.ExclusiveCleanupRan);
        Assert.True(heal.InjectionRefCleared);
        Assert.DoesNotContain("攻击时回血", engine.Bus.GetSubscribers(Updates.EffectRemoved));

        await engine.AttackFlow.ExecuteAsync(attacker, defender, 3); // 清理后不再回血、不再进入 handler
        Assert.Equal(0, heal.HealCount);
        Assert.Equal(10, attacker.GetData<HealthData>().Hp);
    }

    [Fact]
    public async Task EffectRemoved_Update_Path_Converges_To_Same_Cleanup()
    {
        var engine = new LogicEngine();
        var attacker = new Card(engine, "战士");
        attacker.AddData(new HealthData { Hp = 10 });
        var defender = new Card(engine, "哥布林");
        defender.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        attacker.AddEffect(heal);
        await S4TestHelpers.Place(engine, attacker);

        await S4TestHelpers.RemoveViaUpdate(engine, attacker, heal); // 路径二：低层 effect.removed 直接驱动

        // 与路径一共收敛终态：容器面移除＋卸载链完成
        Assert.Empty(attacker.Effects);
        Assert.False(heal.IsMounted);
        Assert.Equal(1, heal.UnmountCount);
        Assert.True(heal.ExclusiveCleanupRan);
        Assert.DoesNotContain("攻击时回血", engine.Bus.GetSubscribers(Updates.EffectRemoved));
        Assert.Contains(
            engine.RootStream.Entries,
            e => e.Keywords.Contains("loadout") && e.Keywords.Contains("cleanup") && e.Keywords.Contains("攻击时回血"));

        await engine.AttackFlow.ExecuteAsync(attacker, defender, 3); // 清理后不再回血
        Assert.Equal(0, heal.HealCount);
        Assert.Equal(10, attacker.GetData<HealthData>().Hp);
    }

    // ---------- 隔离 ----------

    [Fact]
    public async Task Multi_Effect_Isolation_Removing_One_Keeps_Other_Working()
    {
        var engine = new LogicEngine();
        var attacker = new Card(engine, "战士");
        attacker.AddData(new HealthData { Hp = 10 });
        var defender = new Card(engine, "哥布林");
        defender.AddData(new HealthData { Hp = 10 });
        var heal5 = new AttackHealEffect(engine, 5, "攻击回血五");
        var heal2 = new AttackHealEffect(engine, 2, "攻击回血二");
        attacker.AddEffect(heal5);
        attacker.AddEffect(heal2);
        await S4TestHelpers.Place(engine, attacker);

        await engine.AttackFlow.ExecuteAsync(attacker, defender, 3);
        Assert.Equal(17, attacker.GetData<HealthData>().Hp); // 5 + 2
        Assert.Equal(1, heal5.HealCount);
        Assert.Equal(1, heal2.HealCount);

        attacker.RemoveEffect(heal5); // 移除其一：只清理该效果的注入与装载面

        Assert.False(heal5.IsMounted);
        Assert.True(heal2.IsMounted); // 另一效果不受影响

        await engine.AttackFlow.ExecuteAsync(attacker, defender, 3);
        Assert.Equal(19, attacker.GetData<HealthData>().Hp); // 只回 2
        Assert.Equal(1, heal5.HealCount); // 不再进入被移除的 handler
        Assert.Equal(2, heal2.HealCount);
    }

    [Fact]
    public async Task Multi_Card_Isolation_Destroying_One_Keeps_Other_Working()
    {
        var engine = new LogicEngine();
        var a = new Card(engine, "甲");
        a.AddData(new HealthData { Hp = 10 });
        var b = new Card(engine, "乙");
        b.AddData(new HealthData { Hp = 10 });
        var target = new Card(engine, "木桩");
        target.AddData(new HealthData { Hp = 100 });
        var healA = new AttackHealEffect(engine, 5, "甲的回血");
        var healB = new AttackHealEffect(engine, 2, "乙的回血");
        a.AddEffect(healA);
        b.AddEffect(healB);
        await S4TestHelpers.Place(engine, a);
        await S4TestHelpers.Place(engine, b);

        await engine.DestroyCard(a); // 销毁其一

        Assert.False(healA.IsMounted);      // 甲：清理完成
        Assert.Empty(a.Effects);
        Assert.False(a.Life.IsAlive);
        Assert.True(healB.IsMounted);       // 乙：不受影响
        Assert.Equal(new Effect[] { healB }, b.Effects);

        await engine.AttackFlow.ExecuteAsync(b, target, 3); // 乙仍生效
        Assert.Equal(12, b.GetData<HealthData>().Hp);
        Assert.Equal(1, healB.HealCount);
        Assert.Equal(0, healA.HealCount);
    }

    [Fact]
    public async Task Load_And_Cleanup_Order_Is_Deterministic_By_Effects_List()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var card = new Card(engine, "战士");
        card.AddEffect(new RecordingPassiveEffect("效果一", "r1", trace));
        card.AddEffect(new RecordingPassiveEffect("效果二", "r2", trace));
        await S4TestHelpers.Place(engine, card);

        Assert.Equal(new[] { "r1:mount", "r2:mount" }, trace); // 装载顺序＝Effects 列表序

        await engine.DestroyCard(card);
        Assert.Equal(new[] { "r1:mount", "r2:mount", "r1:unmount", "r2:unmount" }, trace); // 清理顺序同样确定
    }

    // ---------- 钩子失败隔离 ----------

    [Fact]
    public async Task Mount_Failure_Is_Recorded_Rolled_Back_And_Does_Not_Block_Others()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var bad = new FailingMountEffect();
        var good = new AttackHealEffect(engine, 5, "好效果");
        card.AddEffect(bad);
        card.AddEffect(good);

        await S4TestHelpers.Place(engine, card); // 不抛：装载失败被隔离

        Assert.False(bad.IsMounted);  // 视为未生效（回滚）
        Assert.True(good.IsMounted);  // 不阻断其它效果的装载
        Assert.DoesNotContain("装载炸弹", engine.Bus.GetSubscribers(Updates.EffectRemoved)); // 回滚：主触发器未滞留

        var error = engine.RootStream.Entries.Single(
            e => e.Level == LogLevel.Error && e.Source == "装载炸弹/OnMount");
        Assert.Contains("loadout", error.Keywords); // 失败记录可定位（source 含效果标识与钩子名）

        var defender = new Card(engine, "哥布林");
        defender.AddData(new HealthData { Hp = 10 });
        await engine.AttackFlow.ExecuteAsync(card, defender, 3); // 好效果照常生效
        Assert.Equal(1, good.HealCount);
    }

    [Fact]
    public async Task Unmount_Failure_Is_Recorded_And_Framework_Cleanup_Still_Completes()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var bad = new FailingUnmountEffect();
        var good = new AttackHealEffect(engine, 5, "好效果");
        card.AddEffect(bad);
        card.AddEffect(good);
        await S4TestHelpers.Place(engine, card);

        await engine.DestroyCard(card); // 不抛：清理失败被隔离

        Assert.False(bad.IsMounted);  // 框架撤销/卸载照常完成（「清理未完成，以实况计」）
        Assert.False(good.IsMounted); // 不阻断其它效果的清理
        Assert.Empty(card.Effects);
        Assert.DoesNotContain("卸载炸弹", engine.Bus.GetSubscribers(Updates.EffectRemoved));

        var error = engine.RootStream.Entries.Single(
            e => e.Level == LogLevel.Error && e.Source == "卸载炸弹/OnUnmount");
        Assert.Contains("loadout", error.Keywords);
        Assert.Contains(
            engine.RootStream.Entries,
            e => e.Keywords.Contains("cleanup") && e.Keywords.Contains("卸载炸弹")); // 框架部分照常留痕
    }

    // ---------- 钩子次序与上下文 ----------

    [Fact]
    public async Task Hooks_Order_Mount_Before_OnMount_And_OnUnmount_Before_Unmount()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        card.AddEffect(heal);

        await S4TestHelpers.Place(engine, card);
        Assert.True(heal.MountSawTriggerOnBus); // 挂载先行、OnMount 后随
        Assert.True(heal.SawPlacedAtMount);     // 单位初始化先、效果注入后
        Assert.Equal(10, heal.MountHp);         // 钩子内可访问宿主卡牌与数据组件

        card.RemoveEffect(heal);
        Assert.True(heal.UnmountSawTriggerOnBus); // OnUnmount 先行、总线卸载后随
        Assert.Equal("战士", heal.UnmountHostName); // OnUnmount 时宿主可访问（专属清理语境）
        Assert.Equal(1, heal.MountCount);
        Assert.Equal(1, heal.UnmountCount);       // 恰一次
    }

    [Fact]
    public async Task Active_Effect_Does_Not_Invoke_Hooks_Nor_Mount()
    {
        var engine = new LogicEngine();
        var caster = new Card(engine, "法师");
        var order = new DamageOrder(engine.DamageFlow);
        caster.AddEffect(order);

        await S4TestHelpers.Place(engine, caster); // 放置处理：主动效果不做装载动作

        Assert.False(order.IsMounted);      // 主动效果无装载动作
        Assert.Equal(0, order.MountCount);  // 不执行钩子
        Assert.DoesNotContain("火球术", engine.Bus.GetSubscribers(Updates.EffectRemoved));

        caster.RemoveEffect(order);
        Assert.Equal(0, order.UnmountCount); // 移除时也不执行钩子（仅列表移除）
        Assert.Empty(caster.Effects);

        caster.AddEffect(order);             // 已放置后 Add（主动：仍无装载动作）
        Assert.False(order.IsMounted);
        Assert.Equal(0, order.MountCount);
    }

    [Fact]
    public async Task EffectRemoved_Update_For_Unloaded_Effect_Is_Container_Only()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        card.AddEffect(heal); // 未放置（未装载）

        await S4TestHelpers.RemoveViaUpdate(engine, card, heal); // 低层更新驱动对未装载效果：幂等模板（仅容器面）

        Assert.Empty(card.Effects);
        Assert.False(heal.IsMounted);
        Assert.Equal(0, heal.UnmountCount);
        Assert.Equal(0, heal.MountCount);
    }
}
