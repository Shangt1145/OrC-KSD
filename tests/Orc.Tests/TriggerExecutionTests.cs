using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点②：嵌套触发与共享 ctx——同一执行会话内视图与全部事件共用同一 ctx 载体（Bind 直连、不拷贝）；
/// 父子执行载体独立、数据流严格显式（不继承）；子流自动挂载到触发者流。
/// 验收点③：伤害示例——同一主动触发器内两事件（数值修正→伤害生效）按 band 排序执行、ctx 数据随执行演进。
/// </summary>
public class TriggerExecutionTests
{
    [Fact]
    public async Task Nested_Trigger_Creates_Own_Context_And_Auto_Mounts_Stream()
    {
        var engine = new LogicEngine();
        var marker = new Entity("共享标记");
        Context? parentCtx1 = null;
        Context? parentCtx2 = null;
        Context? childCtx1 = null;
        Context? childCtx2 = null;
        EventStream? childStream = null;
        var order = new List<string>();

        var child = new Trigger<CounterView>(name: "子触发器", events: new[]
        {
            new TriggerEvent<CounterView>("子1", (v, c, t) => { childCtx1 = c; order.Add("child1"); return Task.CompletedTask; }),
            new TriggerEvent<CounterView>("子2", (v, c, t) =>
            {
                childCtx2 = c;
                order.Add("child2");
                Assert.Equal(0, v.Count);           // 无继承：子执行数据不含父执行数据
                Assert.Same(marker.Ref, v.Payload); // 显式传入同一实例（跨执行共享的唯一来源）
                v.Count = 7;
                return Task.CompletedTask;
            }),
        });

        var parent = new Trigger<CounterView>(name: "父触发器", events: new[]
        {
            new TriggerEvent<CounterView>("父1", (v, c, t) => { parentCtx1 = c; order.Add("parent1"); v.Count = 1; return Task.CompletedTask; }),
            new TriggerEvent<CounterView>("父2", async (v, c, t) =>
            {
                parentCtx2 = c;
                order.Add("parent2-start");
                Assert.Equal(1, v.Count);        // 同一执行会话内：父1 的改写可见（Bind 直连、不拷贝）
                Assert.Equal(1, (int)c.Get("Count")!);
                childStream = await child.InvokeAsync(engine, new Dictionary<string, object?>
                {
                    ["Payload"] = marker.Ref,    // 数据流严格显式：仅显式传入项进入子执行
                });
                order.Add("parent2-end");
            }),
        });

        var parentStream = await parent.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Same(parentCtx1, parentCtx2);   // 同一执行会话：视图与全部事件共用同一 ctx 载体
        Assert.Same(childCtx1, childCtx2);     // 同（子执行）
        Assert.NotSame(parentCtx1, childCtx1); // 链上各执行载体独立（跨触发器不共享载体）
        Assert.Equal(new[] { "parent1", "parent2-start", "child1", "child2", "parent2-end" }, order);

        Assert.NotNull(childStream);
        Assert.Same(parentStream, childStream!.Parent);        // 子流自动挂载到当时的触发者流（无需显式关联）
        Assert.Contains(childStream, parentStream.Children);
        Assert.Equal(engine.RootStream, parentStream.Parent);  // 无父执行者：顶层流挂到总流
        Assert.NotEqual(parentStream.Id, childStream.Id);      // 流唯一 ID

        Assert.Equal(7, (int)childCtx2!.Get("Count")!);        // 子执行独立演进
        Assert.Equal(1, (int)parentCtx2!.Get("Count")!);       // 父执行不受子执行影响
    }

    [Fact]
    public async Task Damage_Example_Modifier_Then_Apply_By_Band_Order_With_Evolving_Data()
    {
        var engine = new LogicEngine();
        var source = new Entity("法师");
        var target = new Entity("守卫");
        var trace = new List<string>();
        Context? captured = null;

        var trigger = new Trigger<DamageView>(
            name: "伤害结算",
            kind: TriggerKind.Active,
            bandType: typeof(DamageBands),
            events: new[]
            {
                new TriggerEvent<DamageView>("数值修正", (view, ctx, ct) =>
                {
                    captured = ctx;
                    var before = view.Amount;
                    if (view.Modifier > 0)
                    {
                        view.Amount = Math.Max(0, before - view.Modifier); // [Mutate] 改写：ctx 数据随执行演进
                    }

                    trace.Add($"数值修正:{before}->{view.Amount}");
                    return Task.CompletedTask;
                }, DamageBands.Prevent),
                new TriggerEvent<DamageView>("伤害生效", (view, ctx, ct) =>
                {
                    trace.Add($"伤害生效:{view.Amount}"); // 读取演进后的最终值
                    return Task.CompletedTask;
                }, DamageBands.Finalize),
            });

        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>
        {
            ["Source"] = source.Ref,
            ["Target"] = target.Ref,
            ["Amount"] = 5,
            ["Modifier"] = 2,
        });

        Assert.Equal(new[] { "数值修正:5->3", "伤害生效:3" }, trace); // 按 band 排序执行 + 数据演进
        Assert.Equal(3, (int)captured!.Get("Amount")!);              // 最终数据经载体可见
        Assert.NotNull(stream);
    }

    [Fact]
    public void Kind_And_Name_Are_Readable_And_Kind_Defaults_To_Active()
    {
        var defaultKind = new Trigger<CounterView>();
        var passive = new Trigger<CounterView>(name: "被动示例", kind: TriggerKind.Passive);

        Assert.Equal(TriggerKind.Active, defaultKind.Kind); // 默认主动
        Assert.Null(defaultKind.Name);
        Assert.Equal(TriggerKind.Passive, passive.Kind);    // 被动需显式声明；运行期可读
        Assert.Equal("被动示例", passive.Name);
    }
}
