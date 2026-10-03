using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点④：触发入口三形态——具名重载（作者侧书写）经视图手写 Translate 汇入统一入口；
/// 字典透传零加工直入；两条路径均可正常执行。转接语义：引用类同一 Ref 实例入、值类快照入、改写可见性。
/// </summary>
public class TriggerInvocationTests
{
    /// <summary>具名重载载体（作者侧书写）：按 source/amount/target 参数形态经手写 Translate 汇入统一入口。</summary>
    private sealed class DamageTrigger : Trigger<DamageView>
    {
        private readonly List<string> _trace;

        public DamageTrigger(List<string> trace, string? name = null)
            : base(name, TriggerKind.Active, typeof(DamageBands))
        {
            _trace = trace;
            Register("护盾检查", OnShieldCheck, DamageBands.Prevent);
            Register("伤害生效", OnDamageApplied, DamageBands.Finalize);
        }

        /// <summary>具名重载：具名参数 → 手写转接（Translate）→ 统一入口。</summary>
        public Task<EventStream> InvokeAsync(
            LogicEngine engine,
            Ref<Entity> source,
            int amount,
            Ref<Entity> target,
            int shield = 0,
            CancellationToken ct = default)
        {
            var data = new Dictionary<string, object?>();
            DamageView.Translate(engine, source, amount, target, shield, data);
            return InvokeAsync(engine, data, ct);
        }

        private Task OnShieldCheck(DamageView view, Context ctx, CancellationToken ct)
        {
            var before = view.Amount;
            if (view.Shield > 0)
            {
                view.Amount = Math.Max(0, before - view.Shield);
            }

            _trace.Add($"护盾检查:{before}->{view.Amount}");
            return Task.CompletedTask;
        }

        private Task OnDamageApplied(DamageView view, Context ctx, CancellationToken ct)
        {
            _trace.Add($"伤害生效:{view.Amount}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Translate_Writes_Same_Ref_Instances_And_Snapshots_Value_Parameters()
    {
        var engine = new LogicEngine();
        var source = new Entity("A");
        var target = new Entity("B");
        var data = new Dictionary<string, object?>();

        var amount = 5;
        DamageView.Translate(engine, source.Ref, amount, target.Ref, shield: 2, data);

        Assert.Same(source.Ref, data["Source"]); // 引用类：同一 Ref 实例入（ReferenceEquals，不拷贝包装/目标）
        Assert.Same(target.Ref, data["Target"]);
        Assert.Equal(5, data["Amount"]);         // 值类：快照入（直接透传赋值，无包装 API）
        Assert.Equal(2, data["Shield"]);

        amount = 999;                            // 调用方后续变化不影响已入值
        Assert.Equal(5, data["Amount"]);

        data["Amount"] = 1;                      // ctx 内改写不回写调用方原值（双向独立）
        Assert.Equal(999, amount);
    }

    [Fact]
    public async Task Named_Overload_Flows_Through_Translate_Into_Unified_Entry()
    {
        var engine = new LogicEngine();
        var source = new Entity("术士");
        var target = new Entity("傀儡");
        var trace = new List<string>();
        var trigger = new DamageTrigger(trace, name: "火球伤害");

        var stream = await trigger.InvokeAsync(engine, source.Ref, 5, target.Ref, shield: 2);

        Assert.Equal(new[] { "护盾检查:5->3", "伤害生效:3" }, trace); // band 排序执行 + 数据随执行演进
        Assert.Same(engine.RootStream, stream.Parent);                 // 经统一入口正常产出与挂载
    }

    [Fact]
    public async Task Mutate_Ref_Property_Rewrites_Key_Visible_Within_Same_Execution()
    {
        var engine = new LogicEngine();
        var original = new Entity("原");
        var replacement = new Entity("替");
        Context? captured = null;

        var trigger = new Trigger<LinkView>(events: new[]
        {
            new TriggerEvent<LinkView>("改写", (view, ctx, ct) =>
            {
                captured = ctx;
                view.Link = replacement.Ref; // [Mutate] 改写引用键
                return Task.CompletedTask;
            }),
            new TriggerEvent<LinkView>("复核", (view, ctx, ct) =>
            {
                Assert.Same(replacement.Ref, view.Link); // 同一次执行内其他读取可见新值
                return Task.CompletedTask;
            }),
        });

        await trigger.InvokeAsync(engine, new Dictionary<string, object?> { ["Link"] = original.Ref });

        Assert.Same(replacement.Ref, (Ref<Entity>)captured!.Get("Link")!); // 经 ctx 载体可见改写结果
    }

    [Fact]
    public async Task Pass_Through_Dictionary_Is_Adopted_Without_Processing_And_Copied_Once()
    {
        var engine = new LogicEngine();
        var marker = new Entity("标记");
        var external = new Dictionary<string, object?> { ["Count"] = 1, ["Payload"] = marker.Ref };
        Context? captured = null;

        var trigger = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("观察", (view, ctx, ct) =>
            {
                captured = ctx;
                Assert.Equal(1, view.Count);            // 零加工：就绪字典内容原样成为本次执行数据
                Assert.Same(marker.Ref, view.Payload);  // 引用原样（无包装）
                external["Count"] = 99;                 // 执行中改外部字典：本次执行不受影响（入料已拷贝一次）
                Assert.Equal(1, (int)ctx.Get("Count")!);
                return Task.CompletedTask;
            }),
        });

        await trigger.InvokeAsync(engine, external);

        Assert.Equal(99, external["Count"]);
        Assert.Equal(1, (int)captured!.Get("Count")!);
    }

    [Fact]
    public async Task Engine_Reference_Reaches_Handlers_Via_Author_Managed_Data_And_Is_Never_Injected()
    {
        var engine = new LogicEngine();
        var subExecuted = false;
        var sub = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("子", (v, c, t) => { subExecuted = true; return Task.CompletedTask; }),
        });

        var outer = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("外", async (view, ctx, ct) =>
            {
                var eng = (LogicEngine)ctx.Get("Engine")!; // 作者显式放入 + 经 ctx 数据取用（合法路径）
                await sub.InvokeAsync(eng, new Dictionary<string, object?>());
            }),
        });

        await outer.InvokeAsync(engine, new Dictionary<string, object?> { ["Engine"] = engine });

        Assert.True(subExecuted);

        // 框架不自动把引擎引用注入 ctx 数据、不占用/不保留键名：
        var probe = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("探", (v, c, t) =>
            {
                Assert.False(c.TryGet("Engine", out _));
                return Task.CompletedTask;
            }),
        });

        await probe.InvokeAsync(engine, new Dictionary<string, object?>());
    }
}
