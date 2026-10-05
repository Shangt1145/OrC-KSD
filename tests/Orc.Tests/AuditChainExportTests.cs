using System.Text.Json;
using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// S-C4 审查链导出验收：JSON 真源（结构/缺口/hook 段）与派生文本视图。
/// </summary>
public class AuditChainExportTests
{
    private static Trigger<CounterView> Make(string key, params string[] downstream)
    {
        var trigger = new Trigger<CounterView>(key, stableKey: key);
        trigger.Register("事件", (v, c, ct) => Task.CompletedTask, downstream: downstream);
        return trigger;
    }

    [Fact]
    public void JSON_空链_合法输出()
    {
        var engine = new LogicEngine();

        using var doc = JsonDocument.Parse(engine.ExportAuditChainJson());

        Assert.Equal(OrchestrationManager.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("hooks").GetArrayLength());
    }

    [Fact]
    public void JSON_节点事件与实例_完整表达()
    {
        var engine = new LogicEngine();
        engine.Orchestration.Register(Make("root", "mid"), host: "卡:甲");
        engine.Orchestration.Register(Make("mid"), host: "卡:乙");

        using var doc = JsonDocument.Parse(engine.ExportAuditChainJson());
        var nodes = doc.RootElement.GetProperty("nodes");

        Assert.Equal(2, nodes.GetArrayLength());

        var root = nodes.EnumerateArray().Single(n => n.GetProperty("stableKey").GetString() == "root");
        Assert.Equal("active", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("serializable").GetBoolean());
        Assert.True(root.GetProperty("declaredKey").GetBoolean());
        Assert.Equal("卡:甲", root.GetProperty("instances")[0].GetProperty("host").GetString());
        Assert.Equal("explicit", root.GetProperty("instances")[0].GetProperty("origin").GetString());
        Assert.False(root.GetProperty("instances")[0].GetProperty("mounted").GetBoolean());

        var downstream = root.GetProperty("events")[0].GetProperty("downstream")[0];
        Assert.Equal("mid", downstream.GetProperty("key").GetString());
        Assert.NotEqual(JsonValueKind.Null, downstream.GetProperty("resolved").ValueKind);
    }

    [Fact]
    public void JSON_未登记的下游_解析为null()
    {
        var engine = new LogicEngine();
        engine.Orchestration.Register(Make("solo", "missing"));

        using var doc = JsonDocument.Parse(engine.ExportAuditChainJson());
        var downstream = doc.RootElement.GetProperty("nodes")[0]
            .GetProperty("events")[0].GetProperty("downstream")[0];

        Assert.Equal(JsonValueKind.Null, downstream.GetProperty("resolved").ValueKind);
    }

    [Fact]
    public void JSON_hook段_含标识与订阅者()
    {
        var engine = new LogicEngine();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("挂", new[] { "h.json" }, new List<string>()));

        using var doc = JsonDocument.Parse(engine.ExportAuditChainJson());
        var hook = doc.RootElement.GetProperty("hooks")[0];

        Assert.Equal("h.json", hook.GetProperty("hook").GetString());
        Assert.Equal("挂", hook.GetProperty("subscribers")[0].GetString());
        Assert.False(string.IsNullOrWhiteSpace(hook.GetProperty("id").GetString()));
    }

    [Fact]
    public void 文本视图_含关键行()
    {
        var engine = new LogicEngine();
        engine.Orchestration.Register(Make("root", "mid"), host: "卡:甲");
        engine.Orchestration.Register(Make("mid"));

        var text = engine.ExportAuditChainText();

        Assert.Contains("审查链 schemaVersion=1", text);
        Assert.Contains("[主动] root", text);
        Assert.Contains("→ mid (resolved ", text);
        Assert.Contains("serializable=false", text);
        Assert.Contains("origin=explicit", text);
    }

    [Fact]
    public void 文本视图_未登记下游_标缺口()
    {
        var engine = new LogicEngine();
        engine.Orchestration.Register(Make("solo", "ghost"));

        Assert.Contains("→ ghost (未登记·图缺口)", engine.ExportAuditChainText());
    }

    [Fact]
    public void 可达链导出_仅含起点可达集()
    {
        var engine = new LogicEngine();
        engine.Orchestration.Register(Make("root", "mid"));
        engine.Orchestration.Register(Make("mid"));
        engine.Orchestration.Register(Make("orphan"));

        using var doc = JsonDocument.Parse(engine.ExportChainJson(TriggerId.FromKey("root")));
        var keys = doc.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("stableKey").GetString()).ToList();

        Assert.Equal(2, keys.Count);
        Assert.Contains("root", keys);
        Assert.Contains("mid", keys);
        Assert.DoesNotContain("orphan", keys);
    }

    [Fact]
    public void 文本视图_被动触发器_显示hook与挂载态()
    {
        var engine = new LogicEngine();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("被", new[] { "h.text" }, new List<string>()));

        var text = engine.ExportAuditChainText();

        Assert.Contains("[被动] 被", text);
        Assert.Contains("hooks: h.text", text);
        Assert.Contains("origin=bus-mount mounted=true", text);
    }
}
