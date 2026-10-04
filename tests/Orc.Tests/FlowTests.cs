using Orc.Cards;
using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收 E（流程）：攻击 band 全序（探针注入）；伤害结算 band 顺序（结算前→伤害生效）；伤害直落路径（无修正）；
/// 伤害结算可独立触发；施放用例（主动效果施放→经同一伤害结算→目标受伤）。
/// </summary>
public class FlowTests
{
    [Fact]
    public async Task AttackFlow_Band_Order_Counter_Resolve_Finalize_With_Injection_Extension_Points()
    {
        var engine = new LogicEngine();
        var a = new Card(engine, "甲");
        a.AddData(new HealthData { Hp = 10 });
        var d = new Card(engine, "乙");
        d.AddData(new HealthData { Hp = 10 });
        var trace = new List<string>();

        // 探针注入三个具名 band（扩展位存在且可注入）
        engine.AttackFlow.Trigger.Register(
            "探针-counter", (v, c, ct) => { trace.Add("counter"); return Task.CompletedTask; }, AttackFlowBands.Counter);
        engine.AttackFlow.Trigger.Register(
            "探针-resolve", (v, c, ct) => { trace.Add("resolve"); return Task.CompletedTask; }, AttackFlowBands.Resolve);
        engine.AttackFlow.Trigger.Register(
            "探针-finalize", (v, c, ct) => { trace.Add("finalize"); return Task.CompletedTask; }, AttackFlowBands.Finalize);

        await engine.AttackFlow.ExecuteAsync(a, d, 1);

        Assert.Equal(new[] { "counter", "resolve", "finalize" }, trace); // band 全序
        Assert.Equal(9, d.GetData<HealthData>().Hp);                      // 默认「伤害结算」事件照常（Resolve band）
    }

    [Fact]
    public async Task DamageFlow_Band_Order_PreApply_Before_Apply_With_Probe_Injection()
    {
        var engine = new LogicEngine();
        var a = new Card(engine, "甲");
        a.AddData(new HealthData { Hp = 10 });
        var d = new Card(engine, "乙");
        d.AddData(new HealthData { Hp = 10 });
        var trace = new List<string>();

        engine.DamageFlow.Trigger.Register(
            "探针-preapply", (v, c, ct) => { trace.Add("probe-preapply"); return Task.CompletedTask; }, DamageFlowBands.PreApply, priority: 100);
        engine.DamageFlow.Trigger.Register(
            "探针-apply", (v, c, ct) => { trace.Add("probe-apply"); return Task.CompletedTask; }, DamageFlowBands.Apply, priority: 100);

        await engine.DamageFlow.ResolveAsync(a, d, 5);

        Assert.Equal(new[] { "probe-preapply", "probe-apply" }, trace); // band 顺序：PreApply → Apply
        Assert.Equal(5, d.GetData<HealthData>().Hp);                    // 5 伤害直落（无修正）

        // 默认处理器留痕次序：结算前先于伤害生效
        var preIdx = S4TestHelpers.IndexOfEntry(
            engine, e => e.Keywords.Contains("damage") && e.Keywords.Contains("preapply"));
        var applyIdx = S4TestHelpers.IndexOfEntry(
            engine, e => e.Keywords.Contains("damage") && e.Keywords.Contains("apply"));
        Assert.True(preIdx >= 0 && applyIdx >= 0 && preIdx < applyIdx);
    }

    [Fact]
    public async Task DamageFlow_Direct_Damage_Path_And_Independent_Trigger()
    {
        var engine = new LogicEngine();
        var a = new Card(engine, "甲");
        a.AddData(new HealthData { Hp = 10 });
        var d = new Card(engine, "乙");
        d.AddData(new HealthData { Hp = 10 }); // 无修正，伤害直落

        var stream = await engine.DamageFlow.ResolveAsync(a, d, 5); // 独立触发（不经攻击流程亦可执行）

        Assert.Equal(5, d.GetData<HealthData>().Hp);
        Assert.Contains(stream.Entries, e => e.Keywords.Contains("damage") && e.Keywords.Contains("apply"));
        Assert.Contains(stream.Entries, e => e.Keywords.Contains("damage") && e.Keywords.Contains("preapply"));
    }

    [Fact]
    public async Task Cast_Flow_Order_Applies_Damage_Via_Same_Damage_Resolution()
    {
        var engine = new LogicEngine();
        var caster = new Card(engine, "法师");
        caster.AddData(new HealthData { Hp = 10 });
        var target = new Card(engine, "目标");
        target.AddData(new HealthData { Hp = 10 });
        var order = new DamageOrder(engine.DamageFlow);
        caster.AddEffect(order);
        await S4TestHelpers.Place(engine, caster);

        // 施放＝主动触发器被调用（主动效果无装载/钩子）
        Assert.Equal(0, order.MountCount);
        Assert.False(order.IsMounted);

        var orderStream = await order.CastAsync(engine, new Dictionary<string, object?>
        {
            [PayloadKeys.Source] = caster,
            [PayloadKeys.Target] = target,
            [PayloadKeys.Amount] = 4,
        });

        Assert.Equal(6, target.GetData<HealthData>().Hp); // 施放→（经伤害结算）→目标受伤
        Assert.Equal(1, order.CastCount);
        Assert.Equal(0, order.UnmountCount);

        // 复用同一伤害结算流程：施放流下含伤害结算子流（与攻击链对照）
        var damageStream = orderStream.Children.Single();
        Assert.Contains(damageStream.Entries, e => e.Keywords.Contains("damage") && e.Keywords.Contains("apply"));
    }

    [Fact]
    public async Task Heal_Injection_Sits_After_Damage_Resolution_Within_Resolve_Band()
    {
        var engine = new LogicEngine();
        var attacker = new Card(engine, "战士");
        attacker.AddData(new HealthData { Hp = 10 });
        var defender = new Card(engine, "哥布林");
        defender.AddData(new HealthData { Hp = 10 });
        var heal = new AttackHealEffect(engine, 5);
        attacker.AddEffect(heal);
        await S4TestHelpers.Place(engine, attacker);

        var trace = new List<string>();
        engine.AttackFlow.Trigger.Register(
            "探针-finalize", (v, c, ct) => { trace.Add("finalize"); return Task.CompletedTask; }, AttackFlowBands.Finalize);

        await engine.AttackFlow.ExecuteAsync(attacker, defender, 3);

        // 伤害结算（默认，priority 0）→ 回血（注入，priority 100）→ 收尾 band：回血先于 Finalize（实现设计说明：攻击结算完成时＝Resolve band 尾）
        Assert.Equal(new[] { "finalize" }, trace);
        Assert.Equal(7, heal.TargetHpAtHealTime); // 回血时伤害已生效
        Assert.Equal(15, attacker.GetData<HealthData>().Hp);
    }
}
