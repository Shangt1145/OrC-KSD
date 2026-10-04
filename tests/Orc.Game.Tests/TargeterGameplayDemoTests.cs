using Orc.Cards;
using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// G10 演示场景（①群体 ②相邻 ④手牌选择 ⑤多选启用·游戏层端到端）：
/// 演示全部落在 tests（"效果侧"＝测试中充当"效果逻辑消费方"的代码——构造对局 → 自组目标集 → 直接应用 → 断言；
/// 不新增生产文件、不新增框架 API/机制）；动作一律经既有对外/受控面（受控数值面/加入面），不绕过受控面直改内部状态。
/// ① 群体＝遍历应用（自组目标集＝全体友方单位〔遍历既有查询面〕；每目标恰一次；结果可断言；非目标不受影响）；
/// ② 相邻＝查询面应用（相邻单位查询＋受控变更；相邻空槽查询＋加入面）；
/// ④ 手牌选择＝真实手牌卡端到端（呈现描述可用＋产出卡引用＋"离手被拒"可观测）；
/// ⑤ 多选启用＝测试内驱动既有 MultiSelectSlot（游戏层设施构造端到端：提交成功/产出多引用
///〔提交序〕/数量边界〔超上限拒绝、min=0 空选成功〕/既有型行为不受影响）。
/// 注：随机形态属 B1/G8、不在本单演示；本单自动形态仅群体/相邻。
/// </summary>
public class TargeterGameplayDemoTests
{
    // ---------- ① 群体（遍历应用） ----------

    [Fact]
    public async Task Group_Demo_Traversal_Applies_Controlled_Change_To_Each_Ally_Once()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();

        var playerA = match.Players[0];
        var playerB = match.Players[1];

        // 构造对局：玩家A 三个单位（支援线 1..3）＋玩家B 一个单位（不参与目标集）
        var a1 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var a2 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 2);
        var a3 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 3);
        var b1 = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.InfantryId, 1);

        // 自组目标集（效果逻辑消费方）＝遍历既有查询面：全在场单位 → 筛归属
        var targets = CommandTestKit.AllUnitsOf(match)
            .Where(u => ReferenceEquals(u.Owner, playerA))
            .ToList();
        Assert.Equal(new[] { a1, a2, a3 }, targets.ToArray());

        using var recorder = new UpdateRecorder(match.Engine);
        var source = new object();

        // 直接应用（受控数值面，不经受控面之外的任何写入）：遍历目标集逐一应用（"全体友方攻击力 +1"）
        foreach (var unit in targets)
        {
            await unit.Modifiers.AddModifierAsync(new AddModifier(CardStatFields.Attack, 1, source));
        }

        // 结果可断言：目标准确（集合成员全中——每目标 +1，基准 2 → 3）；非目标不受影响
        Assert.Equal(3, a1.Modifiers.GetEffectiveValue(CardStatFields.Attack));
        Assert.Equal(3, a2.Modifiers.GetEffectiveValue(CardStatFields.Attack));
        Assert.Equal(3, a3.Modifiers.GetEffectiveValue(CardStatFields.Attack));
        Assert.Equal(2, b1.Modifiers.GetEffectiveValue(CardStatFields.Attack));

        // 每目标被应用恰一次（各一条集中触发；总条数＝目标数；无重复、无遗漏）
        var changed = recorder.Updates.Where(u => u.Type == GameUpdates.CardStatChanged).ToList();
        Assert.Equal(3, changed.Count);
        foreach (var unit in targets)
        {
            Assert.Single(changed, u => ReferenceEquals(u.Payload![GameUpdates.PayloadCard], unit));
        }
    }

    // ---------- ② 相邻（查询面应用） ----------

    [Fact]
    public async Task Adjacent_Demo_Query_Neighbors_Then_Apply_Controlled_Change()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();

        var playerA = match.Players[0];
        var u1 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var u2 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 2);
        var u3 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 3);

        // 相邻查询面（既有）：u2 的相邻单位（同线索引差 1；左邻在前、右邻在后）
        var neighbors = match.Environment.GetAdjacentUnits(u2);
        Assert.Equal(new[] { u1, u3 }, neighbors.ToArray());

        // 得集后应用（受控变更——"对相邻者各造成 1 点伤害"）：防御 5 → 4
        foreach (var neighbor in neighbors)
        {
            await neighbor.ApplyDefenseDamageAsync(1);
        }

        // 结果可断言：相邻者变化、自身与不相邻者不变
        Assert.Equal(4, u1.GetData<UnitStateData>().Defense);
        Assert.Equal(4, u3.GetData<UnitStateData>().Defense);
        Assert.Equal(5, u2.GetData<UnitStateData>().Defense);
    }

    [Fact]
    public async Task Adjacent_Empty_Slot_Query_Demo_Then_Join_Via_Existing_Placement_Face()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();

        var playerA = match.Players[0];
        var line = match.Battlefield.GetSupportLine(playerA);
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);

        // 相邻空槽查询面（既有）：部署候选位（被占槽位的邻位空槽——HQ 占位与单位均在列）
        var candidates = line.GetAdjacentEmptySlots();
        Assert.Equal(new[] { line[2] }, candidates.ToArray());

        // 得集后应用（既有加入面——"放置到相邻空槽"）：新单位加入该槽
        var rook = await CommandTestKit.InstantiateLoadedAsync(match, playerA, CommandTestKit.InfantryId);
        var joined = await match.PlayManager.JoinUnitAsync(rook, candidates[0]);
        Assert.Equal(PlayResultStatus.Success, joined.Status);
        Assert.Same(rook, candidates[0].Occupant);

        // 变化可观测：候选面随布局移动（原槽被占后不再候选）
        Assert.Equal(new[] { line[3] }, line.GetAdjacentEmptySlots().ToArray());
    }

    // ---------- ④ 手牌选择（游戏层端到端：真实手牌卡） ----------

    [Fact]
    public async Task Hand_Select_Demo_With_Real_Cards_Presentation_Output_And_Leave_Hand_Rejection()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();

        var playerA = match.Players[0];
        var cardA = await CommandTestKit.InstantiateLoadedAsync(match, playerA, CommandTestKit.InfantryId, toHand: true);
        var cardB = await CommandTestKit.InstantiateLoadedAsync(match, playerA, CommandTestKit.InfantryId, toHand: true);

        // 构造方从对局状态构造（后端侧）：允许集＝己方手牌卡引用（请求期快照）；域判定＝"仍在手牌"
        var handSnapshot = playerA.Hand.Select(card => card.Ref).ToList();
        var context = new TargetingRequestContext()
            .WithSlotReferences("hand", handSnapshot)
            .WithSlotDomainValidator("hand", r => r.IsAlive && r.Value is Card card && playerA.Hand.Contains(card));

        var targeter = match.TargeterManager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(1, 1, "hand") },
            context: context);

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 呈现描述可用：专属形态标注（区别于场上目标点选）；允许集为卡引用（非场上实体）
        var slot = Assert.Single(description.Slots);
        Assert.Equal(TargetSlotPresentation.HandSelect, slot.Presentation);
        Assert.NotNull(slot.AllowedReferences);
        Assert.Equal(handSnapshot.ToArray(), slot.AllowedReferences!.ToArray());
        Assert.All(slot.AllowedReferences!, r => Assert.IsAssignableFrom<Card>(r.Value));

        // 请求期内离手（从手牌移除）→ 提交该卡＝拒绝（域判定"仍在手牌"；不构成终局、继续等待）
        playerA.Hand.Remove(cardA);
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", cardA.Ref)));
        Assert.False(task.IsCompleted);

        // 仍在手牌者 → 确认成功；产出＝卡引用（读面可读、可解引用）
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", cardB.Ref)));
        var result = await task;

        var selected = Assert.Single(result.Outcome!.GetSelection("hand"));
        Assert.Same(cardB.Ref, selected);
        Assert.Same(cardB, selected.Value);
    }

    // ---------- ⑤ 多选启用（游戏层设施驱动） ----------

    [Fact]
    public async Task Multi_Select_Enabled_Via_Game_Layer_Facilities_With_Count_Boundary_And_Legacy_Intact()
    {
        var bridge = new MockTargeterBridge();
        var match = CommandTestKit.CreateCommandMatch(bridge);
        await match.Initialize();

        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var u1 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var u2 = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 2);
        var u3 = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.InfantryId, 1);

        // 候选脚本（游戏层设施）：全引用超集；后端无筛选＝全通过
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        var pool = CommandTestKit.AllRefsOf(match).OfType<Ref<Entity>>().ToList();
        Assert.True(pool.Count >= 4, "候选超集须足以覆盖数量边界用例。");

        // —— 多选成功（≥2 引用；提交序、读面可读）与数量边界（超上限拒绝）——
        var multi = match.TargeterManager.CreateTargeter(slots: new TargetSlot[] { new MultiSelectSlot(2, 3, "units") });
        var task1 = multi.Targeting();
        var (description1, responder1) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(TargetSlotKind.MultiSelect, description1.Slots[0].Kind);
        Assert.Equal((2, 3), (description1.Slots[0].Min, description1.Slots[0].Max));

        // 超上限（4 > 3）→ 拒绝（数量约束；不构成终局、继续等待）
        Assert.False(responder1.Complete(
            description1.RequestId,
            TargeterTestKit.Selection("units", pool[0], pool[1], pool[2], pool[3])));
        Assert.False(task1.IsCompleted);

        // 修正（3 个）→ 成功；产出＝多引用列表（提交序）
        Assert.True(responder1.Complete(
            description1.RequestId,
            TargeterTestKit.Selection("units", u1.Ref, u2.Ref, u3.Ref)));
        var result1 = await task1;

        Assert.Equal(TargetingStatus.Success, result1.Status);
        Assert.Equal(new[] { u1.Ref, u2.Ref, u3.Ref }, result1.Outcome!.GetSelection("units").ToArray());
        Assert.Equal(new[] { u1.Ref, u2.Ref, u3.Ref }, result1.Outcome!.List.ToArray());
        Assert.Null(result1.Outcome!.Single); // 多选形态：非单值读面

        // —— 数量边界：min=0 空选成功（空产出）——
        var optional = match.TargeterManager.CreateTargeter(slots: new TargetSlot[] { new MultiSelectSlot(0, 2, "units") });
        var task2 = optional.Targeting();
        var (description2, responder2) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder2.Complete(description2.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>()));
        var result2 = await task2;

        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Empty(result2.Outcome!.GetSelection("units"));

        // —— 既有型行为不受影响：缺省单选照常可用 ——
        var legacy = match.TargeterManager.CreateTargeter();
        var task3 = legacy.Targeting();
        var (description3, responder3) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(TargetSlotKind.SingleSelect, Assert.Single(description3.Slots).Kind);
        Assert.True(responder3.Complete(description3.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, u1.Ref)));
        var result3 = await task3;

        Assert.Equal(TargetingStatus.Success, result3.Status);
        Assert.Same(u1.Ref, result3.Outcome!.Single);
    }
}
