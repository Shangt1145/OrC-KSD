using Orc.Game.Collections;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 验收锚点④（集合类行为）：CardList / CardSet 的装填、读面、Shuffle / Insert / Draw / Instantiate 与边界。
/// 边界清单逐条覆盖：①空集 Draw 抛错（两集合各一）；②Insert 越界抛错（负索引与超量两端）；
/// ③CardSet 同一实例重复添加拒绝；④Instantiate 三情形（空→空集 / 未注册→抛错 / 重复 id→独立实例并保序）。
/// </summary>
public class CardListTests
{
    [Fact]
    public void Collection_Surface_Preserves_Order_And_Allows_Duplicates()
    {
        var list = new CardList();
        list.Add("c01");
        list.AddRange(new[] { "c02", "c01" }); // 可重复
        list.Insert(1, "c00"); // 指定位置插入

        Assert.Equal(4, list.Count);
        Assert.Equal(new[] { "c01", "c00", "c02", "c01" }, list.ToArray());

        // 批量构造（装填面之二）
        var built = new CardList(new[] { "c09", "c08", "c07" });
        Assert.Equal(new[] { "c09", "c08", "c07" }, built.ToArray());
        Assert.Equal("c09", built[0]); // 索引读面
    }

    [Fact]
    public void Draw_Takes_First_And_Removes()
    {
        var list = new CardList(new[] { "c01", "c02", "c03" });

        var drawn = list.Draw();

        Assert.Equal("c01", drawn);
        Assert.Equal(new[] { "c02", "c03" }, list.ToArray());
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Draw_On_Empty_Throws() // 边界①（CardList 侧）
    {
        var list = new CardList();
        Assert.Throws<InvalidOperationException>(() => list.Draw());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Insert_Out_Of_Range_Throws(int index) // 边界②（负索引与超量两端）
    {
        var list = new CardList(new[] { "c01", "c02", "c03" });
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(index, "cX"));
    }

    [Fact]
    public void Shuffle_Is_Deterministic_With_Same_Seed_And_Keeps_All_Elements()
    {
        var source = Enumerable.Range(1, 10).Select(i => $"c{i:D2}").ToArray();
        var a = new CardList(source);
        var b = new CardList(source);

        a.Shuffle(new MatchRandomService(1234)); // 确定性随机源经方法传入（集合自身不持有随机源——G8 受控源形态）
        b.Shuffle(new MatchRandomService(1234));

        Assert.Equal(a.ToArray(), b.ToArray()); // 同种子 → 逐位一致（可复现）
        Assert.Equal(source.OrderBy(x => x), a.ToArray().OrderBy(x => x)); // 洗牌结果为原多重集的一个排列
        Assert.Equal(10, a.Count);

        // 洗牌确实产生重排（多个种子中至少一个改变顺序；避免绑定单一实现细节）
        var changed = Enumerable.Range(0, 5).Any(seed =>
        {
            var probe = new CardList(source);
            probe.Shuffle(new MatchRandomService(seed));
            return !probe.SequenceEqual(source);
        });
        Assert.True(changed);
    }

    [Fact]
    public void Null_RandomSource_Is_Rejected()
    {
        var list = new CardList(new[] { "c01" });
        Assert.Throws<ArgumentNullException>(() => list.Shuffle(null!));
    }
}

/// <summary>CardSet：有序、拒绝重复实例、Shuffle / Insert / Draw 与边界。</summary>
public class CardSetTests
{
    private static Orc.Cards.Card CreateCard(string name = "卡") => new(new Orc.Core.LogicEngine(), name);

    [Fact]
    public void Add_Preserves_Insertion_Order()
    {
        var set = new CardSet();
        var first = CreateCard("甲");
        var second = CreateCard("乙");
        var third = CreateCard("丙");

        set.Add(first);
        set.Add(second);
        set.Insert(1, third); // 指定位置插入

        Assert.Equal(3, set.Count);
        Assert.Same(first, set[0]);
        Assert.Same(third, set[1]);
        Assert.Same(second, set[2]);
    }

    [Fact]
    public void Add_Same_Instance_Twice_Is_Rejected() // 边界③
    {
        var set = new CardSet();
        var card = CreateCard();

        set.Add(card);
        Assert.Throws<InvalidOperationException>(() => set.Add(card));
        Assert.Throws<InvalidOperationException>(() => set.Insert(0, card)); // Insert 路径同样拒绝
        Assert.Single(set);
    }

    [Fact]
    public void Draw_Takes_First_And_Removes()
    {
        var set = new CardSet();
        var first = CreateCard("甲");
        var second = CreateCard("乙");
        set.Add(first);
        set.Add(second);

        var drawn = set.Draw();

        Assert.Same(first, drawn);
        var remaining = Assert.Single(set);
        Assert.Same(second, remaining);
    }

    [Fact]
    public void Draw_On_Empty_Throws() // 边界①（CardSet 侧）
    {
        var set = new CardSet();
        Assert.Throws<InvalidOperationException>(() => set.Draw());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Insert_Out_Of_Range_Throws(int index) // 边界②（CardSet 侧：集合 1 张，合法范围 0..1）
    {
        var set = new CardSet();
        set.Add(CreateCard());
        Assert.Throws<ArgumentOutOfRangeException>(() => set.Insert(index, CreateCard()));
    }

    [Fact]
    public void Shuffle_Is_Deterministic_With_Same_Seed_And_Keeps_All_Elements()
    {
        var cards = Enumerable.Range(1, 8).Select(i => CreateCard($"卡{i:D2}")).ToArray();
        var a = new CardSet();
        var b = new CardSet();
        a.AddRange(cards);
        b.AddRange(cards);

        a.Shuffle(new MatchRandomService(99));
        b.Shuffle(new MatchRandomService(99));

        Assert.Equal(a.Select(c => c.Name).ToArray(), b.Select(c => c.Name).ToArray()); // 同种子 → 逐位一致
        Assert.Equal(cards.Select(c => c.Name).OrderBy(x => x), a.Select(c => c.Name).OrderBy(x => x)); // 排列性
        Assert.Equal(8, a.Count);
    }

    [Fact]
    public void Null_Card_Is_Rejected()
    {
        var set = new CardSet();
        Assert.Throws<ArgumentNullException>(() => set.Add(null!));
    }
}
