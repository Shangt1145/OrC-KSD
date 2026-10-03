using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点④⑤（挂载侧）：Mount 完整拒绝集；UnmountOwner（引用相等/幂等/留痕/范围）；
/// 重挂注册序复位；订阅查询读面（有序快照）；Trigger 构造期 hooks 校验；Updates/UpdatePriorities 常量形态与旧调用形态兼容。
/// </summary>
public class BusMountTests
{
    // ---------- 常量形态 ----------

    [Fact]
    public void Updates_And_UpdatePriorities_Constants_Have_Fixed_Forms()
    {
        Assert.Equal("card.placed", Updates.CardPlaced);
        Assert.Equal("card.destroyed", Updates.CardDestroyed);
        Assert.Equal("card.data", Updates.CardData);
        Assert.Equal("effect.removed", Updates.EffectRemoved);

        Assert.Equal(100, UpdatePriorities.High);
        Assert.Equal(200, UpdatePriorities.Normal);
        Assert.Equal(300, UpdatePriorities.Low);
    }

    // ---------- Mount 拒绝集 ----------

    [Fact]
    public void Mount_Rejects_Null_Trigger()
    {
        var engine = new LogicEngine();
        Assert.Throws<ArgumentNullException>(() => engine.Bus.Mount<CounterView>(null!));
    }

    [Fact]
    public void Mount_Rejects_Active_Trigger()
    {
        var engine = new LogicEngine();
        var active = new Trigger<CounterView>(name: "主动", events: new[]
        {
            new TriggerEvent<CounterView>("e", (v, c, ct) => Task.CompletedTask),
        });

        Assert.Throws<InvalidOperationException>(() => engine.Bus.Mount(active));
        Assert.Empty(engine.Bus.GetSubscribers("u")); // 拒绝时未产生任何注册
    }

    [Fact]
    public void Passive_Without_Hooks_Can_Exist_But_Cannot_Mount()
    {
        var bare = new Trigger<CounterView>(name: "裸被动", kind: TriggerKind.Passive);
        Assert.Equal(TriggerKind.Passive, bare.Kind); // 可存在（S2 语义保持）

        var engine = new LogicEngine();
        Assert.Throws<InvalidOperationException>(() => engine.Bus.Mount(bare));
    }

    [Fact]
    public void Mount_Rejects_Duplicate_Mount_And_Allows_Remount_After_Unmount()
    {
        var engine = new LogicEngine();
        var owner = new object();
        var t = BusTestHelpers.RecordingPassive("T", new[] { "u" }, new List<string>(), owner: owner);

        engine.Bus.Mount(t);
        Assert.Throws<InvalidOperationException>(() => engine.Bus.Mount(t)); // 重复挂载拒绝（防双执行）

        engine.Bus.UnmountOwner(owner);
        engine.Bus.Mount(t); // 卸载后状态复位、可再次挂载
        Assert.Equal(new[] { "T" }, engine.Bus.GetSubscribers("u"));
    }

    [Fact]
    public void Mount_Rejects_Cross_Bus_Duplicate_Mount()
    {
        var a = new LogicEngine();
        var b = new LogicEngine();
        var owner = new object();
        var t = BusTestHelpers.RecordingPassive("T", new[] { "u" }, new List<string>(), owner: owner);

        a.Bus.Mount(t);
        Assert.Throws<InvalidOperationException>(() => b.Bus.Mount(t)); // 已挂载状态＝触发器实例级（跨总线同样拒绝）

        a.Bus.UnmountOwner(owner);
        b.Bus.Mount(t); // 卸载后可挂到另一总线
        Assert.Empty(a.Bus.GetSubscribers("u"));
        Assert.Equal(new[] { "T" }, b.Bus.GetSubscribers("u"));
    }

    // ---------- UnmountOwner ----------

    [Fact]
    public void UnmountOwner_Rejects_Null()
    {
        var engine = new LogicEngine();
        Assert.Throws<ArgumentNullException>(() => engine.Bus.UnmountOwner(null!));
    }

    [Fact]
    public async Task UnmountOwner_Removes_All_Mounts_And_Blocks_Future_Activation()
    {
        var engine = new LogicEngine();
        var owner = new object();
        var trace = new List<string>();
        var x = BusTestHelpers.RecordingPassive("X", new[] { Updates.CardPlaced, Updates.CardDestroyed }, trace, owner: owner);
        var y = BusTestHelpers.RecordingPassive("Y", new[] { Updates.CardPlaced }, trace, owner: owner);
        engine.Bus.Mount(x);
        engine.Bus.Mount(y);

        await engine.Emit(Updates.CardPlaced);
        Assert.Equal(new[] { "X", "Y" }, trace);

        trace.Clear();
        engine.Bus.UnmountOwner(owner); // 跨触发器、跨更新字符串全部移除

        await engine.Emit(Updates.CardPlaced);
        await engine.Emit(Updates.CardDestroyed);
        Assert.Empty(trace); // 卸载后不再激活
        Assert.Empty(engine.Bus.GetSubscribers(Updates.CardPlaced));
        Assert.Empty(engine.Bus.GetSubscribers(Updates.CardDestroyed));
    }

    [Fact]
    public async Task UnmountOwner_Matches_By_Reference_Equality()
    {
        var engine = new LogicEngine();
        var owner = new EqualityOverridingOwner();
        var other = new EqualityOverridingOwner(); // Equals(owner)==true
        var trace = new List<string>();
        var t = BusTestHelpers.RecordingPassive("T", new[] { "u" }, trace, owner: owner);
        engine.Bus.Mount(t);

        engine.Bus.UnmountOwner(other); // 值语义相等但引用不同 → 不匹配
        Assert.Equal(new[] { "T" }, engine.Bus.GetSubscribers("u"));
        Assert.DoesNotContain(engine.RootStream.Entries, e => e.Keywords.Contains("unmount"));

        await engine.Emit("u"); // 仍正常激活
        Assert.Equal(new[] { "T" }, trace);

        engine.Bus.UnmountOwner(owner); // 真正所有者 → 命中
        Assert.Empty(engine.Bus.GetSubscribers("u"));
    }

    [Fact]
    public void UnmountOwner_Miss_And_Repeat_Are_Idempotent_Without_Trace()
    {
        var engine = new LogicEngine();
        engine.Bus.UnmountOwner(new object()); // 未命中：无异常、不写留痕
        Assert.DoesNotContain(engine.RootStream.Entries, e => e.Keywords.Contains("unmount"));

        var owner = new object();
        var t = BusTestHelpers.RecordingPassive("T", new[] { "u" }, new List<string>(), owner: owner);
        engine.Bus.Mount(t);

        engine.Bus.UnmountOwner(owner); // 命中：恰一条留痕
        Assert.Equal(1, engine.RootStream.Entries.Count(e => e.Keywords.Contains("unmount")));

        engine.Bus.UnmountOwner(owner); // 第二次：未命中、不写
        Assert.Equal(1, engine.RootStream.Entries.Count(e => e.Keywords.Contains("unmount")));
    }

    [Fact]
    public void UnmountOwner_Leaves_Unrelated_And_Null_Owners_Untouched()
    {
        var engine = new LogicEngine();
        var ownerA = new object();
        var ownerB = new object();
        var t1 = BusTestHelpers.RecordingPassive("T1", new[] { "u" }, new List<string>(), owner: ownerA);
        var t2 = BusTestHelpers.RecordingPassive("T2", new[] { "u" }, new List<string>(), owner: ownerB);
        var t3 = BusTestHelpers.RecordingPassive("T3", new[] { "u" }, new List<string>()); // owner=null（合法、不可经 UnmountOwner 卸载）
        engine.Bus.Mount(t1);
        engine.Bus.Mount(t2);
        engine.Bus.Mount(t3);

        engine.Bus.UnmountOwner(ownerA);
        Assert.Equal(new[] { "T2", "T3" }, engine.Bus.GetSubscribers("u")); // 其余所有者不受影响
    }

    // ---------- 留痕 ----------

    [Fact]
    public void Mount_Trace_Keywords_Match_Declaration_Order()
    {
        var engine = new LogicEngine();
        var t = BusTestHelpers.RecordingPassive("盾牌", new[] { Updates.CardPlaced, Updates.CardData }, new List<string>());
        engine.Bus.Mount(t);

        var entry = engine.RootStream.Entries.Single(e => e.Keywords.Contains("mount"));
        Assert.Equal("bus", entry.Source);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal(LogEntryKind.Log, entry.Kind);
        Assert.Equal(new[] { "mount", "盾牌", "card.placed", "card.data" }, entry.Keywords); // 等值断言（无隐藏附加词；声明序）
        Assert.Contains("盾牌", entry.Message); // message 含动作词与目标标识（措辞不硬性规定）
    }

    [Fact]
    public void Unmount_Trace_Is_One_Per_Affected_Trigger()
    {
        var engine = new LogicEngine();
        var owner = new object();
        var x = BusTestHelpers.RecordingPassive("X", new[] { Updates.CardPlaced }, new List<string>(), owner: owner);
        var y = BusTestHelpers.RecordingPassive("Y", new[] { Updates.CardDestroyed, Updates.CardPlaced }, new List<string>(), owner: owner);
        engine.Bus.Mount(x);
        engine.Bus.Mount(y);

        engine.Bus.UnmountOwner(owner);

        var traces = engine.RootStream.Entries.Where(e => e.Keywords.Contains("unmount")).ToArray();
        Assert.Equal(2, traces.Length); // 每个受影响触发器一条（集合式断言）
        Assert.Contains(traces, e => e.Keywords.SequenceEqual(new[] { "unmount", "X", "card.placed" }));
        Assert.Contains(traces, e => e.Keywords.SequenceEqual(new[] { "unmount", "Y", "card.destroyed", "card.placed" }));
        Assert.All(traces, e => Assert.Equal("bus", e.Source));
        Assert.All(traces, e => Assert.Equal(LogLevel.Info, e.Level));
    }

    [Fact]
    public async Task Mount_Trace_Inside_Trigger_Event_Goes_To_Executor_Stream()
    {
        var engine = new LogicEngine();
        var t = BusTestHelpers.RecordingPassive("被挂", new[] { "u" }, new List<string>());
        var mounter = new Trigger<CounterView>(name: "挂载者", events: new[]
        {
            new TriggerEvent<CounterView>("挂载操作", (v, c, ct) =>
            {
                engine.Bus.Mount(t);
                return Task.CompletedTask;
            }),
        });

        var mounterStream = await mounter.InvokeAsync(engine, new Dictionary<string, object?>());

        // 触发器事件内动态 Mount：留痕写入该执行者流（与 Emit 同规则）
        Assert.Contains(mounterStream.Entries, e => e.Keywords.Contains("mount") && e.Keywords.Contains("被挂"));
        Assert.Contains(engine.RootStream.Entries, e => e.Keywords.Contains("mount") && e.Keywords.Contains("被挂")); // 冒泡可达
    }

    // ---------- 读面 ----------

    [Fact]
    public async Task GetSubscribers_Returns_Ordered_Snapshot_Matching_Execution_Order()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("A", new[] { "u" }, trace));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("B", new[] { "u" }, trace, priority: UpdatePriorities.High));
        engine.Bus.Mount(BusTestHelpers.RecordingPassive("C", new[] { "u" }, trace));

        var snapshot = engine.Bus.GetSubscribers("u");
        Assert.Equal(new[] { "B", "A", "C" }, snapshot); // 优先级升序→注册序升序

        await engine.Emit("u");
        Assert.Equal(new[] { "B", "A", "C" }, trace); // 读面顺序＝执行序

        engine.Bus.Mount(BusTestHelpers.RecordingPassive("D", new[] { "u" }, trace, priority: UpdatePriorities.High));
        Assert.Equal(new[] { "B", "A", "C" }, snapshot); // 快照语义：已返回快照不受后续挂载影响
        Assert.Equal(new[] { "B", "D", "A", "C" }, engine.Bus.GetSubscribers("u")); // 新调用见更新（High 组内先挂先执行）
    }

    [Fact]
    public void GetSubscribers_Validates_Input_And_Returns_Empty_For_Unknown()
    {
        var engine = new LogicEngine();
        Assert.Throws<ArgumentNullException>(() => engine.Bus.GetSubscribers(null!));
        Assert.Throws<ArgumentException>(() => engine.Bus.GetSubscribers(""));
        Assert.Throws<ArgumentException>(() => engine.Bus.GetSubscribers("   "));
        Assert.Empty(engine.Bus.GetSubscribers("unknown.update")); // 开放集合：未知更新返回空、不报错
    }

    [Fact]
    public async Task Remount_After_Unmount_Uses_New_Registration_Order()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var ownerX = new object();
        var ownerY = new object();
        var x = BusTestHelpers.RecordingPassive("X", new[] { "u" }, trace, owner: ownerX);
        var y = BusTestHelpers.RecordingPassive("Y", new[] { "u" }, trace, owner: ownerY);
        engine.Bus.Mount(x);
        engine.Bus.Mount(y);
        Assert.Equal(new[] { "X", "Y" }, engine.Bus.GetSubscribers("u"));

        engine.Bus.UnmountOwner(ownerX);
        engine.Bus.Mount(x); // 重挂：按新 Mount 时点排最后（同优先级内；不保留旧序）

        Assert.Equal(new[] { "Y", "X" }, engine.Bus.GetSubscribers("u"));
        await engine.Emit("u");
        Assert.Equal(new[] { "Y", "X" }, trace);
    }

    [Fact]
    public async Task Multi_Hook_Trigger_Registration_Is_Consistent_Across_Tables()
    {
        var engine = new LogicEngine();
        var trace = new List<string>();
        var x = BusTestHelpers.RecordingPassive("X", new[] { "a", "b" }, trace);
        var y = BusTestHelpers.RecordingPassive("Y", new[] { "a" }, trace);
        engine.Bus.Mount(x); // x 同时注册到 a、b（注册序在各订阅表内一致）
        engine.Bus.Mount(y);

        Assert.Equal(new[] { "X", "Y" }, engine.Bus.GetSubscribers("a"));
        Assert.Equal(new[] { "X" }, engine.Bus.GetSubscribers("b"));

        await engine.Emit("a");
        await engine.Emit("b");
        Assert.Equal(new[] { "X", "Y", "X" }, trace);
    }

    // ---------- Trigger 构造期校验与旧调用形态 ----------

    [Fact]
    public void Trigger_Construction_Rejects_Active_Kind_With_Hooks()
    {
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(hooks: new[] { "u" })); // 默认 Active + hooks
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(kind: TriggerKind.Active, hooks: new[] { "u" }));
    }

    [Fact]
    public void Trigger_Construction_Rejects_Blank_Or_Duplicate_Hooks()
    {
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(kind: TriggerKind.Passive, hooks: new[] { "" }));
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(kind: TriggerKind.Passive, hooks: new[] { "   " }));
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(kind: TriggerKind.Passive, hooks: new[] { "a", "b", "a" }));
    }

    [Fact]
    public void Trigger_Legacy_Construction_Forms_Remain_Available()
    {
        // 受控变更对照：旧调用形态（位置参数/具名参数）保持完全可用
        var t1 = new Trigger<CounterView>("旧一", TriggerKind.Active, typeof(DamageBands));
        var t2 = new Trigger<CounterView>(name: "旧二", bandType: typeof(DamageBands), events: new[]
        {
            new TriggerEvent<CounterView>("e", (v, c, ct) => Task.CompletedTask, DamageBands.Prevent),
        });

        Assert.Equal("旧一", t1.Name);
        Assert.Equal(TriggerKind.Active, t1.Kind);
        Assert.Equal("旧二", t2.Name);
    }

    private sealed class EqualityOverridingOwner
    {
        public override bool Equals(object? obj) => obj is EqualityOverridingOwner;

        public override int GetHashCode() => 0;
    }
}
