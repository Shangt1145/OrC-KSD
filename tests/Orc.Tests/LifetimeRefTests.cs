using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>验收点①：引用失效（Lifetime / Ref / Entity / 视图 getter / 执行入口传播）。</summary>
public class LifetimeRefTests
{
    [Fact]
    public void Lifetime_IsAlive_Flips_On_Kill()
    {
        var life = new Lifetime();
        Assert.True(life.IsAlive);

        life.Kill();

        Assert.False(life.IsAlive);
    }

    [Fact]
    public void Lifetime_Kill_Is_Idempotent()
    {
        var life = new Lifetime();
        life.Kill();
        life.Kill();
        life.Kill();

        Assert.False(life.IsAlive);
    }

    [Fact]
    public void Entity_Exposes_Name_Life_And_Self_Ref()
    {
        var entity = new Entity("A");

        Assert.Equal("A", entity.Name);
        Assert.True(entity.Life.IsAlive);
        Assert.Equal("A", entity.Ref.Name);
        Assert.True(entity.Ref.IsAlive);
        Assert.Same(entity, entity.Ref.Value);
    }

    [Fact]
    public void Entity_Destroy_Kills_And_Members_Remain_Readable()
    {
        var entity = new Entity("B");

        entity.Destroy();
        entity.Destroy(); // 幂等

        Assert.False(entity.Life.IsAlive);
        Assert.False(entity.Ref.IsAlive);
        // 只杀不管卸载：Destroy 后 Name / Life / Ref 本身访问仍允许
        Assert.Equal("B", entity.Name);
        Assert.NotNull(entity.Life);
        Assert.NotNull(entity.Ref);
    }

    [Fact]
    public void Ref_Value_Throws_StaleReferenceException_After_Kill()
    {
        var entity = new Entity("C");
        var reference = entity.Ref;

        entity.Destroy();

        Assert.Throws<StaleReferenceException>(() => { _ = reference.Value; });
    }

    [Fact]
    public void View_Getter_Throws_StaleReferenceException_When_Ref_Dead()
    {
        var entity = new Entity("D");
        var ctx = new Context(new Dictionary<string, object?>
        {
            ["Target"] = entity.Ref,
            ["Source"] = entity.Ref,
            ["Amount"] = 1,
        });
        var view = ContextViewBinder.Create<SampleView>(ctx);
        Assert.True(view.Target.IsAlive);

        entity.Destroy(); // 绑定后目标失效

        // 读取该 Ref 类型属性时即抛（getter 内当刻校验）
        Assert.Throws<StaleReferenceException>(() => { _ = view.Target; });
    }

    [Fact]
    public async Task Trigger_Isolates_StaleReferenceException_From_Events()
    {
        var entity = new Entity("E");
        var engine = new LogicEngine();
        var trigger = new Trigger<SampleView>(name: "失效联动", events: new[]
        {
            new TriggerEvent<SampleView>("读引用", (view, ctx, ct) =>
            {
                _ = view.Target; // 读取失效引用 → 当刻抛（StaleReferenceException）
                return Task.CompletedTask;
            }),
        });

        entity.Destroy();

        // S2 语义（受控变更）：失效异常被隔离——记录并继续，执行正常返回流
        var stream = await trigger.InvokeAsync(engine, new Dictionary<string, object?>
        {
            ["Target"] = entity.Ref,
            ["Source"] = entity.Ref,
            ["Amount"] = 1,
        });

        Assert.Contains(stream.Entries, e => e.Keywords.Contains("exception:StaleReferenceException"));
    }
}
