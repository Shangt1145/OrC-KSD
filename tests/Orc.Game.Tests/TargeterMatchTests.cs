using Orc.Core;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// Match 接入验收（第六员·端到端）：构造（注入 mock 桥接）→ Initialize → 第六员经公开面可读 → 完整 targeting 成功流程；
/// 桥接缺失装配 → Targeting＝失败结局（原因类别＋留痕，经引擎既有渠道〔总流〕）；
/// 违规留痕经对局渠道可观测；准备态访问第六员沿用既有门禁模式（抛错）。
/// </summary>
public class TargeterMatchTests
{
    [Fact]
    public async Task Match_EndToEnd_Targeting_Success_Through_Public_Surface()
    {
        var e1 = new Entity("E1");
        var e2 = new Entity("E2");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref, e2.Ref)),
            InteractionScript = (description, responder) =>
                responder.Complete(
                    description.RequestId,
                    TargeterTestKit.Selection(TargeterTestKit.PrimarySlot(description), description.AllowedTargets[^1])),
        };

        var match = GameTestData.CreateStandardMatch(seed: 42, targeterBridge: bridge);
        await match.Initialize();

        // 第六员随管理器群生成、经公开面可读（进行态）
        Assert.NotNull(match.TargeterManager);
        Assert.Same(bridge, match.TargeterManager.Bridge);

        // 完整 targeting 成功流程（两级筛选 + 交互 + 产出）
        var filter = new TargetFilter(fineFilter: reference => !ReferenceEquals(reference, e1.Ref));
        var result = await match.TargeterManager.CreateTargeter(filter).Targeting();

        Assert.Equal(TargetingStatus.Success, result.Status);
        Assert.Same(e2.Ref, result.Outcome!.Single);
        // 筛选后的允许子集交前端（未返回项由前端置黑——后端只产出"允许什么"）
        Assert.Equal(new[] { e2.Ref }, bridge.Begins[0].AllowedTargets.ToArray());
    }

    [Fact]
    public void Match_Preparing_State_TargeterManager_Access_Follows_Existing_Gate()
    {
        var match = GameTestData.CreateStandardMatch();

        // 准备态沿用既有门禁模式（同其它管理器：抛 InvalidOperationException）
        var exception = Assert.Throws<InvalidOperationException>(() => match.TargeterManager);
        Assert.Contains("进行", exception.Message);
    }

    [Fact]
    public async Task Match_Without_Bridge_Targeting_Fails_With_Trace_On_Engine_Channel()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42); // 无桥接装配（可选参数缺省）
        await match.Initialize();

        var result = await match.TargeterManager.CreateTargeter().Targeting();

        // 失败结局（不抛＋原因类别）；留痕经引擎既有渠道（总流）可观测
        Assert.Equal(TargetingStatus.Failed, result.Status);
        Assert.Equal(TargetingEndReason.BridgeNotAssembled, result.Reason);
        Assert.Contains(
            match.Engine.RootStream.Entries,
            entry => entry.Source == "targeting" && entry.Keywords.Contains("reason:BridgeNotAssembled"));
    }

    [Fact]
    public async Task Match_Violation_Trace_Is_Visible_On_Engine_Event_Stream()
    {
        var e1 = new Entity("E1");
        var bridge = new MockTargeterBridge
        {
            CollectScript = _ => Task.FromResult(TargeterTestKit.Candidates(e1.Ref)),
        };
        var match = GameTestData.CreateStandardMatch(seed: 42, targeterBridge: bridge);
        await match.Initialize();

        var task = match.TargeterManager.CreateTargeter().Targeting();
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // 内容不合规（空提交，少于 min）→ 拒绝＋留痕（对局渠道＝引擎事件流）
        Assert.False(responder.Complete(description.RequestId, new Dictionary<string, IReadOnlyList<Ref<Entity>>>()));
        Assert.Contains(
            match.Engine.RootStream.Entries,
            entry => entry.Source == "targeting" && entry.Keywords.Contains("violation:Content"));

        // 纠正后成功
        Assert.True(responder.Complete(description.RequestId, TargeterTestKit.Selection(TargetSlot.DefaultName, e1.Ref)));
        var result = await task;
        Assert.Equal(TargetingStatus.Success, result.Status);
    }
}
