using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Orc.Game.Judicators;
using Orc.Game.Players;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// J3 示范②（判定器包装 Targeter——持有转发）示范场景测试：
/// ① 完整选择流程（经既有 Targeter 框架：收集→筛选→交互→终局）默认态——筛选环节规则求值经判定器进行；
/// ② 改写「禁止某类目标可选」（步兵）——该类从允许集消失、提交该类被拒（不构成终局）；注销回退（集合恢复）；
/// ③ 改写「允许通常不可选」（HQ）——可选范围扩大；注销回退（双验：允许/禁止两向）。
/// 链路口径（需求 Q&amp;A-4）：调用方（选择请求构造方）经注册面按名解析 → 包装（<see cref="JudicatorSelectionRule"/>）→
/// 接入筛选链（<see cref="JudicatorSelectionRule.AsFilter"/> 细筛谓词）；不直连判定器实例、不绕注册面；
/// 三态改写经同一路径（同一规则包装）观测。「全局生效」＝单引用点行为差异＋机制级背书（J1/J2 机制测试）。
/// 对应验收：③（包装〔持有转发〕就位；选择流程经判定器调用）④（改写替换生效——允许/禁止某类目标可选；注销回退）。
/// </summary>
public class JudicatorTargetSelectionTests
{
    // ---------- ① 完整流程·默认态 ----------

    [Fact]
    public async Task Full_Selection_Flow_Default_Rule_Filters_Through_Judicator()
    {
        var bridge = new MockTargeterBridge();
        var match = CreateTargetingMatch(bridge);
        await match.Initialize();

        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var infantryA = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var tankA = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.TankId, 2);
        var infantryB = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.InfantryId, 1);

        bridge.CollectScript = CommandTestKit.AllRefsScript(match); // 候选超集（槽位＋单位＋双方 HQ）

        // 调用方接线：注册面按名解析 → 包装为选择规则 → 接入筛选链（细筛谓词）
        var rule = new JudicatorSelectionRule(match.Judicators.Resolve(JudicatorNames.TargetCandidateEligibility));
        var targeter = match.TargeterManager.CreateTargeter(filter: rule.AsFilter());

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 默认规则＝在场单位可选：允许集＝全部单位；HQ/槽位候选被默认规则排除（经判定器求值）
        Assert.Equal(new[] { infantryA.Ref, tankA.Ref, infantryB.Ref }, description.AllowedTargets.ToArray());

        // 终局：选第一允许项（步兵 A）完成
        Assert.True(responder.Complete(
            description.RequestId,
            TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), infantryA.Ref)));
        var result = await task;
        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.Same(infantryA.Ref, result.Outcome!.Single);
    }

    // ---------- ② 改写（禁止步兵类可选）＋提交被拒＋注销回退 ----------

    [Fact]
    public async Task Forbid_Infantry_Moding_Removes_Class_And_Falls_Back()
    {
        var bridge = new MockTargeterBridge();
        var match = CreateTargetingMatch(bridge);
        await match.Initialize();

        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var infantryA = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var tankA = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.TankId, 2);
        var infantryB = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.InfantryId, 1);

        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        var registration = match.Judicators.Resolve(JudicatorNames.TargetCandidateEligibility);
        var rule = new JudicatorSelectionRule(registration);
        var targeter = match.TargeterManager.CreateTargeter(filter: rule.AsFilter());

        // 态 1（默认）：允许集含步兵类
        var task1 = targeter.Targeting();
        var (d1, r1) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(new[] { infantryA.Ref, tankA.Ref, infantryB.Ref }, d1.AllowedTargets.ToArray());
        Assert.True(r1.Cancel(d1.RequestId));
        Assert.Equal(TargetingStatus.Cancelled, (await task1).Status);

        // 态 2（改写：禁止步兵类可选——经强类型面注入、规则整体更换）
        var moding = match.Judicators.RegisterModing<TargetEligibilityJudicator.TargetCandidateRule>(
            registration,
            candidate => candidate.IsAlive && candidate.Value is UnitCard card
                && !card.GetData<UnitStateData>().UnitTypes.Contains(UnitType.Infantry));
        Assert.NotNull(moding);

        var task2 = targeter.Targeting();
        var (d2, r2) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(new[] { tankA.Ref }, d2.AllowedTargets.ToArray()); // 步兵类从允许集消失

        // 提交该类＝被拒（不在允许集；不构成终局、请求继续等待）
        Assert.False(r2.Complete(
            d2.RequestId,
            TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(d2), infantryA.Ref)));
        Assert.False(task2.IsCompleted);

        // 允许项提交成功（终局）
        Assert.True(r2.Complete(
            d2.RequestId,
            TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(d2), tankA.Ref)));
        var result2 = await task2;
        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Same(tankA.Ref, result2.Outcome!.Single);

        // 态 3（注销回退）：经同一路径恢复默认允许集
        Assert.True(match.Judicators.UnregisterModing(moding!));
        var task3 = targeter.Targeting();
        var (d3, r3) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(new[] { infantryA.Ref, tankA.Ref, infantryB.Ref }, d3.AllowedTargets.ToArray());
        Assert.True(r3.Cancel(d3.RequestId));
        Assert.Equal(TargetingStatus.Cancelled, (await task3).Status);
    }

    // ---------- ③ 改写（允许通常不可选〔HQ〕）＋注销回退 ----------

    [Fact]
    public async Task Allow_Moding_Enables_Usually_Ineligible_Class_And_Falls_Back()
    {
        var bridge = new MockTargeterBridge();
        var match = CreateTargetingMatch(bridge);
        await match.Initialize();

        var playerA = match.Players[0];
        var playerB = match.Players[1];
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.TankId, 2);
        await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.InfantryId, 1);

        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        var registration = match.Judicators.Resolve(JudicatorNames.TargetCandidateEligibility);
        var rule = new JudicatorSelectionRule(registration);
        var targeter = match.TargeterManager.CreateTargeter(filter: rule.AsFilter());

        // 态 1（默认）：HQ 通常不可选（允许集不含 HQ 引用）
        var task1 = targeter.Targeting();
        var (d1, r1) = await bridge.WaitForNextBeginAsync();
        Assert.DoesNotContain(playerA.Hq.Ref, d1.AllowedTargets);
        Assert.DoesNotContain(playerB.Hq.Ref, d1.AllowedTargets);
        Assert.True(r1.Cancel(d1.RequestId));
        await task1;

        // 态 2（改写：允许 HQ 类可选——可选范围扩大）
        var moding = match.Judicators.RegisterModing<TargetEligibilityJudicator.TargetCandidateRule>(
            registration,
            candidate => candidate.IsAlive && (candidate.Value is UnitCard || candidate.Value is Hq));
        Assert.NotNull(moding);

        var task2 = targeter.Targeting();
        var (d2, r2) = await bridge.WaitForNextBeginAsync();
        Assert.Contains(playerA.Hq.Ref, d2.AllowedTargets);
        Assert.Contains(playerB.Hq.Ref, d2.AllowedTargets);

        // 改写后可提交 HQ（通常不可选项——现可选）
        Assert.True(r2.Complete(
            d2.RequestId,
            TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(d2), playerA.Hq.Ref)));
        var result2 = await task2;
        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Same(playerA.Hq.Ref, result2.Outcome!.Single);

        // 态 3（注销回退）：HQ 恢复不可选
        Assert.True(match.Judicators.UnregisterModing(moding!));
        var task3 = targeter.Targeting();
        var (d3, r3) = await bridge.WaitForNextBeginAsync();
        Assert.DoesNotContain(playerA.Hq.Ref, d3.AllowedTargets);
        Assert.DoesNotContain(playerB.Hq.Ref, d3.AllowedTargets);
        Assert.True(r3.Cancel(d3.RequestId));
        await task3;
    }

    // ---------- 测试辅助 ----------

    /// <summary>创建示范②对局（标准指挥测试定义集；装配期注册目标合法性判定器——经既有装配期注册面）。</summary>
    private static Match CreateTargetingMatch(MockTargeterBridge bridge)
        => new(
            new CardList(Enumerable.Repeat(CommandTestKit.InfantryId, 10)),
            new CardList(Enumerable.Repeat(CommandTestKit.InfantryId, 10)),
            CommandTestKit.CreateDefinitions(),
            seed: 42,
            options: new MatchOptions { SkipMulligan = true },
            targeterBridge: bridge,
            judicatorAssembly: registry => registry.Register(
                JudicatorNames.TargetCandidateEligibility,
                new TargetEligibilityJudicator()));
}
