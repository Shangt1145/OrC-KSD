using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// Targeter 槽位验收（③）：SingleSelect／MultiSelect(min,max) 最小集；min=0 空选成功（空产出）；
/// min/max 边界与违规 Complete 的后端真实校验（拒绝＋留痕＋继续等待）；产出形态（单引用/列表/多槽位结构）；
/// 多槽位：一次交互完成全部槽位、按槽位组织提交、每槽位独立约束＋成员校验、跨槽位重复默认允许；
/// 槽位声明收尾规则：名非空/互不重复（构造期拒绝）；单槽位名可省略（归缺省）；缺省承载稳定可引用。
/// </summary>
public class TargeterSlotTests
{
    [Fact]
    public async Task Default_Slot_Is_Single_Select_Semantics_With_Default_Name()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge);

        // 未声明槽位＝缺省槽位承载（default，1..1 单一选择语义）
        var result = await manager.CreateTargeter().Targeting();

        var description = bridge.Begins[0];
        var slot = Assert.Single(description.Slots);
        Assert.Equal(TargetSlot.DefaultName, slot.Name);
        Assert.Equal(TargetSlotKind.SingleSelect, slot.Kind);
        Assert.Equal(1, slot.Min);
        Assert.Equal(1, slot.Max);

        var outcome = result.Outcome!;
        Assert.True(outcome.IsFlat);
        Assert.Same(e1.Ref, outcome.Single);
        Assert.Equal(new[] { e1.Ref }, outcome.List.ToArray());
        // 按名取出在缺省下稳定可用
        Assert.Equal(new[] { e1.Ref }, outcome.GetSelection(TargetSlot.DefaultName).ToArray());
    }

    [Fact]
    public async Task Explicit_Single_Select_Named_Slot_Uses_Declared_Name()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge);

        var result = await manager.CreateTargeter(slots: new[] { new SingleSelectSlot("target") }).Targeting();

        Assert.Equal("target", Assert.Single(bridge.Begins[0].Slots).Name);
        var outcome = result.Outcome!;
        Assert.Same(e1.Ref, outcome.Single);
        Assert.Equal(new[] { e1.Ref }, outcome.GetSelection("target").ToArray());
        // 非声明名＝明确异常（不静默）
        Assert.Throws<KeyNotFoundException>(() => outcome.GetSelection(TargetSlot.DefaultName));
    }

    [Fact]
    public async Task Slot_Name_Omitted_Or_Blank_Falls_To_Default_Name()
    {
        Assert.Equal(TargetSlot.DefaultName, new SingleSelectSlot().Name);
        Assert.Equal(TargetSlot.DefaultName, new SingleSelectSlot(null).Name);
        Assert.Equal(TargetSlot.DefaultName, new SingleSelectSlot("   ").Name);
        Assert.Equal(TargetSlot.DefaultName, new MultiSelectSlot(0, 2).Name);
        Assert.Equal("units", new MultiSelectSlot(0, 2, "units").Name);
    }

    [Fact]
    public async Task Multi_Select_Accepts_Counts_Within_Min_And_Max()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new[] { new MultiSelectSlot(1, 3) });

        // 请求 1：恰 min（1 个）
        var task1 = targeter.Targeting();
        var (d1, r1) = await bridge.WaitForNextBeginAsync();
        Assert.True(r1.Complete(d1.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        var result1 = await task1;
        Assert.Equal(new[] { e1.Ref }, result1.Outcome!.List.ToArray());

        // 请求 2：恰 max（3 个）
        var task2 = targeter.Targeting();
        var (d2, r2) = await bridge.WaitForNextBeginAsync();
        Assert.True(r2.Complete(d2.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref, e2.Ref, e3.Ref)));
        var result2 = await task2;
        Assert.Equal(new[] { e1.Ref, e2.Ref, e3.Ref }, result2.Outcome!.List.ToArray());
        Assert.Null(result2.Outcome!.Single); // 多选形态：非单值读面
    }

    [Fact]
    public async Task Multi_Select_Rejects_Below_Min_And_Above_Max_Then_Correction_Succeeds()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        var e4 = new Entity("E4");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref, e4.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var targeter = manager.CreateTargeter(slots: new[] { new MultiSelectSlot(1, 3) });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 少于 min（0 个）：拒绝、不构成终局
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName)));
        Assert.False(task.IsCompleted);

        // 超过 max（4 个）：拒绝、不构成终局
        Assert.False(responder.Complete(
            description.RequestId,
            TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref, e2.Ref, e3.Ref, e4.Ref)));
        Assert.False(task.IsCompleted);

        // 内容不合规＝显式拒绝＋留痕；请求继续等待
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Content"));

        // 前端纠正重试：合规提交 → 成功
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref, e2.Ref)));
        var result = await task;
        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.Equal(new[] { e1.Ref, e2.Ref }, result.Outcome!.List.ToArray());
    }

    [Fact]
    public async Task Multi_Select_Min_Zero_Allows_Empty_Completion_Success_With_Empty_Output()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new[] { new MultiSelectSlot(0, 2) });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        // 空选完成（min=0；缺键＝空组）＝成功、空产出（区别于"空可用集失败"）
        Assert.True(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>()));
        var result = await task;

        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.True(result.Outcome!.IsFlat);
        Assert.Empty(result.Outcome!.List);
        Assert.Empty(result.Outcome!.GetSelection(TargetSlot.DefaultName));
    }

    [Fact]
    public async Task Multi_Slot_One_Interaction_Completes_All_Slots_Organized_By_Name()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new MultiSelectSlot(1, 2, "units"),
            new SingleSelectSlot("hero"),
        });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 一次 Begin 完成全部槽位选择；提交按槽位组织
        Assert.Equal(2, description.Slots.Count);
        Assert.Equal("units", description.Slots[0].Name);
        Assert.Equal(TargetSlotKind.MultiSelect, description.Slots[0].Kind);
        Assert.Equal((1, 2), (description.Slots[0].Min, description.Slots[0].Max));
        Assert.Equal("hero", description.Slots[1].Name);
        Assert.Equal(TargetSlotKind.SingleSelect, description.Slots[1].Kind);

        var selections = new Dictionary<string, IReadOnlyList<Ref<Entity>>>
        {
            ["units"] = new[] { e1.Ref, e2.Ref },
            ["hero"] = new[] { e3.Ref },
        };
        Assert.True(responder.Complete(description.RequestId, selections));
        var result = await task;

        var outcome = result.Outcome!;
        Assert.False(outcome.IsFlat); // 多槽位＝非扁平产出
        Assert.Equal(new[] { "units", "hero" }, outcome.SlotNames.ToArray());
        Assert.Equal(new[] { e1.Ref, e2.Ref }, outcome.GetSelection("units").ToArray());
        Assert.Equal(new[] { e3.Ref }, outcome.GetSelection("hero").ToArray());
        Assert.Null(outcome.Single);
        Assert.Empty(outcome.List); // 非扁平：按槽位读取
    }

    [Fact]
    public async Task Multi_Slot_Enforces_Per_Slot_Constraints_And_Membership()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var targeter = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new MultiSelectSlot(2, 2, "units"),
            new SingleSelectSlot("hero"),
        });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // units 少于 min（1 个 < 2）：拒绝
        Assert.False(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>
        {
            ["units"] = new[] { e1.Ref },
            ["hero"] = new[] { e1.Ref },
        }));
        Assert.False(task.IsCompleted);

        // hero（Single）收到多引用：拒绝
        Assert.False(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>
        {
            ["units"] = new[] { e1.Ref, e2.Ref },
            ["hero"] = new[] { e1.Ref, e2.Ref },
        }));
        Assert.False(task.IsCompleted);

        // 未声明槽位名：拒绝
        Assert.False(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>
        {
            ["units"] = new[] { e1.Ref, e2.Ref },
            ["hero"] = new[] { e1.Ref },
            ["mystery"] = new[] { e1.Ref },
        }));
        Assert.False(task.IsCompleted);

        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Content"));

        // 修正 → 成功
        Assert.True(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>
        {
            ["units"] = new[] { e1.Ref, e2.Ref },
            ["hero"] = new[] { e3.Ref },
        }));
        var result = await task;
        Assert.Equal(TargetingStatus.Success, result.Status);
    }

    [Fact]
    public async Task Multi_Slot_Same_Reference_Selected_By_Two_Slots_Is_Allowed()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new SingleSelectSlot("a"),
            new SingleSelectSlot("b"),
        });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        // 默认允许同一引用被多个槽位同时选中（不校验跨槽重复）
        Assert.True(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>
        {
            ["a"] = new[] { e1.Ref },
            ["b"] = new[] { e1.Ref },
        }));
        var result = await task;

        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.Equal(new[] { e1.Ref }, result.Outcome!.GetSelection("a").ToArray());
        Assert.Equal(new[] { e1.Ref }, result.Outcome!.GetSelection("b").ToArray());
    }

    [Fact]
    public async Task Member_Outside_Allowed_Set_Is_Rejected_Then_Correction_Succeeds()
    {
        var e1 = new Entity("E1");
        var outside = new Entity("Outside");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var targeter = manager.CreateTargeter();

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 每个引用须属于最终允许集：外部引用＝拒绝
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, outside.Ref)));
        Assert.False(task.IsCompleted);
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Content"));

        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        var result = await task;
        Assert.Same(e1.Ref, result.Outcome!.Single);
    }

    [Fact]
    public async Task Stale_Reference_Passes_Normalization_But_Is_Rejected_At_Completion()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        e1.Destroy(); // 收集前失效：规范化只判"是否引用实例"（保留）、有效性判定下沉筛选/终局

        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var targeter = manager.CreateTargeter();

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 失效引用可规范化、保留在允许集中
        Assert.Equal(new[] { e1.Ref, e2.Ref }, description.AllowedTargets.ToArray());

        // 终局校验附加"仍有效"：失效提交＝内容不合规（拒绝＋留痕＋继续等待）
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        Assert.False(task.IsCompleted);
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Content"));

        // 有效引用照常完成
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e2.Ref)));
        var result = await task;
        Assert.Same(e2.Ref, result.Outcome!.Single);
    }

    [Fact]
    public void Slot_Declaration_Validation_Rejects_Invalid_Declarations_At_Construction()
    {
        var manager = new TargeterManager();

        // 名互不重复（两个省略名都归缺省 → 重复；同名 → 重复）
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(slots: new TargetSlot[]
        {
            new SingleSelectSlot(),
            new SingleSelectSlot(),
        }));
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(slots: new TargetSlot[]
        {
            new SingleSelectSlot("x"),
            new SingleSelectSlot("x"),
        }));

        // MultiSelect min/max 关系（构造期校验）
        Assert.Throws<ArgumentOutOfRangeException>(() => new MultiSelectSlot(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MultiSelectSlot(1, 0));
        Assert.Throws<ArgumentException>(() => new MultiSelectSlot(3, 2));

        // null 槽位元素
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(slots: new TargetSlot[] { new SingleSelectSlot(), null! }));
    }
}
