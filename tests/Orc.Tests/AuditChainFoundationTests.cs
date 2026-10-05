using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// S-C1/S-C2 内核加性面验收：稳定标识（HookId/TriggerId）、hook 注册表、内核只读枚举面、<see cref="Context.Engine"/>。
/// </summary>
public class AuditChainFoundationTests
{
    // ---------- S-C1：稳定哈希与标识 ----------

    [Fact]
    public void StableHash_相同输入恒同值_不同输入不同值()
    {
        var a1 = StableHash.Fnv1a64("card.placed");
        var a2 = StableHash.Fnv1a64("card.placed");
        var b = StableHash.Fnv1a64("card.destroyed");

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    [Fact]
    public void StableHash_null_抛参数异常()
    {
        Assert.Throws<ArgumentNullException>(() => StableHash.Fnv1a64(null!));
    }

    [Fact]
    public void HookId_FromName_空白_拒绝()
    {
        Assert.Throws<ArgumentException>(() => HookId.FromName("   "));
        Assert.Throws<ArgumentException>(() => TriggerId.FromKey(""));
    }

    [Fact]
    public void HookId_由名字确定性派生()
    {
        Assert.Equal(HookId.FromName("card.placed"), HookId.FromName("card.placed"));
        Assert.NotEqual(HookId.FromName("card.placed"), HookId.FromName("card.data"));
    }

    // ---------- S-C1：hook 注册表 ----------

    [Fact]
    public void HookRegistry_名字与标识双向互转()
    {
        var registry = new HookRegistry();
        var id = registry.Register("card.placed");

        Assert.True(registry.TryGetId("card.placed", out var byName));
        Assert.Equal(id, byName);

        Assert.True(registry.TryGetName(id, out var name));
        Assert.Equal("card.placed", name);
    }

    [Fact]
    public void HookRegistry_自由字符串保留_未登记名可查标识但不隐式登记()
    {
        var registry = new HookRegistry();

        Assert.False(registry.TryGetId("custom.hook", out _)); // 未登记＝false
        Assert.Equal(0, registry.Count);

        var id = HookId.FromName("custom.hook"); // 纯函数仍可取标识
        Assert.False(registry.TryGetName(id, out _));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void HookRegistry_同名重复登记_幂等()
    {
        var registry = new HookRegistry();
        var first = registry.Register("hook.a");
        var second = registry.Register("hook.a");

        Assert.Equal(first, second);
        Assert.Equal(1, registry.Count);
        Assert.Empty(registry.Collisions);
    }

    [Fact]
    public void HookRegistry_空白名_拒绝()
    {
        var registry = new HookRegistry();
        Assert.Throws<ArgumentException>(() => registry.Register("  "));
    }

    // ---------- S-C1：引擎词汇表 ----------

    [Fact]
    public void 引擎构造_内置更新字符串已预登记()
    {
        var engine = new LogicEngine();

        Assert.True(engine.Hooks.TryGetId(Updates.CardPlaced, out _));
        Assert.True(engine.Hooks.TryGetId(Updates.CardDestroyed, out _));
        Assert.True(engine.Hooks.TryGetId(Updates.CardData, out _));
        Assert.True(engine.Hooks.TryGetId(Updates.EffectRemoved, out _));
    }

    [Fact]
    public void 多引擎_hook注册表各自独立()
    {
        var a = new LogicEngine();
        var b = new LogicEngine();

        a.Hooks.Register("only.a");

        Assert.True(a.Hooks.TryGetId("only.a", out _));
        Assert.False(b.Hooks.TryGetId("only.a", out _));
    }

    // ---------- S-C1：触发器稳定键 ----------

    [Fact]
    public void 触发器_显式稳定键_定义级标识由键派生()
    {
        var t1 = new Trigger<CounterView>("打出触发器", stableKey: "unit.play");
        var t2 = new Trigger<CounterView>("部署触发器", stableKey: "unit.play");

        Assert.Equal("unit.play", t1.StableKey);
        Assert.True(t1.HasDeclaredStableKey);
        Assert.Equal(t1.Id, t2.Id); // 同键＝同种类标识（与展示名无关）
        Assert.Equal(TriggerId.FromKey("unit.play"), t1.Id);
    }

    [Fact]
    public void 触发器_未声明稳定键_回退派生并标弱身份()
    {
        var named = new Trigger<CounterView>("自定义触发器");
        Assert.False(named.HasDeclaredStableKey);
        Assert.Equal("自定义触发器", named.StableKey);

        var unnamed = new Trigger<CounterView>();
        Assert.False(unnamed.HasDeclaredStableKey);
        Assert.Equal(nameof(CounterView), unnamed.StableKey);
    }

    [Fact]
    public void 触发器_空白稳定键_拒绝()
    {
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>("x", stableKey: "   "));
    }

    // ---------- S-C2：事件枚举与下游声明 ----------

    [Fact]
    public void 触发器_事件枚举_按执行序且携带下游声明()
    {
        var trigger = new Trigger<CounterView>("t", stableKey: "t.kind");
        trigger.Register("后置", (v, c, ct) => Task.CompletedTask, priority: 10, downstream: new[] { "unit.deploy" });
        trigger.Register("前置", (v, c, ct) => Task.CompletedTask, priority: 1);

        var events = trigger.Events;

        Assert.Equal(2, events.Count);
        Assert.Equal("前置", events[0].Name); // 优先级升序
        Assert.Equal("后置", events[1].Name);
        Assert.Empty(events[0].Downstream);
        Assert.Equal(new[] { "unit.deploy" }, events[1].Downstream);
        Assert.Equal(1, events[0].Priority);
    }

    [Fact]
    public void 触发器_下游声明_含重复项_拒绝()
    {
        var trigger = new Trigger<CounterView>("t");
        Assert.Throws<ArgumentException>(() =>
            trigger.Register("e", (v, c, ct) => Task.CompletedTask, downstream: new[] { "a", "a" }));
        Assert.Throws<ArgumentException>(() =>
            trigger.Register("e", (v, c, ct) => Task.CompletedTask, downstream: new[] { " " }));
    }

    // ---------- S-C2：总线枚举面 ----------

    [Fact]
    public void 总线_枚举hook_按挂载序且含订阅者()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var slow = BusTestHelpers.RecordingPassive("慢", new[] { "h.one" }, trace, UpdatePriorities.Low);
        var fast = BusTestHelpers.RecordingPassive("快", new[] { "h.one" }, trace, UpdatePriorities.High);
        var other = BusTestHelpers.RecordingPassive("其它", new[] { "h.two" }, trace);

        engine.Bus.Mount(slow);
        engine.Bus.Mount(fast);
        engine.Bus.Mount(other);

        var hooks = engine.Bus.EnumerateHooks();

        Assert.Equal(2, hooks.Count);
        Assert.Equal("h.one", hooks[0].Hook);
        Assert.Equal(HookId.FromName("h.one"), hooks[0].Id);
        Assert.Equal(new[] { "快", "慢" }, hooks[0].Subscribers); // 优先级升序
        Assert.Equal("h.two", hooks[1].Hook);
    }

    [Fact]
    public void 总线_挂载_将hook登记进引擎词汇表()
    {
        var engine = new LogicEngine();
        Assert.False(engine.Hooks.TryGetId("custom.placed", out _));

        engine.Bus.Mount(BusTestHelpers.RecordingPassive("t", new[] { "custom.placed" }, new List<string>()));

        Assert.True(engine.Hooks.TryGetId("custom.placed", out var id));
        Assert.Equal(HookId.FromName("custom.placed"), id);
    }

    [Fact]
    public void 总线_卸载后_hook枚举不再包含()
    {
        var engine = new LogicEngine();
        var owner = new object();
        var trigger = BusTestHelpers.RecordingPassive("t", new[] { "h.x" }, new List<string>(), owner: owner);
        engine.Bus.Mount(trigger);
        Assert.Single(engine.Bus.EnumerateHooks());

        engine.Bus.UnmountOwner(owner);

        Assert.Empty(engine.Bus.EnumerateHooks());
        Assert.True(engine.Hooks.TryGetId("h.x", out _)); // 词汇表登记不因卸载消失（对局内固化）
    }

    // ---------- S-C2：Context.Engine ----------

    [Fact]
    public async Task 执行期_Context可读引擎_执行外为null()
    {
        var engine = new LogicEngine();
        LogicEngine? seen = null;

        var trigger = new Trigger<CounterView>(
            "t",
            events: new[]
            {
                new TriggerEvent<CounterView>("e", (v, c, ct) =>
                {
                    seen = c.Engine;
                    return Task.CompletedTask;
                }),
            });

        await trigger.InvokeAsync(engine);

        Assert.Same(engine, seen);
        Assert.Null(new Context().Engine); // 未关联执行会话＝null
    }
}
