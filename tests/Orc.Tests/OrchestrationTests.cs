using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// S-C3 编排管理器验收：登记（自动/显式）、反向依赖、种类聚合、声明期可达链与图缺口。
/// </summary>
public class OrchestrationTests
{
    private static Trigger<CounterView> Make(string stableKey, params string[] downstream)
    {
        var trigger = new Trigger<CounterView>(stableKey, stableKey: stableKey);
        trigger.Register("事件", (v, c, ct) => Task.CompletedTask, downstream: downstream);
        return trigger;
    }

    [Fact]
    public void 总线挂载_自动登记进编排管理器()
    {
        var engine = new LogicEngine();
        var trigger = BusTestHelpers.RecordingPassive("自动", new[] { "h.auto" }, new List<string>());

        engine.Bus.Mount(trigger);

        Assert.Equal(1, engine.Orchestration.Count);
        var node = engine.Orchestration.BuildAuditChain().Nodes.Single();
        Assert.Equal("自动", node.DisplayName);
        Assert.Equal("bus-mount", node.Instances.Single().Origin);
        Assert.True(node.Instances.Single().Mounted);
    }

    [Fact]
    public void 显式登记_主动触发器_未装载标记()
    {
        var engine = new LogicEngine();
        var active = Make("unit.play");

        engine.Orchestration.Register(active, host: "卡:测试卡");

        var node = engine.Orchestration.BuildAuditChain().Nodes.Single();
        Assert.Equal(TriggerKind.Active, node.Kind);
        Assert.False(node.Instances.Single().Mounted);
        Assert.Equal("卡:测试卡", node.Instances.Single().Host);
    }

    [Fact]
    public void 重复登记同一实例_幂等()
    {
        var engine = new LogicEngine();
        var trigger = Make("k");

        engine.Orchestration.Register(trigger);
        engine.Orchestration.Register(trigger);

        Assert.Equal(1, engine.Orchestration.Count);
    }

    [Fact]
    public void 反向依赖_按hook查触发器()
    {
        var engine = new LogicEngine();
        var a = BusTestHelpers.RecordingPassive("甲", new[] { "shared.hook" }, new List<string>());
        var b = BusTestHelpers.RecordingPassive("乙", new[] { "shared.hook", "other.hook" }, new List<string>());
        var c = BusTestHelpers.RecordingPassive("丙", new[] { "other.hook" }, new List<string>());

        engine.Bus.Mount(a);
        engine.Bus.Mount(b);
        engine.Bus.Mount(c);

        var subscribers = engine.Orchestration.TriggersWithHook("shared.hook");

        Assert.Equal(new[] { "甲", "乙" }, subscribers.Select(t => t.DisplayName));

        var otherHook = engine.Orchestration.TriggersWithHook("other.hook");
        Assert.Equal(2, otherHook.Count);
        Assert.Single(otherHook, t => t.DisplayName == "丙");
    }

    [Fact]
    public void 审查链_同种类多实例聚合为单节点()
    {
        var engine = new LogicEngine();
        var first = Make("unit.play");
        var second = Make("unit.play");

        engine.Orchestration.Register(first, host: "卡:甲");
        engine.Orchestration.Register(second, host: "卡:乙");

        var chain = engine.Orchestration.BuildAuditChain();

        var node = Assert.Single(chain.Nodes);
        Assert.Equal(2, node.Instances.Count);
        Assert.Equal(new[] { "卡:甲", "卡:乙" }, node.Instances.Select(i => i.Host));
    }

    [Fact]
    public void 审查链_可达链_沿下游BFS_排除无关节点()
    {
        var engine = new LogicEngine();
        engine.Orchestration.Register(Make("root", "mid"));
        engine.Orchestration.Register(Make("mid", "leaf"));
        engine.Orchestration.Register(Make("leaf"));
        engine.Orchestration.Register(Make("orphan"));

        var chain = engine.Orchestration.QueryChain(TriggerId.FromKey("root"));

        var keys = chain.Nodes.Select(n => n.StableKey).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(3, keys.Count);
        Assert.Contains("root", keys);
        Assert.Contains("mid", keys);
        Assert.Contains("leaf", keys);
        Assert.DoesNotContain("orphan", keys);
    }

    [Fact]
    public void 审查链_未登记的下游键_显式呈现为图缺口()
    {
        var engine = new LogicEngine();
        engine.Orchestration.Register(Make("solo", "not.registered"));

        var node = engine.Orchestration.QueryChain(TriggerId.FromKey("solo")).Nodes.Single();
        var edge = Assert.Single(node.Downstream);

        Assert.Equal("not.registered", edge.DownstreamKey);
        Assert.Null(edge.ResolvedTriggerId);
    }

    [Fact]
    public void 审查链_携带schema版本与hook读面()
    {
        var engine = new LogicEngine();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("t", new[] { "h.chained" }, new List<string>()));

        var chain = engine.Orchestration.BuildAuditChain();

        Assert.Equal(OrchestrationManager.SchemaVersion, chain.SchemaVersion);
        Assert.Contains(chain.Hooks, h => h.Hook == "h.chained");
    }

    [Fact]
    public void 登记非触发器对象_拒绝()
    {
        var engine = new LogicEngine();
        Assert.Throws<ArgumentException>(() => engine.Orchestration.Register(new object()));
    }
}
