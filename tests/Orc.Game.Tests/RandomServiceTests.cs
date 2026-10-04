using Orc.Core;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 第 2 批 G8（效果级随机服务）验收——服务级测试面：
/// ①原语级测试（数值 <see cref="MatchRandomService.Next"/> / 单取样 <see cref="MatchRandomService.PickOne{T}"/> /
///   多取样 <see cref="MatchRandomService.PickN{T}"/>——行为＋边界：空候选集 / n 边界 / max 边界）；
/// ②确定性复现与防退化（固定种子下与「恒取首项」等退化行为可区分的确定性用例）；
/// ③访问门禁（准备态对外拒绝／进行态可用／终局拒绝——含已持引用路径）；
/// ④脱局降级（ResolveFor 解析 null——功能不可用、不抛错）；
/// ⑤共流断言（洗切与取样共用同一单流：额外洗切 → 后续取样序列变化；组合消费序列复现）；
/// ⑥多对局隔离（同种子一致＋一方消费不改变另一方的后续序列——无全局共享状态）。
/// </summary>
public class RandomServicePrimitiveTests
{
    // ---------- ① 原语级：数值（Next） ----------

    [Fact]
    public void Next_Returns_Values_Within_Range_And_Is_Deterministic_With_Same_Seed()
    {
        var a = new MatchRandomService(123);
        var b = new MatchRandomService(123);

        var valuesA = Enumerable.Range(0, 200).Select(_ => a.Next(10)).ToArray();
        var valuesB = Enumerable.Range(0, 200).Select(_ => b.Next(10)).ToArray();

        Assert.All(valuesA, value => Assert.InRange(value, 0, 9)); // [0, max)
        Assert.Equal(valuesA, valuesB); // 同种子 → 同序列（逐位确定复现）
    }

    [Fact]
    public void Next_Max_One_Is_Always_Zero()
    {
        var service = new MatchRandomService(7);
        Assert.All(Enumerable.Range(0, 20).Select(_ => service.Next(1)), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Next_Max_Zero_Or_Negative_Is_Rejected(int max)
    {
        var service = new MatchRandomService(7);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Next(max));
    }

    [Fact]
    public void Rejected_Requests_Do_Not_Consume_The_Stream()
    {
        var a = new MatchRandomService(99);
        var b = new MatchRandomService(99);

        Assert.Throws<ArgumentOutOfRangeException>(() => a.Next(0)); // 拒绝（零消费）
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Next(-5));

        // 拒绝不推进流：后续序列与未发生拒绝的实例一致（确定性）
        Assert.Equal(b.Next(1000), a.Next(1000));
        Assert.Equal(b.Next(1000), a.Next(1000));
    }

    // ---------- ① 原语级：单取样（PickOne） ----------

    [Fact]
    public void PickOne_Returns_Element_From_Candidates_For_Reference_And_Value_Types()
    {
        var service = new MatchRandomService(21);
        var names = new[] { "a", "b", "c", "d" };

        var picks = Enumerable.Range(0, 60).Select(_ => service.PickOne(names)).ToArray();
        Assert.All(picks, picked => Assert.Contains(picked, names)); // 均来自候选

        var numbers = new[] { 10, 20, 30 };
        Assert.Contains(service.PickOne(numbers), numbers); // 值类型元素（泛型、不依赖元素语义）
    }

    [Fact]
    public void PickOne_Empty_Candidates_Is_Rejected()
    {
        var service = new MatchRandomService(21);
        Assert.Throws<InvalidOperationException>(() => service.PickOne(Array.Empty<string>()));
    }

    [Fact]
    public void PickOne_Null_Candidates_Is_Rejected()
    {
        var service = new MatchRandomService(21);
        Assert.Throws<ArgumentNullException>(() => service.PickOne<string>(null!));
    }

    // ---------- ① 原语级：多取样（PickN——不放回） ----------

    [Fact]
    public void PickN_Returns_Distinct_Elements_Without_Replacement()
    {
        var service = new MatchRandomService(11);
        var candidates = new[] { "a", "b", "c", "d", "e" };

        var picked = service.PickN(candidates, 3);
        Assert.Equal(3, picked.Count);
        Assert.Equal(3, picked.Distinct().Count()); // 互异（不放回）
        Assert.All(picked, item => Assert.Contains(item, candidates)); // 均来自候选

        // n == 候选数：全量取出＝一个排列（互异＋多重集一致）
        var all = service.PickN(candidates, candidates.Length);
        Assert.Equal(candidates.OrderBy(x => x), all.OrderBy(x => x));
    }

    [Fact]
    public void PickN_Zero_Returns_Empty_And_Consumes_Nothing()
    {
        var a = new MatchRandomService(5);
        var b = new MatchRandomService(5);

        Assert.Empty(a.PickN(new[] { "a", "b", "c" }, 0)); // n == 0 → 空列表（零消费）

        // 零消费核验：零取样后的后续序列与未发生零取样的一致
        Assert.Equal(b.Next(1000), a.Next(1000));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void PickN_Out_Of_Range_Is_Rejected(int n)
    {
        var service = new MatchRandomService(5);
        var candidates = new[] { "a", "b", "c" }; // 候选数 3
        Assert.Throws<ArgumentOutOfRangeException>(() => service.PickN(candidates, n));
    }

    [Fact]
    public void PickN_Null_Candidates_Is_Rejected()
    {
        var service = new MatchRandomService(5);
        Assert.Throws<ArgumentNullException>(() => service.PickN<string>(null!, 1));
    }

    [Fact]
    public void PickN_Same_Seed_Same_Result()
    {
        var candidates = new[] { "a", "b", "c", "d", "e", "f" };
        var a = new MatchRandomService(77);
        var b = new MatchRandomService(77);
        Assert.Equal(a.PickN(candidates, 4), b.PickN(candidates, 4)); // 同种子 → 同结果（返回顺序＝逐次选取序、确定复现）
    }

    // ---------- ② 确定性复现与防退化 ----------

    [Fact]
    public void Anti_Degeneration_Same_Seed_Is_Distinguishable_From_Always_First()
    {
        // 固定种子下结果与「恒取首项」等退化行为可区分（确定性用例——非概率性断言）
        var service = new MatchRandomService(2026);
        var picks = Enumerable.Range(0, 30).Select(_ => service.PickOne(new[] { 0, 1, 2 })).ToArray();

        Assert.Contains(picks, picked => picked != 0); // 至少一次选中非首项
        Assert.NotEqual(Enumerable.Repeat(0, 30).ToArray(), picks);
    }

    [Fact]
    public void Mixed_Primitive_Sequence_Replays_With_Same_Seed()
    {
        static List<object> Run(int seed)
        {
            var service = new MatchRandomService(seed);
            return new List<object>
            {
                service.Next(100),
                service.PickOne(new[] { "x", "y", "z" }),
                string.Join(",", service.PickN(new[] { 1, 2, 3, 4, 5 }, 2)),
                service.Next(2),
            };
        }

        Assert.Equal(Run(31337), Run(31337)); // 同种子、同操作序列 → 同结果（复现）
        Assert.NotEqual(Run(31337), Run(31338)); // 不同种子 → 序列变化（种子生效；可选辅助）
    }
}

/// <summary>
/// G8 服务级——访问门禁与脱局降级（对局语境）：
/// ③门禁（准备态对外拒绝／进行态可用／终局拒绝）与 ④脱局降级（ResolveFor 解析 null）。
/// </summary>
public class RandomServiceGateTests
{
    // ---------- ③ 访问门禁 ----------

    [Fact]
    public async Task External_Access_Is_Available_Only_In_Progress_State()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);

        // 准备态（未初始化）：对外取用＝明确拒绝
        Assert.Equal(MatchState.Preparing, match.State);
        Assert.Throws<InvalidOperationException>(() => match.RandomService);

        await match.Initialize();

        // 进行态：可用（取样成功、值域正确）
        Assert.Equal(MatchState.InProgress, match.State);
        var service = match.RandomService;
        Assert.InRange(service.Next(10), 0, 9);
    }

    [Fact]
    public async Task External_Access_Is_Rejected_After_Match_End()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();
        var service = match.RandomService; // 进行态取得引用（终局后仍持有）

        // 终局：敌方 HQ 归零（统一响应链——「先数值变化、后终局记录」）
        await match.Players[1].Hq.ApplyDamageAsync(20);
        Assert.Equal(MatchState.Ended, match.State);

        Assert.Throws<InvalidOperationException>(() => match.RandomService); // 属性面拒绝（终局）
        Assert.Throws<InvalidOperationException>(() => service.Next(10)); // 已持引用亦拒绝（门禁在服务本体）
        Assert.Throws<InvalidOperationException>(() => service.PickOne(new[] { 1, 2, 3 }));
        Assert.Throws<InvalidOperationException>(() => service.PickN(new[] { 1, 2, 3 }, 1));
    }

    // ---------- ④ 脱局降级 ----------

    [Fact]
    public async Task Resolve_For_Detached_Or_Unloaded_Card_Is_Null()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();

        // 独立构造（脱离对局——未经卡库加载）：无归属 → null（功能不可用、不抛错）
        var standalone = ModifierTestKit.CreateBareUnit(new LogicEngine());
        Assert.Null(MatchRandomService.ResolveFor(standalone));

        // 对局卡库实例化但未加载（无归属）→ null
        var unloaded = match.CardLibrary.Instantiate(CommandTestKit.InfantryId);
        Assert.Null(MatchRandomService.ResolveFor(unloaded));

        // 已加载（对局装配路径——归属与注入就绪）→ 对局服务（同一实例；对局级、无隐藏全局单例）
        var loaded = await CommandTestKit.InstantiateLoadedAsync(match, match.Players[0]);
        Assert.Same(match.RandomService, MatchRandomService.ResolveFor(loaded));
        Assert.Same(match.RandomService, MatchRandomService.ResolveFor(match.Players[0].Hq)); // HQ 实体同路径可达
    }
}

/// <summary>
/// G8 服务级——对局集成（对局级设施语义）：
/// ⑤共流断言（洗切与取样共用同一单流）与 ⑥多对局隔离（各自持服务、互不影响——无全局共享状态证据）。
/// </summary>
public class RandomServiceMatchIntegrationTests
{
    // ---------- ⑤ 共流断言（单流证据） ----------

    [Fact]
    public async Task Shuffle_And_Service_Draws_Share_A_Single_Stream()
    {
        // 形态（a）：同种子下额外执行一次洗切后，后续取样结果随预期序列变化
        var m1 = GameTestData.CreateStandardMatch(seed: 777);
        var m2 = GameTestData.CreateStandardMatch(seed: 777);
        await m1.Initialize();
        await m2.Initialize();

        var candidates = Enumerable.Range(0, 100).ToArray();
        var baseline = Enumerable.Range(0, 5).Select(_ => m1.RandomService.PickOne(candidates)).ToArray();

        await m2.ShuffleDeckAsync(m2.Players[0]); // 额外一次洗切（消费同一流）
        var afterExtraShuffle = Enumerable.Range(0, 5).Select(_ => m2.RandomService.PickOne(candidates)).ToArray();

        Assert.NotEqual(baseline, afterExtraShuffle); // 洗切消费影响了后续取样 → 洗切与取样共流（单流证据）

        // 形态（b）：同种子下整体「洗切＋取样」组合消费序列复现
        var m3 = GameTestData.CreateStandardMatch(seed: 777);
        await m3.Initialize();
        await m3.ShuffleDeckAsync(m3.Players[0]);
        var replay = Enumerable.Range(0, 5).Select(_ => m3.RandomService.PickOne(candidates)).ToArray();
        Assert.Equal(afterExtraShuffle, replay); // 同种子＋同操作序列 → 组合序列复现
    }

    // ---------- ⑥ 多对局隔离 ----------

    [Fact]
    public async Task Independent_Matches_Do_Not_Share_Random_State()
    {
        var a = GameTestData.CreateStandardMatch(seed: 2026);
        var b = GameTestData.CreateStandardMatch(seed: 2026);
        var c = GameTestData.CreateStandardMatch(seed: 2026);
        await a.Initialize();
        await b.Initialize();
        await c.Initialize();

        var candidates = Enumerable.Range(0, 6).ToArray();

        // A 大量消费（模拟一方对局活动）
        var seqA = Enumerable.Range(0, 30).Select(_ => a.RandomService.PickOne(candidates)).ToArray();

        // B、C 交错取样 10 次：互不影响（逐位一致）；且与 A 的前 10 次一致（同种子确定复现）
        var seqB = new List<int>();
        var seqC = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            seqB.Add(b.RandomService.PickOne(candidates));
            seqC.Add(c.RandomService.PickOne(candidates));
        }

        Assert.Equal(seqB, seqC); // 「一方消费不改变另一方」：A 的 30 次消费后，B 与 C 的后续序列仍一致
        Assert.Equal(seqB, seqA.Take(10).ToArray()); // 同种子一致：各对局独立流的同位序列逐位相同
    }

    // ---------- 补充：经服务路径洗切＋同种子复现（两对局对比） ----------

    [Fact]
    public async Task Same_Seed_Matches_Shuffle_Identically_Via_Service()
    {
        var m1 = GameTestData.CreateStandardMatch(seed: 555);
        var m2 = GameTestData.CreateStandardMatch(seed: 555);
        await m1.Initialize();
        await m2.Initialize();

        // 经服务路径洗切：同种子 → 双方卡组逐位一致（可复现）
        Assert.Equal(m1.Players[0].Deck.ToArray(), m2.Players[0].Deck.ToArray());
        Assert.Equal(m1.Players[1].Deck.ToArray(), m2.Players[1].Deck.ToArray());
    }
}
