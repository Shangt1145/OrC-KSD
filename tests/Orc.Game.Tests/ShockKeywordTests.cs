using Orc.Core;
using Orc.Game;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Judicators;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 批 5（冲击词条化）验收：①条目级（带冲击攻击者→无反击资格；装配/独立构造一致）；
/// ②流程级（免反击结算——攻击者防御不变、目标受伤；压伏击——伏击者不改写、正常单方结算）；
/// ③移除时点（攻击后 Has=false；未攻击不失去；取消/复验拒绝不消耗；攻击 HQ 消耗）；
/// ④复装（消耗后重授前回归常规互伤；重授后再触发——正反向闭环）；⑤打标生效（对战词条池含 shock）。
/// 构造口径：词条经运行时授予（<c>KeywordManager.GrantAsync</c>——公开受控入口）；
/// 流程级经真实指挥流程（<c>CommandTestKit.RunCommandAsync</c>）。
/// </summary>
public class ShockKeywordTests
{
    /// <summary>真实指挥流程驱动（含交互期插入动作——执行前复验拒绝的行为观测面）。</summary>
    private static async Task<CommandResult> RunCommandWithInterludeAsync(
        Match match, MockTargeterBridge bridge, UnitCard unit, Ref<Entity> targetRef, Func<Task> interlude)
    {
        var task = match.CommandManager.BeginCommandAsync(unit);
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        await interlude();
        var accepted = responder.Complete(
            description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, targetRef));
        Assert.True(accepted, "交互完成被拒绝（目标不在候选面）。");
        return await task;
    }

    // ---------- ① 条目级＋独立构造一致 ----------

    [Fact]
    public async Task Shock_Counter_Eligibility_Entry_And_Standalone_Consistent()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var shocked = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var plain = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 2);
        var enemyInfantry = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.InfantryId, 0);

        // 打标生效：shock ∈ 对战词条池（wiki『属于对战词条』——本单全集 7 项口径）。
        Assert.True(KeywordRegistry.IsBattleKeyword(KeywordIds.Shock));

        // 条目级（经判定器条目句柄——单源）：带冲击攻击者 → 无反击资格（⓪ 条款；现势词条面读取）。
        Assert.True(await shocked.Keywords.GrantAsync(KeywordIds.Shock));
        var entry = match.Judicators.Resolve(JudicatorNames.CombatCounterEligibility);
        Assert.NotNull(entry);
        Assert.Equal(new object[] { false }, entry.Invoke(new object[] { shocked, enemyInfantry }));
        Assert.Equal(new object[] { true }, entry.Invoke(new object[] { plain, enemyInfantry })); // 对照：无冲击＝常规互伤资格

        // 独立构造（不经注册表／装配链）：内置默认通道构造即可用——行为与注册路径一致（同一判定器、现势读取）。
        var standalone = new CommandManager(
            match.Engine,
            match.Battlefield,
            match.TargeterManager,
            match.Players,
            () => match.CurrentPlayer);
        var standaloneContext = standalone.KeywordLoadContext;
        Assert.NotNull(standaloneContext.CounterEligibility);
        Assert.False(standaloneContext.CounterEligibility!(shocked, enemyInfantry));
        Assert.True(standaloneContext.CounterEligibility!(plain, enemyInfantry));
    }

    // ---------- ② 流程级：免反击结算＋消耗＋复装闭环（正反向） ----------

    [Fact]
    public async Task Shock_Process_Exemption_Consume_And_Regrant_Closed_Loop()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attacker = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1); // 攻 2 / 防 5
        var target1 = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.InfantryId, 0); // 攻 2 / 防 5
        var target2 = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.InfantryId, 1);
        var target3 = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.InfantryId, 2);
        await match.ResourceManager.AddPointsAsync(playerA, 10);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        using var recorder = new UpdateRecorder(match.Engine);

        // 首攻（冲击在场）：免反击结算——目标受伤（5-2=3）、攻击者防御不变（5）；冲击消耗（Has=false）。
        Assert.True(await attacker.Keywords.GrantAsync(KeywordIds.Shock));
        CommandTestKit.Activate(attacker);
        recorder.Clear();
        var first = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target1.Ref);
        Assert.Equal(CommandResultStatus.Success, first.Status);
        Assert.Equal(3, target1.GetData<UnitStateData>().Defense); // 目标照常受伤
        Assert.Equal(5, attacker.GetData<UnitStateData>().Defense); // 免反击：攻击者防御不变
        Assert.False(attacker.Keywords.Has(KeywordIds.Shock)); // X1：攻击即消耗（到达执行段尾部）

        // 证据（信号面——2(b) 观察预期）：仅正向 unit.damage.dealt（攻击者施动）恰一次；
        // 反向（目标施动）零发射——反击未执行。
        Assert.Equal(1, recorder.Updates.Count(u => u.Type == GameUpdates.UnitDamageDealt
            && ReferenceEquals(u.Payload![GameUpdates.PayloadUnit], attacker)));
        Assert.Equal(0, recorder.Updates.Count(u => u.Type == GameUpdates.UnitDamageDealt
            && ReferenceEquals(u.Payload![GameUpdates.PayloadUnit], target1)));

        // 消耗后（重授前）二次攻击：回归常规互伤——免反击消失、反击按默认条款恢复（攻击者 5-2=3）；无冲击可耗。
        CommandTestKit.Activate(attacker);
        var second = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target2.Ref);
        Assert.Equal(CommandResultStatus.Success, second.Status);
        Assert.Equal(3, target2.GetData<UnitStateData>().Defense);
        Assert.Equal(3, attacker.GetData<UnitStateData>().Defense); // 反击恢复（不再豁免）
        Assert.False(attacker.Keywords.Has(KeywordIds.Shock));

        // 重授后三次攻击：豁免恢复＋再消耗（Has 再 false）——复装闭环（现势读取的行为证据）。
        Assert.True(await attacker.Keywords.GrantAsync(KeywordIds.Shock));
        CommandTestKit.Activate(attacker);
        var third = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target3.Ref);
        Assert.Equal(CommandResultStatus.Success, third.Status);
        Assert.Equal(3, target3.GetData<UnitStateData>().Defense);
        Assert.Equal(3, attacker.GetData<UnitStateData>().Defense); // 豁免恢复（不再受伤）
        Assert.False(attacker.Keywords.Has(KeywordIds.Shock));
        Assert.False(attacker.GetData<UnitStateData>().IsDestroyed);
    }

    // ---------- ② 流程级：压伏击（伏击者不改写、正常单方结算） ----------

    [Fact]
    public async Task Shock_Suppresses_Ambush_Rewrite_With_Single_Sided_Settlement()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var shocked = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.WeakId, 1); // 攻 1 / 防 2（冲击）
        var weak = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.WeakId, 2); // 攻 1 / 防 2（对照）
        var ambusher1 = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.AmbushId, 0); // 攻 5 / 防 6（伏击）
        var ambusher2 = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.AmbushId, 1); // 攻 5 / 防 6（伏击）
        await match.ResourceManager.AddPointsAsync(playerA, 10);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        // 对照（无冲击——伏击条件本应命中：5 ＞ 2）：改写成立——攻击者死亡、伏击者不受伤（证明场景有效）。
        CommandTestKit.Activate(weak);
        var control = await CommandTestKit.RunCommandAsync(match, bridge, weak, ambusher1.Ref);
        Assert.Equal(CommandResultStatus.Success, control.Status);
        Assert.True(weak.GetData<UnitStateData>().IsDestroyed);
        Assert.Equal(6, ambusher1.GetData<UnitStateData>().Defense); // 伏击者不受伤（改写成立）

        // 冲击压止（授予冲击）：先资格翻转（C5 判定 false）→ 伏击不改写、正常单方结算——
        // 目标扣防受伤（6-1=5）、反击不执行（同一判定 false——攻击者防御不变 2）；尾部消耗（Has=false）。
        Assert.True(await shocked.Keywords.GrantAsync(KeywordIds.Shock));
        CommandTestKit.Activate(shocked);
        var suppressed = await CommandTestKit.RunCommandAsync(match, bridge, shocked, ambusher2.Ref);
        Assert.Equal(CommandResultStatus.Success, suppressed.Status);
        Assert.Equal(5, ambusher2.GetData<UnitStateData>().Defense); // 伏击者受伤（改写未成立）
        Assert.False(ambusher2.GetData<UnitStateData>().IsDestroyed);
        Assert.Equal(2, shocked.GetData<UnitStateData>().Defense); // 攻击者防御不变（反击不执行）
        Assert.False(shocked.GetData<UnitStateData>().IsDestroyed);
        Assert.False(shocked.Keywords.Has(KeywordIds.Shock)); // 攻击伏击者＝单位路径——消耗
    }

    // ---------- ③ 移除时点：未攻击／取消／复验拒绝＝不消耗 ----------

    [Fact]
    public async Task Shock_Not_Consumed_Without_Reaching_Execution_Tail()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var victim = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.InfantryId, 0); // 攻 2 / 防 5
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        // ① 未攻击不失去（基础）：授予后无任何操作——保持。
        var idle = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        Assert.True(await idle.Keywords.GrantAsync(KeywordIds.Shock));
        Assert.True(idle.Keywords.Has(KeywordIds.Shock));

        // ② 目标选择取消不消耗（拖回——仅本次取消、零副作用）：冲击保持、双方状态不变。
        var canceller = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 2);
        Assert.True(await canceller.Keywords.GrantAsync(KeywordIds.Shock));
        CommandTestKit.Activate(canceller);
        var pointsBeforeCancel = playerA.Points;
        var cancelled = await CommandTestKit.CancelCommandAsync(match, bridge, canceller);
        Assert.Equal(CommandResultStatus.Cancelled, cancelled.Status);
        Assert.True(canceller.Keywords.Has(KeywordIds.Shock)); // 取消＝未达尾部、不消耗
        Assert.Equal(5, victim.GetData<UnitStateData>().Defense); // 零副作用：未结算
        Assert.Equal(pointsBeforeCancel, playerA.Points); // 零副作用：未扣费

        // ③ 复验拒绝不消耗（交互期费用漂移 → 执行前复验拒绝、零副作用＋留痕）：冲击保持。
        var rerouted = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.True(await rerouted.Keywords.GrantAsync(KeywordIds.Shock));
        CommandTestKit.Activate(rerouted);
        await match.ResourceManager.AddPointsAsync(playerA, 1);
        var pointsBeforeReject = playerA.Points;
        var costSource = new object();
        var rejected = await RunCommandWithInterludeAsync(
            match, bridge, rerouted, victim.Ref,
            () => rerouted.Modifiers.AddModifierAsync(new AddModifier(CardStatFields.OperateCost, 5, costSource)));
        Assert.Equal(CommandResultStatus.Failed, rejected.Status);
        Assert.Equal(CommandFailureReason.ExecutionRejected, rejected.FailureReason);
        Assert.True(rerouted.Keywords.Has(KeywordIds.Shock)); // 复验拒绝＝未达尾部、不消耗
        Assert.Equal(5, victim.GetData<UnitStateData>().Defense); // 零副作用：未结算
        Assert.Equal(pointsBeforeReject, playerA.Points); // 零副作用：未扣费
    }

    // ---------- ③ 移除时点：HQ 攻击消耗（含 HQ 路径——同一公共尾部） ----------

    [Fact]
    public async Task Shock_Consumed_On_Hq_Attack()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var artillery = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.ArtilleryId, 1); // 攻 2 / 防 2（炮兵——任意线含 HQ）
        await match.ResourceManager.AddPointsAsync(playerA, 1);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        // 授予冲击；攻击敌方 HQ（简路——不反击、不经互伤链）：到达执行段公共尾部＝消耗（HQ 路径与单位路径同尾部）。
        Assert.True(await artillery.Keywords.GrantAsync(KeywordIds.Shock));
        CommandTestKit.Activate(artillery);
        var hqBefore = playerB.HqHealth;
        var result = await CommandTestKit.RunCommandAsync(match, bridge, artillery, playerB.Hq.Ref);
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.Equal(hqBefore - 2, playerB.HqHealth); // 伤害＝攻击力有效值（2）
        Assert.Equal(2, artillery.GetData<UnitStateData>().Defense); // 攻击者不受伤害（HQ 不反击）
        Assert.False(artillery.Keywords.Has(KeywordIds.Shock)); // HQ 攻击消耗（同一尾部）
    }
}
