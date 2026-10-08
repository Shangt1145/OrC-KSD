using Orc.Game.Cards;
using Orc.Game.Effects;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// E1-57（**真实数值条件**）：<c>EffectRuntime.EvaluateCondition</c>（计数/资源/总部属性比较）
/// 与 <c>SelectAsync</c> 的**目标阈值过滤**。
/// </summary>
public class EffectConditionTests
{
    [Fact]
    public async Task SelectAsync_Applies_Target_Threshold()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];

        // 三个友方兵（攻 2／3／4）——视角卡＝首个（攻 2，兼作 EffectRuntime 解析宿主）
        var viewer = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);   // 攻 2
        var fighter = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.FighterId, 2);   // 攻 3
        var bomber = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.BomberId, 1);     // 攻 4
        var runtime = EffectRuntime.ResolveFor(viewer);
        Assert.NotNull(runtime);

        var weakOnly = await runtime!.SelectAsync(
            viewer, new EffectSelector("all", "friendly", null, null, null, null, new EffectThreshold("attack", "lte", 2)));
        var strongOnly = await runtime.SelectAsync(
            viewer, new EffectSelector("all", "friendly", null, null, null, null, new EffectThreshold("attack", "gte", 4)));

        Assert.Equal(new[] { viewer }, weakOnly);      // 仅攻 ≤ 2 者
        Assert.Equal(new[] { bomber }, strongOnly);    // 仅攻 ≥ 4 者
        Assert.DoesNotContain(fighter, weakOnly);
        Assert.DoesNotContain(bomber, weakOnly);
    }

    [Fact]
    public async Task EvaluateCondition_Compares_Unit_Counts_And_Hq_Defense()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var viewer = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.WeakId, 0);

        // 单位数：友方 1（只有视角卡）、敌方 1
        Assert.True(EffectRuntime.EvaluateCondition(viewer, "count=s=friendly:gte:#1"));
        Assert.False(EffectRuntime.EvaluateCondition(viewer, "count=s=friendly:gte:#2"));
        Assert.True(EffectRuntime.EvaluateCondition(viewer, "count=s=enemy:eq:#1"));
        Assert.True(EffectRuntime.EvaluateCondition(viewer, "count=s=friendly:lte:count=s=enemy"));

        // 己方总部防御力（＝HQ 生命值）
        var hqDefense = playerA.Hq.Health;
        Assert.True(EffectRuntime.EvaluateCondition(viewer, $"stat=f=defense;s=friendly;z=hq:eq:#{hqDefense}"));
        Assert.True(EffectRuntime.EvaluateCondition(viewer, $"stat=f=defense;s=friendly;z=hq:gte:#{hqDefense - 1}"));

        // 不可求值/非法 ⇒ false（不抛错）
        Assert.False(EffectRuntime.EvaluateCondition(viewer, "stat=f=defense;s=enemy;z=hq:gte:#1"));
        Assert.False(EffectRuntime.EvaluateCondition(viewer, "garbage"));
        Assert.False(EffectRuntime.EvaluateCondition(null, "count=s=friendly:gte:#1"));
    }
}

/// <summary>
/// E1-56（**光环**）：<c>EffectRuntime.DeclareAuraAsync</c> 的语义——受益集合按谓词**实时重算**
/// （后加入/移入者亦受益），与一次性修饰器（<c>BuffAsync</c>）区分。
/// </summary>
public class EffectAuraTests
{
    [Fact]
    public async Task Declared_Aura_Filters_Beneficiaries_And_Reevaluates_Dynamically()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var runtime = EffectRuntime.ResolveFor(host);
        Assert.NotNull(runtime);
        var baseAttack = host.Modifiers.GetEffectiveValue(CardStatFields.Attack);

        // 友方步兵 +2、排除宿主自身
        var declared = await runtime!.DeclareAuraAsync(
            host, "attack", 2, new EffectAuraFilter(Side: "friendly", UnitType: "Infantry", ExcludeSelf: true));
        Assert.True(declared);
        Assert.Equal(baseAttack, host.Modifiers.GetEffectiveValue(CardStatFields.Attack)); // 宿主被排除

        // **动态重算**：登记之后才加入的友方步兵同样受益（不是登记时的一次性快照）
        var later = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 2);
        Assert.Equal(baseAttack + 2, later.Modifiers.GetEffectiveValue(CardStatFields.Attack));

        // 敌方步兵不受益（阵营面）
        var enemy = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.InfantryId, 0);
        Assert.Equal(baseAttack, enemy.Modifiers.GetEffectiveValue(CardStatFields.Attack));

        // 友方非步兵不受益（兵种面）：等于其**自身基准**攻（未被光环加成）
        var bomber = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.BomberId, 1);
        Assert.Equal(
            bomber.Definition.Attack, bomber.Modifiers.GetEffectiveValue(CardStatFields.Attack));
    }

    [Fact]
    public async Task Aura_Is_Removed_With_Its_Source_Effect()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var runtime = EffectRuntime.ResolveFor(host);
        Assert.NotNull(runtime);
        var baseAttack = host.Modifiers.GetEffectiveValue(CardStatFields.Attack);
        await runtime!.DeclareAuraAsync(host, "attack", 3, new EffectAuraFilter(Side: "friendly", UnitType: "Infantry"));

        Assert.Equal(baseAttack + 3, host.Modifiers.GetEffectiveValue(CardStatFields.Attack));

        // 宿主离场（死亡链内置「在场」门禁）⇒ 光环不再合成
        await host.ApplyDefenseDamageAsync(99);
        Assert.Equal(baseAttack, host.Modifiers.GetEffectiveValue(CardStatFields.Attack));
    }
}

/// <summary>
/// E1-41（期限）：<c>EffectRuntime.BuffAsync/CostModAsync</c> 的**期限**语义——
/// <see cref="EffectDuration.TurnEnd"/>＝本回合结束相位到期自注销；<see cref="EffectDuration.Permanent"/>＝不撤销。
/// </summary>
public class EffectDurationTests
{
    [Fact]
    public async Task Buff_With_TurnEnd_Duration_Expires_At_Turn_End()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var unit = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var runtime = EffectRuntime.ResolveFor(unit);
        Assert.NotNull(runtime);
        var before = unit.Modifiers.GetEffectiveValue(CardStatFields.Attack);

        await runtime!.BuffAsync(unit, 2, 0, EffectDuration.TurnEnd);
        Assert.Equal(before + 2, unit.Modifiers.GetEffectiveValue(CardStatFields.Attack));

        await match.EndTurn(); // A 回合结束 ⇒ 相位 turn.end ⇒ 期限修饰器自注销
        Assert.Equal(before, unit.Modifiers.GetEffectiveValue(CardStatFields.Attack));
    }

    [Fact]
    public async Task Buff_Without_Duration_Survives_Turn_End()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var unit = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var runtime = EffectRuntime.ResolveFor(unit);
        Assert.NotNull(runtime);
        var before = unit.Modifiers.GetEffectiveValue(CardStatFields.Attack);

        await runtime!.BuffAsync(unit, 3, 0); // 无期限＝随效果存续
        await match.EndTurn();
        await match.EndTurn();

        Assert.Equal(before + 3, unit.Modifiers.GetEffectiveValue(CardStatFields.Attack));
    }

    [Fact]
    public async Task CostMod_With_NextOwner_TurnStart_Expires_On_Owners_Turn_Start()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var unit = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var runtime = EffectRuntime.ResolveFor(unit);
        Assert.NotNull(runtime);
        var before = unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost);

        await runtime!.CostModAsync(unit, -1, EffectDuration.NextOwnerTurnStart);
        Assert.Equal(before - 1, unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost));

        await match.EndTurn(); // A 结束（turn.end 不触发本期限）
        Assert.Equal(before - 1, unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost));

        await match.EndTurn(); // B 结束 → A 回合开始（turn.start，载荷玩家＝A）⇒ 到期
        Assert.Equal(before, unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost));
    }
}
