using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// Targeter 槽位类型族验收（③④⑥⑦·机制级）：选项槽位（声明条目集/非引用候选/恰选 1/标识唯一/禁空集；
/// 请求描述含条目〔标识＋文本〕、产出含且仅含选中标识、按 id 分派分支）；手牌选择槽位（请求构造期强制
/// 允许集与域判定面；专属呈现标注；快照语义；离手拒绝；判定异常＝失败结局；空允许集＝失败不进交互；min..max）；
/// 卡牌选择器槽位（名单/引用集两形态；构造期声明二择一；载荷匹配；空集语义分途〔名单空＝构造期错误、
/// 引用集空＝失败〕；呈现＝屏中卡牌阵列〔卡名级要素〕）；混合槽位最小端到端（选项＋既有单选引用：
/// 收集恰一次、单次提交两类元素、按名读取两类产出且类别可辨）。
/// </summary>
public class TargeterSlotFamilyTests
{
    // ---------- ① 选项槽位（抉择） ----------

    [Fact]
    public void Option_Slot_Declaration_Validation_Rejects_Invalid_Configurations_At_Construction()
    {
        // 条目字段：标识/文本均非空
        Assert.Throws<ArgumentException>(() => new OptionEntry(string.Empty, "文本"));
        Assert.Throws<ArgumentException>(() => new OptionEntry("id", " "));
        Assert.Throws<ArgumentNullException>(() => new OptionEntry(null!, "文本"));
        Assert.Throws<ArgumentNullException>(() => new OptionEntry("id", null!));

        // 声明集为空＝构造期错误（非引用类声明集为空）
        Assert.Throws<ArgumentException>(() => new OptionSelectSlot(Array.Empty<OptionEntry>()));

        // 标识重复＝构造期拒绝（文本不要求唯一）
        Assert.Throws<ArgumentException>(() => new OptionSelectSlot(new[]
        {
            new OptionEntry("dup", "相同文本"),
            new OptionEntry("dup", "相同文本"),
        }));

        // null 条目＝构造期拒绝
        Assert.Throws<ArgumentException>(() => new OptionSelectSlot(new OptionEntry[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new OptionSelectSlot(null!));

        // 合法声明：文本可重复；声明序保持；省略名归缺省
        var slot = new OptionSelectSlot(new[]
        {
            new OptionEntry("a", "相同文本"),
            new OptionEntry("b", "相同文本"),
        });
        Assert.Equal(TargetSlot.DefaultName, slot.Name);
        Assert.Equal(new[] { "a", "b" }, slot.Options.Select(o => o.Id).ToArray());
    }

    [Fact]
    public async Task Option_Slot_Begin_Delivers_Entries_And_Completion_Yields_Selected_Identifier()
    {
        var bridge = new MockTargeterBridge(); // 无收集脚本——本请求不应触发收集
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new OptionSelectSlot(new[]
            {
                new OptionEntry("save", "保命"),
                new OptionEntry("draw", "抽牌"),
            }, "choice"),
        });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 非引用请求：不要求桥接收集调用（不含收集需求）
        Assert.Empty(bridge.CollectCalls);

        // 请求描述交付条目（标识与文本均可断言；顺序＝声明序）；呈现＝选项列表
        var slot = Assert.Single(description.Slots);
        Assert.Equal("choice", slot.Name);
        Assert.Equal(TargetSlotKind.OptionSelect, slot.Kind);
        Assert.Equal(TargetSlotPresentation.OptionList, slot.Presentation);
        Assert.Equal((1, 1), (slot.Min, slot.Max));
        Assert.NotNull(slot.Options);
        Assert.Equal(new[] { "save", "draw" }, slot.Options!.Select(o => o.Id).ToArray());
        Assert.Equal(new[] { "保命", "抽牌" }, slot.Options!.Select(o => o.Text).ToArray());
        Assert.Null(slot.AllowedReferences); // 分类承载：非引用不经允许集通道
        Assert.Empty(description.AllowedTargets); // 无收集＝无全局允许子集

        // 单次提交（标识元素）→ 成功终局
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("choice", "draw")));
        var result = await task;

        Assert.Equal(TargetingStatus.Success, result.Status);
        var outcome = result.Outcome!;

        // 产出含且仅含选中标识（文本等呈现数据不进产出）；类别可辨、读面分类别
        Assert.Equal(new[] { "draw" }, outcome.GetIdentifiers("choice").ToArray());
        Assert.Equal(TargetSlotKind.OptionSelect, outcome.GetSlotKind("choice"));
        Assert.Throws<InvalidOperationException>(() => outcome.GetSelection("choice"));
        Assert.True(outcome.IsFlat);
        Assert.Null(outcome.Single);
        Assert.Empty(outcome.List);

        // 消费方按 id 分派分支（"分支"＝按标识分派的行为约定、非条目字段）
        var dispatched = outcome.GetIdentifiers("choice")[0] switch
        {
            "save" => "branch-save",
            "draw" => "branch-draw",
            _ => "branch-unknown",
        };
        Assert.Equal("branch-draw", dispatched);
    }

    [Fact]
    public async Task Option_Slot_Rejects_Unknown_Or_Empty_Identifier_And_Wrong_Counts_Then_Correction_Succeeds()
    {
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge, trace);
        var targeter = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new OptionSelectSlot(new[] { new OptionEntry("a", "甲"), new OptionEntry("b", "乙") }, "choice"),
        });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 标识 ∉ 声明集 → 拒绝（不构成终局、继续等待）
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("choice", "unknown")));
        Assert.False(task.IsCompleted);

        // 标识为空（空白/null）→ 同路径拒绝
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("choice", "")));
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("choice", new string[] { null! })));
        Assert.False(task.IsCompleted);

        // 缺键＝空组 → 少于 min 拒绝；多于 max 拒绝（恰选 1）
        Assert.False(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<TargetSelection>>()));
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("choice", "a", "b")));
        Assert.False(task.IsCompleted);

        // 元素类别与槽位类别不匹配：引用元素提交到非引用槽位 → 拒绝
        var e1 = new Entity("E1");
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("choice", e1.Ref)));
        Assert.False(task.IsCompleted);

        // 内容不合规＝显式拒绝＋留痕
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Content"));

        // 前端纠正重试：合规提交 → 成功
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("choice", "a")));
        var result = await task;
        Assert.Equal(new[] { "a" }, result.Outcome!.GetIdentifiers("choice").ToArray());
    }

    // ---------- ② 手牌选择槽位 ----------

    [Fact]
    public void Hand_Slot_Request_Construction_Requires_References_And_Domain_Validator()
    {
        var manager = new TargeterManager();

        // 无请求级数据 → 拒绝（手牌槽位强制绑定）
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new HandSelectSlot(1, 1, "hand") }));
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new HandSelectSlot(1, 1, "hand") },
            context: new TargetingRequestContext()));

        // 有判定面、无允许集 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new HandSelectSlot(1, 1, "hand") },
            context: new TargetingRequestContext().WithSlotDomainValidator("hand", _ => true)));

        // 有允许集、无判定面 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new HandSelectSlot(1, 1, "hand") },
            context: new TargetingRequestContext().WithSlotReferences("hand", Array.Empty<Ref<Entity>>())));

        // 绑定未声明的槽位名 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new HandSelectSlot(1, 1, "hand") },
            context: new TargetingRequestContext()
                .WithSlotReferences("other", Array.Empty<Ref<Entity>>())
                .WithSlotDomainValidator("hand", _ => true)));

        // 绑定类别错配：引用集绑到既有单选槽位 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new SingleSelectSlot("unit") },
            context: new TargetingRequestContext().WithSlotReferences("unit", Array.Empty<Ref<Entity>>())));

        // 域判定面绑到非引用槽位 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new OptionSelectSlot(new[] { new OptionEntry("a", "甲") }, "choice") },
            context: new TargetingRequestContext().WithSlotDomainValidator("choice", _ => true)));

        // 重复绑定 → 拒绝
        var context = new TargetingRequestContext();
        context.WithSlotReferences("hand", Array.Empty<Ref<Entity>>());
        Assert.Throws<ArgumentException>(() => context.WithSlotReferences("hand", Array.Empty<Ref<Entity>>()));

        // 合法：允许集＋判定面齐备（允许集可为空集——空集＝执行时失败、不属构造期）
        var valid = manager.CreateTargeter(
            slots: new[] { new HandSelectSlot(1, 1, "hand") },
            context: new TargetingRequestContext()
                .WithSlotReferences("hand", Array.Empty<Ref<Entity>>())
                .WithSlotDomainValidator("hand", _ => true));
        Assert.NotNull(valid);
    }

    [Fact]
    public async Task Hand_Slot_Delivers_Snapshot_And_Dedicated_Presentation_And_Yields_Card_Reference()
    {
        var e1 = new Entity("手牌1");
        var e2 = new Entity("手牌2");
        var inHand = new List<Ref<Entity>> { e1.Ref, e2.Ref };
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge);
        var context = new TargetingRequestContext()
            .WithSlotReferences("hand", inHand)
            .WithSlotDomainValidator("hand", r => inHand.Contains(r));

        var targeter = manager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(1, 1, "hand") },
            context: context);

        // 快照语义：发起后修改源集合不影响允许集（新增不进入可选范围）
        inHand.Add(new Entity("后来").Ref);

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 无收集需求：不要求桥接收集调用
        Assert.Empty(bridge.CollectCalls);

        // 专属呈现标注（可断言：区别于场上目标点选）；允许子集＝请求期快照（未经域判定过滤）
        var slot = Assert.Single(description.Slots);
        Assert.Equal(TargetSlotKind.HandSelect, slot.Kind);
        Assert.Equal(TargetSlotPresentation.HandSelect, slot.Presentation);
        Assert.NotNull(slot.AllowedReferences);
        Assert.Equal(new[] { e1.Ref, e2.Ref }, slot.AllowedReferences!.ToArray());
        Assert.Empty(description.AllowedTargets); // 无收集＝无全局允许子集（新引用类经槽位描述交付）

        // 提交卡引用 → 成功；产出＝卡引用（Ref 读面）
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", e2.Ref)));
        var result = await task;

        var outcome = result.Outcome!;
        Assert.Equal(new[] { e2.Ref }, outcome.GetSelection("hand").ToArray());
        Assert.Equal(TargetSlotKind.HandSelect, outcome.GetSlotKind("hand"));
        Assert.Throws<InvalidOperationException>(() => outcome.GetIdentifiers("hand"));
    }

    [Fact]
    public async Task Hand_Slot_Rejects_Reference_That_Left_Hand_Then_Correction_Succeeds()
    {
        var e1 = new Entity("手牌1");
        var e2 = new Entity("手牌2");
        var inHand = new List<Ref<Entity>> { e1.Ref, e2.Ref };
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge, trace);
        var context = new TargetingRequestContext()
            .WithSlotReferences("hand", inHand)
            .WithSlotDomainValidator("hand", r => inHand.Contains(r));

        var targeter = manager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(1, 1, "hand") },
            context: context);

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 请求期内离手（域判定＝动态成员性；快照仍含它）→ 提交＝拒绝（不构成终局、继续等待、留痕）
        inHand.Remove(e1.Ref);
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", e1.Ref)));
        Assert.False(task.IsCompleted);
        Assert.True(TargeterTestKit.HasKeyword(trace, "violation:Content"));

        // 仍在手牌者照常完成
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", e2.Ref)));
        var result = await task;
        Assert.Equal(new[] { e2.Ref }, result.Outcome!.GetSelection("hand").ToArray());
    }

    [Fact]
    public async Task Hand_Slot_Empty_Allowed_Set_Fails_Without_Interaction()
    {
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge);
        var context = new TargetingRequestContext()
            .WithSlotReferences("hand", Array.Empty<Ref<Entity>>())
            .WithSlotDomainValidator("hand", _ => true);

        var result = await manager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(1, 1, "hand") },
            context: context).Targeting();

        // 必须非空槽位其允许集为空＝失败（不进交互；对齐既有"无可用候选"语义）
        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.NoAvailableCandidates, result.Reason);
        Assert.Empty(bridge.Begins);
        Assert.Empty(bridge.CollectCalls);
    }

    [Fact]
    public async Task Hand_Slot_Domain_Validator_Exception_Yields_Failure_And_Queue_Continues()
    {
        var e1 = new Entity("手牌1");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge, trace);
        var context = new TargetingRequestContext()
            .WithSlotReferences("hand", new[] { e1.Ref })
            .WithSlotDomainValidator("hand", _ => throw new InvalidOperationException("判定面故障"));

        var task = manager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(1, 1, "hand") },
            context: context).Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 判定面抛异常＝失败结局（系统原因类别；不归拒绝路径——Complete 返回 true、终局已定）
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", e1.Ref)));
        var result = await task;

        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.DomainValidationFault, result.Reason);
        Assert.Null(result.Outcome);
        Assert.Contains(trace.Entries, entry =>
            entry.Keywords.Contains($"reason:{TargetingEndReason.DomainValidationFault}", StringComparer.Ordinal));

        // 终局后幂等（恰好一次）
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", e1.Ref)));

        // 队列继续：下一条照常完成
        var e2 = new Entity("手牌2");
        var inHand2 = new List<Ref<Entity>> { e2.Ref };
        var context2 = new TargetingRequestContext()
            .WithSlotReferences("hand", inHand2)
            .WithSlotDomainValidator("hand", r => inHand2.Contains(r));

        var task2 = manager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(1, 1, "hand") },
            context: context2).Targeting();
        var (description2, responder2) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder2.Complete(description2.RequestId, TargeterTestKit.ReferenceSelection("hand", e2.Ref)));
        var result2 = await task2;

        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Equal(new[] { e2.Ref }, result2.Outcome!.GetSelection("hand").ToArray());
    }

    [Fact]
    public async Task Hand_Slot_Supports_Configurable_Counts_And_List_Read_Face()
    {
        var e1 = new Entity("手牌1");
        var e2 = new Entity("手牌2");
        var inHand = new List<Ref<Entity>> { e1.Ref, e2.Ref };
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge);
        var context = new TargetingRequestContext()
            .WithSlotReferences("hand", inHand)
            .WithSlotDomainValidator("hand", r => inHand.Contains(r));

        var targeter = manager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(1, 2, "hand") },
            context: context);

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.Equal((1, 2), (description.Slots[0].Min, description.Slots[0].Max));

        // 少于 min → 拒绝
        Assert.False(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<TargetSelection>>()));
        Assert.False(task.IsCompleted);

        // 恰选 2 → 成功（扁平列表读面＝选择序列）
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("hand", e1.Ref, e2.Ref)));
        var result = await task;

        Assert.Equal(new[] { e1.Ref, e2.Ref }, result.Outcome!.List.ToArray());
        Assert.Equal(new[] { e1.Ref, e2.Ref }, result.Outcome!.GetSelection("hand").ToArray());
        Assert.Null(result.Outcome!.Single); // 多选形态：非单值读面

        // min=0：允许空选完成（空产出）
        var optional = manager.CreateTargeter(
            slots: new TargetSlot[] { new HandSelectSlot(0, 1, "hand") },
            context: context);
        var task2 = optional.Targeting();
        var (description2, responder2) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder2.Complete(description2.RequestId, new Dictionary<string, IReadOnlyList<TargetSelection>>()));
        var result2 = await task2;

        Assert.Equal(TargetingStatus.Success, result2.Status);
        Assert.Empty(result2.Outcome!.GetSelection("hand"));
    }

    // ---------- ③ 卡牌选择器槽位 ----------

    [Fact]
    public void Card_Picker_Request_Construction_Enforces_Form_Payload_Matching()
    {
        var manager = new TargeterManager();

        // 名单形态：缺载荷 → 拒绝（载荷与声明形态不匹配）
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new CardPickerSlot(CardPickerForm.Listing, 1, 1, "pick") }));

        // 名单形态：空名单 → 构造期错误（拒绝）
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new CardPickerSlot(CardPickerForm.Listing, 1, 1, "pick") },
            context: new TargetingRequestContext().WithSlotListings("pick", Array.Empty<CardListing>())));

        // 名单形态：绑定引用集 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new CardPickerSlot(CardPickerForm.Listing, 1, 1, "pick") },
            context: new TargetingRequestContext().WithSlotReferences("pick", Array.Empty<Ref<Entity>>())));

        // 引用集形态：缺引用集 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new CardPickerSlot(CardPickerForm.ReferenceSet, 1, 1, "pick") }));

        // 引用集形态：绑定名单 → 拒绝
        Assert.Throws<ArgumentException>(() => manager.CreateTargeter(
            slots: new[] { new CardPickerSlot(CardPickerForm.ReferenceSet, 1, 1, "pick") },
            context: new TargetingRequestContext().WithSlotListings("pick", new[] { new CardListing("c01", "卡01") })));

        // 名录条目字段：标识/名称均非空
        Assert.Throws<ArgumentException>(() => new CardListing(string.Empty, "卡01"));
        Assert.Throws<ArgumentException>(() => new CardListing("c01", " "));

        // 形态/数量构造期校验
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardPickerSlot((CardPickerForm)99, 1, 1));
        Assert.Throws<ArgumentException>(() => new CardPickerSlot(CardPickerForm.Listing, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardPickerSlot(CardPickerForm.Listing, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardPickerSlot(CardPickerForm.Listing, 1, 0));

        // 合法（名单非空）
        var ok = manager.CreateTargeter(
            slots: new[] { new CardPickerSlot(CardPickerForm.Listing, 1, 1, "pick") },
            context: new TargetingRequestContext().WithSlotListings("pick", new[] { new CardListing("c01", "卡01") }));
        Assert.NotNull(ok);
    }

    [Fact]
    public async Task Card_Picker_Listing_Delivers_Presentable_Entries_And_Yields_Selected_Definition_Id()
    {
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge);
        var context = new TargetingRequestContext().WithSlotListings("pick", new[]
        {
            new CardListing("c01", "卡01"),
            new CardListing("c02", "卡02"),
            new CardListing("c03", "卡03"),
        });
        var targeter = manager.CreateTargeter(
            slots: new TargetSlot[] { new CardPickerSlot(CardPickerForm.Listing, 1, 1, "pick") },
            context: context);

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 无收集需求；呈现＝屏中卡牌阵列（"多张"＝阵列呈现、非数量下界；卡名级要素随描述交付、可断言）
        Assert.Empty(bridge.CollectCalls);
        var slot = Assert.Single(description.Slots);
        Assert.Equal(TargetSlotKind.CardPicker, slot.Kind);
        Assert.Equal(TargetSlotPresentation.CardArray, slot.Presentation);
        Assert.NotNull(slot.CardListings);
        Assert.Equal(new[] { "c01", "c02", "c03" }, slot.CardListings!.Select(l => l.Id).ToArray());
        Assert.Equal(new[] { "卡01", "卡02", "卡03" }, slot.CardListings!.Select(l => l.Name).ToArray());
        Assert.Null(slot.Options);
        Assert.Null(slot.AllowedReferences);

        // 未列出的标识 → 拒绝（不构成终局）
        Assert.False(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("pick", "c99")));
        Assert.False(task.IsCompleted);

        // 选一 → 产出定义标识（按槽位名、非引用读面；"选中后由消费方生成实例"——本单产出标识即达验收）
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.IdentifierSelection("pick", "c02")));
        var result = await task;

        var outcome = result.Outcome!;
        Assert.Equal(new[] { "c02" }, outcome.GetIdentifiers("pick").ToArray());
        Assert.Equal(TargetSlotKind.CardPicker, outcome.GetSlotKind("pick"));
        Assert.Throws<InvalidOperationException>(() => outcome.GetSelection("pick"));
    }

    [Fact]
    public async Task Card_Picker_Reference_Set_Delivers_Snapshot_And_Yields_Card_Reference()
    {
        var e1 = new Entity("卡1");
        var e2 = new Entity("卡2");
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge);
        var context = new TargetingRequestContext().WithSlotReferences("pick", new[] { e1.Ref, e2.Ref });
        var targeter = manager.CreateTargeter(
            slots: new TargetSlot[] { new CardPickerSlot(CardPickerForm.ReferenceSet, 1, 1, "pick") },
            context: context);

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        var slot = Assert.Single(description.Slots);
        Assert.Equal(TargetSlotPresentation.CardArray, slot.Presentation);
        Assert.Null(slot.CardListings);
        Assert.Equal(new[] { e1.Ref, e2.Ref }, slot.AllowedReferences!.ToArray());

        // 选一 → 产出卡引用（沿用 Ref 读面）
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.ReferenceSelection("pick", e1.Ref)));
        var result = await task;
        Assert.Equal(new[] { e1.Ref }, result.Outcome!.GetSelection("pick").ToArray());
    }

    [Fact]
    public async Task Card_Picker_Reference_Set_Empty_Set_Fails_Without_Interaction()
    {
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge);
        var context = new TargetingRequestContext().WithSlotReferences("pick", Array.Empty<Ref<Entity>>());

        var result = await manager.CreateTargeter(
            slots: new TargetSlot[] { new CardPickerSlot(CardPickerForm.ReferenceSet, 1, 1, "pick") },
            context: context).Targeting();

        // 引用集为空＝失败（不进交互；对齐"必须非空槽位空集＝失败"）
        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.NoAvailableCandidates, result.Reason);
        Assert.Empty(bridge.Begins);
    }

    // ---------- ④ 混合槽位最小端到端（⑦） ----------

    [Fact]
    public async Task Mixed_Slots_Option_Plus_Existing_Single_Select_One_Submission_Two_Categories()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
        };
        var manager = new TargeterManager(bridge);

        // 混合：既有单选引用槽位（走收集＋筛选）＋选项槽位（声明承载、不经筛选链）
        var filter = new TargetFilter(coarseFilter: refs => refs.Where(r => !ReferenceEquals(r, e2.Ref)).ToList());
        var targeter = manager.CreateTargeter(
            filter,
            slots: new TargetSlot[]
            {
                new SingleSelectSlot("unit"),
                new OptionSelectSlot(new[] { new OptionEntry("save", "保命"), new OptionEntry("draw", "抽牌") }, "choice"),
            });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 含收集需求的混合请求：收集恰一次（产物仅服务既有引用类；筛选链仅作用于既有引用类）
        Assert.Single(bridge.CollectCalls);
        Assert.Equal(new[] { e1.Ref }, description.AllowedTargets.ToArray());

        // 描述分类承载、可分别断言
        Assert.Equal(2, description.Slots.Count);
        Assert.Equal("unit", description.Slots[0].Name);
        Assert.Equal(TargetSlotKind.SingleSelect, description.Slots[0].Kind);
        Assert.Equal(TargetSlotPresentation.TargetPoints, description.Slots[0].Presentation);
        Assert.Null(description.Slots[0].Options);
        Assert.Null(description.Slots[0].AllowedReferences);
        Assert.Equal("choice", description.Slots[1].Name);
        Assert.Equal(TargetSlotKind.OptionSelect, description.Slots[1].Kind);
        Assert.NotNull(description.Slots[1].Options);

        // 单次提交覆盖全部槽位（两类元素按槽位名组织；混合请求同一次提交、单终局）
        var submission = new Dictionary<string, IReadOnlyList<TargetSelection>>
        {
            ["unit"] = new[] { TargetSelection.FromReference(e1.Ref) },
            ["choice"] = new[] { TargetSelection.FromIdentifier("save") },
        };
        Assert.True(responder.Complete(description.RequestId, submission));
        var result = await task;

        // 按名读取两类产出（不依赖顺序；类别可辨）；单槽位扁平简化读面语义不受影响
        var outcome = result.Outcome!;
        Assert.False(outcome.IsFlat);
        Assert.Equal(new[] { e1.Ref }, outcome.GetSelection("unit").ToArray());
        Assert.Equal(new[] { "save" }, outcome.GetIdentifiers("choice").ToArray());
        Assert.Equal(TargetSlotKind.SingleSelect, outcome.GetSlotKind("unit"));
        Assert.Equal(TargetSlotKind.OptionSelect, outcome.GetSlotKind("choice"));
        Assert.Throws<InvalidOperationException>(() => outcome.GetIdentifiers("unit"));
        Assert.Throws<InvalidOperationException>(() => outcome.GetSelection("choice"));
    }

    [Fact]
    public async Task Option_Slots_Two_Of_Kind_With_Independent_Declared_Sets_Are_Legal()
    {
        var bridge = new MockTargeterBridge();
        var manager = new TargeterManager(bridge);
        var targeter = manager.CreateTargeter(slots: new TargetSlot[]
        {
            new OptionSelectSlot(new[] { new OptionEntry("a1", "甲一"), new OptionEntry("a2", "甲二") }, "first"),
            new OptionSelectSlot(new[] { new OptionEntry("b1", "乙一") }, "second"),
        });

        var task = targeter.Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.Equal(2, description.Slots.Count); // 同类多槽位（名互不重复）＝合法

        // 声明集互相独立：first 提交 second 的标识 → 拒绝
        Assert.False(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<TargetSelection>>
        {
            ["first"] = new[] { TargetSelection.FromIdentifier("b1") },
            ["second"] = new[] { TargetSelection.FromIdentifier("b1") },
        }));
        Assert.False(task.IsCompleted);

        // 各自独立声明集内选择 → 成功（按名读取）
        Assert.True(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<TargetSelection>>
        {
            ["first"] = new[] { TargetSelection.FromIdentifier("a2") },
            ["second"] = new[] { TargetSelection.FromIdentifier("b1") },
        }));
        var result = await task;

        Assert.Equal(new[] { "a2" }, result.Outcome!.GetIdentifiers("first").ToArray());
        Assert.Equal(new[] { "b1" }, result.Outcome!.GetIdentifiers("second").ToArray());
    }
}
