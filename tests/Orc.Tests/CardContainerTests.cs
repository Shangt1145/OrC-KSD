using Orc.Cards;
using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// 验收 C（容器）：数据组件正常存取（同实例）、多类型共存、缺失抛明确异常、重复 Add 拒绝；
/// 逻辑组件（Effects 读面、Add/Remove、重复 Add 同一实例拒绝、移除后重新 Add）；null 防御。
/// </summary>
public class CardContainerTests
{
    // ---------- 数据组件 ----------

    [Fact]
    public void AddData_GetData_Returns_Same_Instance_And_Sharing_Works()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        var health = new HealthData { Hp = 10 };

        card.AddData(health);

        Assert.Same(health, card.GetData<HealthData>()); // 返回同一实例（引用共享）
        health.Hp -= 3;                                   // 外部直接修改字段
        Assert.Equal(7, card.GetData<HealthData>().Hp);   // 修改可见
    }

    [Fact]
    public void AddData_Multiple_Types_Coexist()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        card.AddData(new HealthData { Hp = 10 });
        card.AddData(new SampleData { Value = 3 });

        Assert.Equal(10, card.GetData<HealthData>().Hp);
        Assert.Equal(3, card.GetData<SampleData>().Value);
    }

    [Fact]
    public void GetData_Missing_Throws_Explicit_Exception()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");

        Assert.Throws<KeyNotFoundException>(() => card.GetData<HealthData>()); // 缺失＝明确异常（不返回 null/default）
        Assert.False(card.TryGetData<HealthData>(out var data));               // 试探读面：未添加 → false/null
        Assert.Null(data);
    }

    [Fact]
    public void TryGetData_Returns_Instance_When_Present()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        var health = new HealthData { Hp = 10 };
        card.AddData(health);

        Assert.True(card.TryGetData<HealthData>(out var data));
        Assert.Same(health, data);
    }

    [Fact]
    public void AddData_Duplicate_Type_Rejected_Without_Overwrite()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        var first = new HealthData { Hp = 10 };
        card.AddData(first);

        Assert.Throws<InvalidOperationException>(() => card.AddData(new HealthData { Hp = 99 }));
        Assert.Same(first, card.GetData<HealthData>()); // 不静默覆盖
        Assert.Equal(10, card.GetData<HealthData>().Hp);
    }

    [Fact]
    public void AddData_Null_Rejected()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        Assert.Throws<ArgumentNullException>(() => card.AddData(null!));
    }

    // ---------- 逻辑组件（Effects 读面） ----------

    [Fact]
    public void Effects_ReadFace_Add_Remove_And_Order()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        var e1 = new RecordingPassiveEffect("一", "e1", new List<string>());
        var e2 = new RecordingPassiveEffect("二", "e2", new List<string>());

        Assert.Empty(card.Effects);

        card.AddEffect(e1);
        card.AddEffect(e2);
        Assert.Equal(new Effect[] { e1, e2 }, card.Effects); // 顺序＝添加序

        card.RemoveEffect(e1);
        Assert.Equal(new Effect[] { e2 }, card.Effects);     // 容器读面与运行态一致
    }

    [Fact]
    public void AddEffect_Duplicate_Same_Instance_Rejected_Then_ReAdd_After_Remove()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");
        var e = new RecordingPassiveEffect("一", "e1", new List<string>());

        card.AddEffect(e);
        Assert.Throws<InvalidOperationException>(() => card.AddEffect(e)); // 同一实例重复添加被拒绝

        card.RemoveEffect(e);
        card.AddEffect(e); // 先移除再添加＝允许（复装）
        Assert.Equal(new Effect[] { e }, card.Effects);
    }

    [Fact]
    public void AddEffect_Already_Owned_By_Another_Card_Rejected()
    {
        var engine = new LogicEngine();
        var a = new Card(engine, "甲");
        var b = new Card(engine, "乙");
        var e = new RecordingPassiveEffect("一", "e1", new List<string>());

        a.AddEffect(e);
        Assert.Throws<InvalidOperationException>(() => b.AddEffect(e)); // 已属另一卡牌 → 拒绝

        a.RemoveEffect(e);
        b.AddEffect(e); // 移除（引用清理）后可转移
        Assert.Equal(new Effect[] { e }, b.Effects);
    }

    [Fact]
    public void AddEffect_RemoveEffect_Null_Rejected()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");

        Assert.Throws<ArgumentNullException>(() => card.AddEffect(null!));
        Assert.Throws<ArgumentNullException>(() => card.RemoveEffect(null!));
    }

    [Fact]
    public void Card_Is_Entity_With_Lifecycle_And_Initially_Not_Placed()
    {
        var engine = new LogicEngine();
        var card = new Card(engine, "战士");

        Assert.Equal("战士", card.Name);
        Assert.True(card.Life.IsAlive);
        Assert.Same(card, card.Ref.Value);
        Assert.False(card.IsPlaced); // 放置前＝静态组装
    }
}
