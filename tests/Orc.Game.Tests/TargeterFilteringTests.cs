using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// Targeter 两级筛选验收（②）：粗筛（批量：完整列表 → 允许子集）＋细筛（单引用谓词逐项）；
/// 缺省＝全通过；规范化（null/非引用剔除、重复保留、失效保留）；最终允许集为空＝失败（无可用候选、不进交互）；
/// 筛选回调异常＝统一失败模式（不抛＋留痕＋队列继续）；粗筛结果越界项剔除。
/// </summary>
public class TargeterFilteringTests
{
    [Fact]
    public async Task Normalization_Keeps_Only_Engine_References_In_Submission_Order()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        IReadOnlyList<Ref<Entity>>? seenByCoarse = null;
        var bridge = new MockTargeterBridge
        {
            // 脏数据：null、非引用、重复引用混入
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, null, "not-a-ref", 42, e2.Ref, e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge);
        var filter = new TargetFilter(coarseFilter: input =>
        {
            seenByCoarse = input;
            return input;
        });

        var result = await manager.CreateTargeter(filter).Targeting();

        Assert.Equal(TargetingStatus.Success, result.Status);
        // 粗筛只见规范化后引用：null/非引用剔除；重复不做强制去重（保持提交原样、保序）
        Assert.NotNull(seenByCoarse);
        Assert.Equal(new Ref<Entity>[] { e1.Ref, e2.Ref, e1.Ref }, seenByCoarse!);
    }

    [Fact]
    public async Task Coarse_Filter_Returns_Allowed_Subset_And_Keeps_Return_Order()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        TargetingRequestDescription? seenAtBegin = null;
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref)),
            InteractionScript = (description, responder) =>
            {
                seenAtBegin = description;
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[0]));
            },
        };
        var manager = new TargeterManager(bridge);
        // 批量式：返回子集 [E3, E1]（顺序＝返回序）
        var filter = new TargetFilter(coarseFilter: input => new[] { input[2], input[0] });

        var result = await manager.CreateTargeter(filter).Targeting();

        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.NotNull(seenAtBegin);
        Assert.Equal(new[] { e3.Ref, e1.Ref }, seenAtBegin!.AllowedTargets.ToArray());
        Assert.Same(e3.Ref, result.Outcome!.Single);
    }

    [Fact]
    public async Task Coarse_Filter_Output_Outside_Input_Is_Dropped()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var outside = new Entity("Outside");
        TargetingRequestDescription? seenAtBegin = null;
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = (description, responder) =>
            {
                seenAtBegin = description;
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[0]));
            },
        };
        var manager = new TargeterManager(bridge, trace);
        // 粗筛误返回输入之外的引用：被交叉剔除（不破坏流程），件被记录留痕
        var filter = new TargetFilter(coarseFilter: input => new[] { input[0], outside.Ref, input[1] });

        var result = await manager.CreateTargeter(filter).Targeting();

        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.Equal(new[] { e1.Ref, e2.Ref }, seenAtBegin!.AllowedTargets.ToArray());
        Assert.Contains(trace.Entries, e => e.Message.Contains("输入之外"));
    }

    [Fact]
    public async Task Fine_Filter_Runs_Item_By_Item_On_Coarse_Result()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var e3 = new Entity("E3");
        var fineSeen = new List<Ref<Entity>>();
        TargetingRequestDescription? seenAtBegin = null;
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref, e3.Ref)),
            InteractionScript = (description, responder) =>
            {
                seenAtBegin = description;
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[0]));
            },
        };
        var manager = new TargeterManager(bridge);
        var filter = new TargetFilter(
            coarseFilter: input => input, // 全通过
            fineFilter: reference =>
            {
                fineSeen.Add(reference);
                return !ReferenceEquals(reference, e2.Ref); // 淘汰 E2
            });

        var result = await manager.CreateTargeter(filter).Targeting();

        Assert.Equal(TargetingStatus.Success, result.Status);
        // 细筛逐项（单引用参数）：按粗筛结果顺序、每一项均被调用
        Assert.Equal(new[] { e1.Ref, e2.Ref, e3.Ref }, fineSeen.ToArray());
        // 最终允许集＝粗筛结果去掉细筛淘汰项
        Assert.Equal(new[] { e1.Ref, e3.Ref }, seenAtBegin!.AllowedTargets.ToArray());
    }

    [Fact]
    public async Task Default_Filters_Pass_All_Candidates()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = (description, responder) =>
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[1])),
        };
        var manager = new TargeterManager(bridge);

        // 无筛选器（null）＝两级全通过
        var result1 = await manager.CreateTargeter().Targeting();
        // 空筛选器（两级均缺省）＝全通过
        var result2 = await manager.CreateTargeter(new TargetFilter()).Targeting();

        Assert.Same(e2.Ref, result1.Outcome!.Single);
        Assert.Same(e2.Ref, result2.Outcome!.Single);
        Assert.Equal(new[] { e1.Ref, e2.Ref }, bridge.Begins[0].AllowedTargets.ToArray());
        Assert.Equal(new[] { e1.Ref, e2.Ref }, bridge.Begins[1].AllowedTargets.ToArray());
    }

    [Fact]
    public async Task Empty_Submission_Fails_NoAvailableCandidates_Without_Interaction()
    {
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge(); // 收集脚本缺省＝空列表
        var manager = new TargeterManager(bridge, trace);

        var result = await manager.CreateTargeter().Targeting();

        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.NoAvailableCandidates, result.Reason);
        Assert.Null(result.Outcome);
        Assert.Empty(bridge.Begins); // 不进入前端交互
        Assert.True(TargeterTestKit.HasKeyword(trace, "reason:NoAvailableCandidates"));
    }

    [Fact]
    public async Task All_Invalid_Elements_Dropped_Then_Fails_NoAvailableCandidates()
    {
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(null, "junk", 7)),
        };
        var manager = new TargeterManager(bridge);

        var result = await manager.CreateTargeter().Targeting();

        // 无法规范化元素＝剔除单项继续；最终集合空 → 无可用候选失败（整批不受坏元素拖垮）
        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.NoAvailableCandidates, result.Reason);
        Assert.Empty(bridge.Begins);
    }

    [Fact]
    public async Task Coarse_Filter_Empty_Result_Fails_NoAvailableCandidates()
    {
        var e1 = new Entity("E1");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge, trace);
        var filter = new TargetFilter(coarseFilter: _ => Array.Empty<Ref<Entity>>());

        var result = await manager.CreateTargeter(filter).Targeting();

        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.NoAvailableCandidates, result.Reason);
        Assert.Empty(bridge.Begins);
        Assert.True(TargeterTestKit.HasKeyword(trace, "reason:NoAvailableCandidates"));
    }

    [Fact]
    public async Task Fine_Filter_Screens_All_Out_Fails_NoAvailableCandidates()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var filter = new TargetFilter(
            coarseFilter: input => input,
            fineFilter: _ => false);

        var result = await manager.CreateTargeter(filter).Targeting();

        // 判定点在筛选完成后最终允许集为空（有候选但全部被筛除）
        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.NoAvailableCandidates, result.Reason);
        Assert.Empty(bridge.Begins);
    }

    [Fact]
    public async Task Coarse_Filter_Fault_Is_Unified_Failure_And_Queue_Continues()
    {
        var e1 = new Entity("E1");
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge, trace);
        var faulty = new TargetFilter(coarseFilter: _ => throw new InvalidOperationException("粗筛爆炸"));

        var failed = await manager.CreateTargeter(faulty).Targeting();

        // 失败不抛、留痕；队列继续（后续请求正常完成）
        Assert.Equal(TargetingStatus.Failed, failed.Status);
        Assert.Equal(TargetingEndReason.FilterFault, failed.Reason);
        Assert.True(TargeterTestKit.HasKeyword(trace, "reason:FilterFault"));

        var ok = await manager.CreateTargeter().Targeting();
        Assert.Equal(TargetingStatus.Success, ok.Status);
    }

    [Fact]
    public async Task Fine_Filter_Fault_Is_Unified_Failure_And_Queue_Continues()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var calls = 0;
        var trace = new InMemoryTargetingTrace();
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = TargeterTestKit.AutoCompleteWithFirstAllowed(),
        };
        var manager = new TargeterManager(bridge, trace);
        var faulty = new TargetFilter(fineFilter: _ =>
        {
            calls++;
            throw new InvalidOperationException("细筛爆炸");
        });

        var failed = await manager.CreateTargeter(faulty).Targeting();

        Assert.Equal(TargetingStatus.Failed, failed.Status);
        Assert.Equal(TargetingEndReason.FilterFault, failed.Reason);
        Assert.Equal(1, calls); // 第一项即抛、不继续
        Assert.True(TargeterTestKit.HasKeyword(trace, "reason:FilterFault"));

        var ok = await manager.CreateTargeter().Targeting();
        Assert.Equal(TargetingStatus.Success, ok.Status);
    }

    [Fact]
    public async Task Coarse_Filter_Null_Return_Is_FilterFault()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var manager = new TargeterManager(bridge);
        var faulty = new TargetFilter(coarseFilter: _ => null!);

        var failed = await manager.CreateTargeter(faulty).Targeting();

        Assert.Equal(TargetingStatus.Failed, failed.Status);
        Assert.Equal(TargetingEndReason.FilterFault, failed.Reason);
        Assert.Empty(bridge.Begins);
    }
}
