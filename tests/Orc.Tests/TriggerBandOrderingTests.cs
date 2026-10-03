using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收点①：band/优先级排序（band 值×1000＋band 内优先级、注册序兜底、别名同区段）与
/// 注册期校验（数值契约 + 二选一保护矩阵 + 执行时点快照）。
/// </summary>
public class TriggerBandOrderingTests
{
    private static Task Nop(CounterView view, Context ctx, CancellationToken ct) => Task.CompletedTask;

    private static TriggerEvent<CounterView> Ev(string name, List<string> order, int priority = 0)
        => new(name, (v, c, t) => { order.Add(name); return Task.CompletedTask; }, priority);

    private static TriggerEvent<CounterView> EvB(DamageBands band, string name, List<string> order, int priority = 0)
        => new(name, (v, c, t) => { order.Add(name); return Task.CompletedTask; }, band, priority);

    [Fact]
    public async Task Default_Scheme_Orders_By_Priority_Then_Registration_Sequence()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        var trigger = new Trigger<CounterView>(events: new[]
        {
            Ev("p5", order, priority: 5),
            Ev("p0-a", order),
            Ev("p0-b", order),
        });

        await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "p0-a", "p0-b", "p5" }, order); // 优先级升序；同优先级→注册序兜底
    }

    [Fact]
    public async Task Band_Value_Times_1000_Plus_Priority_Defines_Order_Across_Bands()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        var trigger = new Trigger<CounterView>(bandType: typeof(DamageBands), events: new[]
        {
            EvB(DamageBands.Finalize, "finalize", order),        // 500*1000+0
            EvB(DamageBands.Prevent, "prevent-999", order, 999), // 100*1000+999
            EvB(DamageBands.Reduce, "reduce-0", order),          // 300*1000+0
            EvB(DamageBands.Prevent, "prevent-0", order),        // 100*1000+0
        });

        await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        // band 支配跨区段顺序（prevent-999 仍先于 reduce-0）；区段内按优先级
        Assert.Equal(new[] { "prevent-0", "prevent-999", "reduce-0", "finalize" }, order);
    }

    [Fact]
    public async Task Same_Sort_Key_Uses_Registration_Sequence_And_Alias_Is_Same_Section()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        var trigger = new Trigger<CounterView>(bandType: typeof(DamageBands), events: new[]
        {
            EvB(DamageBands.Prevent, "a", order),
            EvB(DamageBands.AliasPrevent, "b", order), // 同值别名：同一区段（排序键相同）→ 注册序兜底
            EvB(DamageBands.Prevent, "c", order),
        });

        await trigger.InvokeAsync(engine, new Dictionary<string, object?>());

        Assert.Equal(new[] { "a", "b", "c" }, order);
    }

    [Fact]
    public void Registration_Rejects_Out_Of_Range_Band_Values_And_Priority()
    {
        var trigger = new Trigger<CounterView>(bandType: typeof(BoundaryBands));

        Assert.Throws<ArgumentOutOfRangeException>(() => trigger.Register("neg", Nop, BoundaryBands.Negative));
        Assert.Throws<ArgumentOutOfRangeException>(() => trigger.Register("huge", Nop, BoundaryBands.TooLarge));
        Assert.Throws<ArgumentOutOfRangeException>(() => trigger.Register("p-neg", Nop, BoundaryBands.Normal, priority: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => trigger.Register("p-1000", Nop, BoundaryBands.Normal, priority: 1000));

        trigger.Register("max-ok", Nop, BoundaryBands.Max);                     // 上限 1,000,000 合法
        trigger.Register("p-999-ok", Nop, BoundaryBands.Normal, priority: 999); // 上界（不含 1000）合法
    }

    [Fact]
    public void Two_Choice_Protection_Passes_For_Allowed_Combinations()
    {
        var defaultScheme = new Trigger<CounterView>();
        defaultScheme.Register("no-band", Nop);                              // 默认＋无 band → 落 default
        defaultScheme.Register("default-member", Nop, DefaultBands.Default); // 默认＋默认枚举成员 → 通过

        var dedicated = new Trigger<CounterView>(bandType: typeof(DamageBands));
        dedicated.Register("own", Nop, DamageBands.Prevent);                 // 专门＋本枚举成员 → 通过
    }

    [Fact]
    public void Two_Choice_Protection_Rejects_Mismatched_Combinations()
    {
        var defaultScheme = new Trigger<CounterView>();
        Assert.Throws<ArgumentException>(
            () => defaultScheme.Register("foreign", Nop, DamageBands.Prevent)); // 默认＋他类枚举成员 → 拒绝

        var dedicated = new Trigger<CounterView>(bandType: typeof(DamageBands));
        Assert.Throws<ArgumentException>(
            () => dedicated.Register("omitted", Nop));                          // 专门＋省略 band → 报错
        Assert.Throws<ArgumentException>(
            () => dedicated.Register("foreign1", Nop, DefaultBands.Default));   // 专门＋他类枚举成员 → 拒绝
        Assert.Throws<ArgumentException>(
            () => dedicated.Register("foreign2", Nop, BoundaryBands.Normal));   // 专门＋他类枚举成员 → 拒绝
    }

    [Fact]
    public void Construction_Validates_Band_Type_And_Initial_Events()
    {
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(bandType: typeof(int))); // 方案须为枚举类型

        // 专门方案下，构造集合中省略 band 同样被拒绝（构造与注册为等价通道）
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(
            bandType: typeof(DamageBands),
            events: new[] { new TriggerEvent<CounterView>("x", Nop) }));

        // 默认方案下，构造集合携带他类枚举成员同样被拒绝
        Assert.Throws<ArgumentException>(() => new Trigger<CounterView>(
            events: new[] { new TriggerEvent<CounterView>("x", Nop, DamageBands.Prevent) }));
    }

    [Fact]
    public async Task Registration_During_Iteration_Does_Not_Affect_Current_Execution()
    {
        var engine = new LogicEngine();
        var order = new List<string>();
        Trigger<CounterView>? trigger = null;

        trigger = new Trigger<CounterView>(events: new[]
        {
            new TriggerEvent<CounterView>("一", (v, c, t) =>
            {
                order.Add("一");
                trigger!.Register("插入", (v2, c2, t2) => { order.Add("插入"); return Task.CompletedTask; }); // 执行中注册
                return Task.CompletedTask;
            }),
            new TriggerEvent<CounterView>("二", (v, c, t) => { order.Add("二"); return Task.CompletedTask; }),
        });

        await trigger.InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.Equal(new[] { "一", "二" }, order); // 本次迭代使用执行时点快照，不受新注册影响

        await trigger.InvokeAsync(engine, new Dictionary<string, object?>());
        Assert.Equal(new[] { "一", "二", "一", "二", "插入" }, order); // 下次迭代包含新增注册
    }
}
