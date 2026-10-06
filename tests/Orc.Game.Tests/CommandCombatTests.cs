using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2C 验收③（攻击结算；后置项 A/B 适配后）：互伤（同时结算、以互扣前实时值为基准；豁免表全组合见 CommandImmunityTests）；
/// 死亡清理最小口径（槽位释放 / IsDestroyed / Position 置空 / 实例保留可查询 / 不发 position.changed / card.died 恰一次）；
/// 同归于尽（两枚 card.died、被攻击者在前）；HQ 归零＝立即终局（钳制到 0、不反击、状态置结束＋胜者；当次收尾照常）；
/// 攻击者死亡仍成功（照扣费、照清位）；伏击改写（先资格〔反击豁免表〕后条件：成立＝攻击者死·目标不受伤；
/// 不成立＝正常互伤与无资格；攻击者侧伏击不参与；死亡后不再改写）。
/// </summary>
public class CommandCombatTests
{
    [Fact]
    public async Task Mutual_Destruction_Emits_Two_Died_With_Target_First()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        // 适配（后置项 A）：原「炮兵 × 炮兵」组合现属豁免表（攻击者＝炮兵不受任何反击、不会互伤）——
        // 迁移为非豁免组合「战斗机（攻 3 / 防 2）× 炮兵（攻 2 / 防 2）」（覆盖语义保持：互伤同归）。
        var attacker = await CommandTestKit.PrepareOnFrontAsync(match, playerA, CommandTestKit.FighterId, 0); // 攻 3 / 防 2
        var target = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.ArtilleryId, 2); // 攻 2 / 防 2
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target.Ref);

        // 同时结算（互扣前实时值）→ 同归于尽（战斗机攻 3 ≥ 炮兵防 2；炮兵反击 2 ≥ 战斗机防 2）：
        // 两枚 card.died 均发射，顺序＝被攻击者在前、攻击者在后。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        var diedEvents = recorder.Updates.Where(u => u.Type == GameUpdates.CardDied).ToList();
        Assert.Equal(2, diedEvents.Count);
        Assert.Same(target, diedEvents[0].Payload![GameUpdates.PayloadCard]);
        Assert.Same(attacker, diedEvents[1].Payload![GameUpdates.PayloadCard]);
        Assert.True(target.GetData<UnitStateData>().IsDestroyed);
        Assert.True(attacker.GetData<UnitStateData>().IsDestroyed);
    }

    [Fact]
    public async Task Death_Cleanup_Frees_Slot_Keeps_Instance_And_Skips_Position_Update()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attacker = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1); // 攻 2 / 防 5
        var target = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.WeakId, 0); // 攻 1 / 防 2
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        using var recorder = new UpdateRecorder(match.Engine);
        var targetSlot = match.Battlefield.FrontLine[0];

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target.Ref);

        // 死亡清理：槽位释放（变空槽）；IsDestroyed 置位；Position 置空、实例保留可查询；card.died 恰一次；
        // 不发 unit.position.changed（该更新专属移动语义）。
        // W2b 随改：防御扣减经门户→跑链集中触发——被攻击者（2→0）与攻击者（5→4）各一条 card.stat.changed；
        // 「先数值变化、后死亡信号」：被攻击者的 sc（外部订阅通知）先于 card.died（防御归零的统一死亡衔接——总线触发器阶段），
        // 攻击者的 sc 随后；恰一次死亡。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.True(targetSlot.IsEmpty);
        Assert.True(target.GetData<UnitStateData>().IsDestroyed);
        Assert.Null(target.GetData<UnitStateData>().Position);
        Assert.True(target.Ref.IsAlive); // 实例保留（不销毁）——可查询
        Assert.Equal(
            new[]
            {
                GameUpdates.CardStatChanged, GameUpdates.CardDied, GameUpdates.CardDamaged,
                GameUpdates.CardStatChanged, GameUpdates.CardDamaged, // E1-33：受伤害（先数值、后伤害信号）
                GameUpdates.PointChanged, // 收尾扣行动费（E1-25 后续）
                GameUpdates.UnitActed,    // E1-33：攻击者行动后（存活）
            },
            recorder.Types);
        var diedPayload = recorder.Updates[1].Payload!;
        Assert.Same(target, diedPayload[GameUpdates.PayloadCard]);
        Assert.DoesNotContain(GameUpdates.UnitPositionChanged, recorder.Types);
        // 攻击者收尾照常（未死）：扣费＋清位。
        Assert.Equal(0, playerA.Points);
        Assert.False(attacker.GetData<CommandData>().CanAttack);
    }

    [Fact]
    public async Task Dead_Attacker_Still_Resolves_As_Success_With_Fee_And_Flag_Updates()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        // 适配（后置项 A）：原「炮兵 × 炮兵」组合现属豁免表（攻击者＝炮兵不受任何反击——攻击者不会死亡）——
        // 迁移为非豁免组合「脆皮步兵（攻 1 / 防 2）× 炮兵（攻 2 / 防 2）」：以攻击者死亡告终（覆盖语义保持）。
        var attacker = await CommandTestKit.PrepareOnFrontAsync(match, playerA, CommandTestKit.WeakId, 0);
        var target = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.ArtilleryId, 2);
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, target.Ref);

        // 攻击者死亡的攻击仍为「成功」（结算完整完成）；收尾照常：扣费（不因死亡豁免）＋清位照写（统一执行、不特判）。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.True(attacker.GetData<UnitStateData>().IsDestroyed);
        Assert.Equal(0, playerA.Points);
        Assert.False(attacker.GetData<CommandData>().CanAttack);
        Assert.False(attacker.GetData<CommandData>().CanMove);
    }

    [Fact]
    public async Task Hq_Damage_Is_Clamped_To_Zero_Without_Counterattack()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var mega = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.MegaId, 1); // 攻 30 / 防 30（炮兵）
        CommandTestKit.Activate(mega);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        var enemyHq = playerB.Hq; // W3-3：HQ 目标＝实体引用

        var result = await CommandTestKit.RunCommandAsync(match, bridge, mega, enemyHq.Ref);

        // 伤害＝实时攻击力（20-30 → 钳制到 0）；HQ 不反击（攻击者不受伤害）；HQ 生命≤0 → 立即终局
        // （状态＝结束＋胜者＝攻击方）。当次结算收尾照常完成（扣费＋清位）。
        // W3-3：伤害经 HQ 数值路径与管线（改写→介入→应用→跑链）→ 归零统一响应（判定归 HQ 侧）。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.Equal(0, playerB.HqHealth);
        Assert.Equal(30, mega.GetData<UnitStateData>().Defense);
        Assert.Equal(MatchState.Ended, match.State);
        Assert.Same(playerA, match.Winner);
        Assert.Same(enemyHq, match.Battlefield.PlayerBSupportLine[0].Occupant); // HQ 占位不变（占位者＝HQ 实体）
        Assert.Equal(0, playerA.Points); // 当次收尾照常：扣费恰一次
        Assert.False(mega.GetData<CommandData>().CanAttack); // 清位照常
        Assert.False(mega.GetData<CommandData>().CanMove);
    }

    [Fact]
    public async Task Ambush_Rewrites_When_Target_Attack_Exceeds_Attacker_Defense()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attacker = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.WeakId, 1); // 攻 1 / 防 2
        var ambusher = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.AmbushId, 0); // 攻 5 / 防 6（伏击）
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, ambusher.Ref);

        // 伏击改写：被攻击单位实时攻击力（5）＞攻击者实时防御力（2）→ 攻击者死亡（统一死亡流程、无 HP 逐步扣减）、
        // 被攻击者完全不受伤；其他结算不发生。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.True(attacker.GetData<UnitStateData>().IsDestroyed);
        Assert.Equal(2, attacker.GetData<UnitStateData>().Defense); // 无 HP 扣减（直接死亡结果）
        Assert.Equal(6, ambusher.GetData<UnitStateData>().Defense); // 目标不受伤
        Assert.Equal(new[] { GameUpdates.CardDied, GameUpdates.PointChanged }, recorder.Types); // 末条＝收尾扣行动费
        Assert.Equal(0, playerA.Points); // 攻击者死亡不豁免收尾
    }

    [Fact]
    public async Task Ambush_Not_Triggered_When_Condition_Fails_Falls_Back_To_Default_Damage()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attacker = await CommandTestKit.PrepareOnFrontAsync(match, playerA, CommandTestKit.BeastId, 0); // 攻 6 / 防 7
        var ambusher = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.AmbushId, 2); // 攻 5 / 防 6
        CommandTestKit.Activate(attacker);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attacker, ambusher.Ref);

        // 条件不成立（5 ＞ 7 为假）＝正常基础互伤：伏击兵 6-6=0 死；攻击者 7-5=2。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.True(ambusher.GetData<UnitStateData>().IsDestroyed);
        Assert.Equal(2, attacker.GetData<UnitStateData>().Defense);
    }

    [Fact]
    public async Task Ambush_Is_Defense_Side_Only_Attacker_Ambush_Does_Not_Trigger()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var attackerAmbush = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.AmbushId, 1); // 攻 5 / 防 6（伏击——攻击者侧）
        var target = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.InfantryId, 0); // 攻 2 / 防 5
        CommandTestKit.Activate(attackerAmbush);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        var result = await CommandTestKit.RunCommandAsync(match, bridge, attackerAmbush, target.Ref);

        // 攻击者侧伏击不参与（伏击为防御方特性）＝正常互伤：目标 5-5=0 死；攻击者 6-2=4（受反伤）。
        Assert.Equal(CommandResultStatus.Success, result.Status);
        Assert.True(target.GetData<UnitStateData>().IsDestroyed);
        Assert.Equal(4, attackerAmbush.GetData<UnitStateData>().Defense);
    }

    [Fact]
    public async Task Ambush_No_Longer_Rewrites_After_Death()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var killer = await CommandTestKit.PrepareOnFrontAsync(match, playerA, CommandTestKit.BeastId, 0); // 攻 6 / 防 7
        var ambusher = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.AmbushId, 2); // 攻 5 / 防 6
        CommandTestKit.Activate(killer);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);

        // 先杀死伏击兵（互伤：防 6-6=0）——死亡后逻辑组件注销、不再参与结算。
        var attackResult = await CommandTestKit.RunCommandAsync(match, bridge, killer, ambusher.Ref);
        Assert.Equal(CommandResultStatus.Success, attackResult.Status);
        Assert.True(ambusher.GetData<UnitStateData>().IsDestroyed);

        // 手动驱动「造成攻击伤害」（模拟对局内结算路径）：探针攻击者满足伏击条件（防 2 ＜ 伏击兵攻 5），
        // 但伏击兵已死 → 不再改写（resolution 未被置位）。
        var probe = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.WeakId, 3);
        var resolution = new AttackDamageResolution();
        await match.CommandManager.AttackDamageTrigger.InvokeAsync(
            match.Engine,
            new Dictionary<string, object?>
            {
                [CommandDataKeys.Attacker] = probe.Ref,
                [CommandDataKeys.Target] = ambusher.Ref,
                [CommandDataKeys.Resolution] = resolution,
            });

        Assert.False(resolution.IsRewritten);
    }
}
