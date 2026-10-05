using Orc.Cards;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Targeting;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// C2「开发/发现」完整链——定义级链验收测试（①主场景＋确定性/防退化＋取消/池边界）：
/// ① 全链（三选一 → 生成 → 落位〔手牌〕）＝经真实效果链驱动（装配→装载→执行→取用→出题→提交→生成→落位）；
///    断言面＝取样证据组（互异/∈池/逐项一致）＋禁旁路行为锚点（构筑外 ID/桥接驱动）＋交互两端（请求描述＋提交与产出读取）；
/// ② 确定性（同种子复现）＋防退化（换种子变化——池＞N 配置）＝「真经对局随机服务」证据组；
/// ③ 取消（必验）＝终局取消＋零副作用＋取消后隔离（同链下一次执行正常完成）；
/// ④ 池不足退化（按 Q&A-2）：M=0 ＝链级终止（不构造选择器/零副作用/不抛）；M≥1（含 M&lt;N）＝照常完成。
/// </summary>
public class C2DevelopChainTests
{
    // ---------- ① 全链（定义级/三选一 → 生成 → 落位〔手牌〕）——效果链驱动 ----------

    [Fact]
    public async Task Scenario1_Full_Chain_Listings_Pick3_Choose1_Generate_Then_Place_To_Hand()
    {
        var bridge = new MockTargeterBridge();
        var registry = C2Kit.CreateRegistry(3, C2Destination.Hand);
        var match = C2Kit.CreateSceneMatch(bridge, registry, seed: 42);
        await match.Initialize();
        var playerA = match.Players[0];

        // 效果装配（工厂/声明）→ 装载（卡加载时点）→ 执行（施放）
        var (_, effect) = await C2Kit.PrepareEmitterAsync(match, playerA);
        using var recorder = new UpdateRecorder(match.Engine);
        recorder.Clear();
        var handCountBefore = playerA.Hand.Count;

        var castTask = effect.CastAsync(match.Engine);
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        // —— 取样证据组（经对局随机服务、不放回） ——
        Assert.True(effect.ServicesResolved); // 三个上下文取用面（随机/卡牌/目标器）均可达
        var drawn = effect.LastDrawnIds;
        Assert.Equal(3, drawn.Count); // 三选一（N=3；池＞N）
        Assert.Equal(drawn.Count, drawn.Distinct().Count()); // 名单互异（不放回直接证据）
        Assert.All(drawn, id => Assert.True(match.CardLibrary.Definitions.ContainsKey(id))); // 元素∈池
        // 池＝本局卡牌库已注册定义的全体（经枚举读面；无筛选场景——规范化排序后逐项一致）
        Assert.Equal(
            match.CardLibrary.Definitions.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            effect.LastPool.ToArray());

        // —— 交互两端①：出题请求描述（呈现类别＝卡牌阵列＋载荷一致性＋槽位形态） ——
        Assert.Empty(bridge.CollectCalls); // 无收集需求（非引用类请求——不经收集与筛选链）
        var slot = Assert.Single(description.Slots);
        Assert.Equal(C2Kit.SlotName, slot.Name);
        Assert.Equal(TargetSlotKind.CardPicker, slot.Kind);
        Assert.Equal(TargetSlotPresentation.CardArray, slot.Presentation);
        Assert.NotNull(slot.CardListings);
        Assert.Null(slot.AllowedReferences);
        Assert.Null(slot.Options);
        Assert.Equal(drawn.ToArray(), slot.CardListings!.Select(l => l.Id).ToArray()); // 请求登场名单＝取样结果（逐项含条目数）
        Assert.Equal(
            drawn.Select(id => match.CardLibrary.Definitions[id].Name).ToArray(),
            slot.CardListings!.Select(l => l.Name).ToArray()); // 可读名称级呈现要素

        // —— 交互两端②：提交与产出读取（按槽位名读出选中标识） ——
        var chosen = drawn[1]; // 固定选择（不消费随机——锚点稳定性）
        Assert.True(responder.Complete(
            description.RequestId, TargeterTestKit.IdentifierSelection(C2Kit.SlotName, chosen)));
        await castTask;

        Assert.Null(effect.LastError);
        Assert.Equal(TargetingStatus.Success, effect.LastTargetingStatus);
        Assert.Equal(chosen, effect.LastSelectedId);

        // —— 生成实例（真经创建面——构筑外 ID 行为锚点） ——
        var result = effect.LastPlaceResult!;
        Assert.Equal(CardPlaceStatus.Placed, result.Status);
        Assert.NotNull(result.Card);
        var generated = result.Card!;
        Assert.True(match.CardLibrary.TryGetRegisteredId(generated.Definition, out var generatedId));
        Assert.Equal(chosen, generatedId);
        Assert.True(match.IsOutsideDeck(generated)); // 新实例 ID ＞ 水位线＝构筑外（旁路则锚点失效）

        // —— 落位（手牌——尾部追加＋card.hand.add 恰一次；真经放置面） ——
        Assert.Equal(handCountBefore + 1, playerA.Hand.Count);
        Assert.Same(generated, playerA.Hand[^1]);
        Assert.Equal(1, recorder.CountOf(GameUpdates.CardHandAdd));
        var handAddPayload = recorder.PayloadOf(GameUpdates.CardHandAdd)!;
        Assert.Same(generated, handAddPayload[GameUpdates.PayloadCard]);
        Assert.Same(playerA, handAddPayload[GameUpdates.PayloadPlayer]);
        Assert.Equal(1, recorder.CountOf(GameUpdates.CardLoad)); // 创建事实（加载链照常）
    }

    // ---------- ② 确定性（同种子复现）＋防退化（换种子变化） ----------

    [Fact]
    public async Task Scenario1_Determinism_Same_Seed_Same_Operation_Order_Replays_Identical_Draw_List()
    {
        var first = await RunChainAsync(seed: 777);
        var second = await RunChainAsync(seed: 777);

        // 两个同种子对局（独立构造）、各自执行同序操作序列 → 取样名单一致
        // （完整操作序一致性：含初始化洗切等同一随机流消费——两次运行代码路径完全一致）
        Assert.Equal(first.Drawn, second.Drawn);
        Assert.Equal(first.SelectedId, second.SelectedId);
        Assert.Equal(first.GeneratedName, second.GeneratedName);
        Assert.True(first.Placed && second.Placed);

        // 回归锚点（同种子 777 的确定性输出——锚点值同步登记于《实现记录-C2》）
        Assert.Equal(new[] { "u_inf", "u_furytank", "u_cnt" }, first.Drawn.ToArray());
    }

    [Fact]
    public async Task Scenario1_Anti_Degeneration_Different_Seed_Changes_Draw_List_Pool_Greater_Than_N()
    {
        // 池＞N 配置（池＝全库 26 枚 ＞ N=3）——任何种子都取满 3 张，名单变化直接证明「真经随机服务」（非恒取注册序前 N）
        var baseline = await RunChainAsync(seed: 777);
        var alternate = await RunChainAsync(seed: 778);

        Assert.NotEqual(baseline.Drawn, alternate.Drawn);
        Assert.NotEqual(baseline.SelectedId, alternate.SelectedId);

        // 对照锚点（种子 778 的确定性输出——与 777 组对照；同步登记于《实现记录-C2》）
        Assert.Equal(new[] { "u_cmd", "u_art", "u_cost2" }, alternate.Drawn.ToArray());

        // 防退化补充证据：锚点名单非注册序前 N（注册序前 3 为定制定义集起始段）
        Assert.NotEqual(
            new[] { "u_inf", "u_tank", "u_art" }, baseline.Drawn.ToArray());
    }

    // ---------- ③ 取消（必验）＋取消后隔离 ----------

    [Fact]
    public async Task Scenario1_Cancel_Terminates_Chain_With_Zero_Side_Effects_Then_Next_Run_Succeeds()
    {
        var bridge = new MockTargeterBridge();
        var registry = C2Kit.CreateRegistry(3, C2Destination.Hand);
        var match = C2Kit.CreateSceneMatch(bridge, registry, seed: 42);
        await match.Initialize();
        var playerA = match.Players[0];

        var (_, effect) = await C2Kit.PrepareEmitterAsync(match, playerA);
        using var recorder = new UpdateRecorder(match.Engine);
        recorder.Clear();
        var handBefore = playerA.Hand.Count;
        var deckBefore = playerA.Deck.Count;

        // —— 第一段：出题 → 取消（前端主动放弃） ——
        var castTask = effect.CastAsync(match.Engine);
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        Assert.True(responder.Cancel(description.RequestId));
        await castTask;

        // 终局＝取消（三态之一、不抛）＋链侧零副作用（不生成/不落位/无信号）
        Assert.Null(effect.LastError);
        Assert.Equal(TargetingStatus.Cancelled, effect.LastTargetingStatus);
        Assert.Equal(TargetingEndReason.PlayerCancelled, effect.LastTargetingReason);
        Assert.Null(effect.LastSelectedId);
        Assert.Null(effect.LastPlaceResult);
        Assert.Equal(handBefore, playerA.Hand.Count);
        Assert.Equal(deckBefore, playerA.Deck.Count);
        Assert.Equal(0, recorder.TotalCount());

        // —— 第二段：取消后隔离（同一条链的下一次执行仍可正常完成——新一轮请求 → 成功完成全链） ——
        var castTask2 = effect.CastAsync(match.Engine);
        var (description2, responder2) = await bridge.WaitForNextBeginAsync();
        var chosen2 = effect.LastDrawnIds[0];
        Assert.True(responder2.Complete(
            description2.RequestId, TargeterTestKit.IdentifierSelection(C2Kit.SlotName, chosen2)));
        await castTask2;

        Assert.Null(effect.LastError);
        Assert.Equal(TargetingStatus.Success, effect.LastTargetingStatus);
        Assert.Equal(chosen2, effect.LastSelectedId);
        var result = effect.LastPlaceResult!;
        Assert.Equal(CardPlaceStatus.Placed, result.Status);
        Assert.Equal(handBefore + 1, playerA.Hand.Count);
        Assert.Same(result.Card, playerA.Hand[^1]);
        Assert.Equal(2, effect.RunCount);
    }

    // ---------- ④ 池不足退化（Q&A-2 分段口径） ----------

    [Fact]
    public async Task Scenario1_Empty_Pool_Terminates_Without_Targeting_Construction_Or_Side_Effects()
    {
        var bridge = new MockTargeterBridge();
        var registry = C2Kit.CreateRegistry(3, C2Destination.Hand, poolFilter: _ => false);
        var match = C2Kit.CreateSceneMatch(bridge, registry, seed: 42);
        await match.Initialize();
        var playerA = match.Players[0];

        var (_, effect) = await C2Kit.PrepareEmitterAsync(match, playerA);
        using var recorder = new UpdateRecorder(match.Engine);
        recorder.Clear();
        var handBefore = playerA.Hand.Count;

        // 效果执行：取样池空（M=0）→ 链级终止（不抛断链、零副作用、结局可观察）
        await effect.CastAsync(match.Engine);

        Assert.Null(effect.LastError); // 不抛（运行路径未触发构造期 fail-fast 通道——组装护栏）
        Assert.True(effect.PoolEmptyTerminated); // 终止分支可观察
        Assert.Empty(effect.LastDrawnIds);
        Assert.Null(effect.LastTargetingStatus); // 不构造选择器、无 targeting 请求
        Assert.Null(effect.LastPlaceResult);
        Assert.Empty(bridge.Begins); // 桥接零调用
        Assert.Empty(bridge.CollectCalls);
        Assert.Equal(handBefore, playerA.Hand.Count);
        Assert.Equal(0, recorder.TotalCount()); // 零信号
    }

    [Fact]
    public async Task Scenario1_Partial_Pool_M_Less_Than_N_Proceeds_With_Actual_Draw_Count()
    {
        // 池＝2（＜N=3）——以实际取到的 2 张构造名单、照常进选择器（N 为期望张数上限、非硬性）
        var bridge = new MockTargeterBridge();
        var registry = C2Kit.CreateRegistry(
            3, C2Destination.Hand, poolFilter: id => id is C2Kit.PoolCard1Id or C2Kit.PoolCard2Id);
        var match = C2Kit.CreateSceneMatch(bridge, registry, seed: 42);
        await match.Initialize();
        var playerA = match.Players[0];

        var (_, effect) = await C2Kit.PrepareEmitterAsync(match, playerA);
        var handBefore = playerA.Hand.Count;

        var castTask = effect.CastAsync(match.Engine);
        var (description, responder) = await bridge.WaitForNextBeginAsync();

        Assert.Equal(2, effect.LastDrawnIds.Count);
        Assert.Equal(2, effect.LastPool.Count);
        var slot = Assert.Single(description.Slots);
        Assert.Equal(effect.LastDrawnIds.ToArray(), slot.CardListings!.Select(l => l.Id).ToArray()); // 请求登场名单＝取样结果

        var chosen = effect.LastDrawnIds[0];
        Assert.True(responder.Complete(
            description.RequestId, TargeterTestKit.IdentifierSelection(C2Kit.SlotName, chosen)));
        await castTask;

        Assert.Null(effect.LastError);
        Assert.Equal(TargetingStatus.Success, effect.LastTargetingStatus);
        Assert.Equal(CardPlaceStatus.Placed, effect.LastPlaceResult!.Status);
        Assert.Equal(handBefore + 1, playerA.Hand.Count);
    }

    // ---------- 链路驱动助手 ----------

    private sealed record ChainRun(
        IReadOnlyList<string> Drawn, string SelectedId, string GeneratedName, bool Placed);

    /// <summary>运行一次完整链（同序操作序列——供确定性/防退化复现断言）。</summary>
    private static async Task<ChainRun> RunChainAsync(int seed)
    {
        var bridge = new MockTargeterBridge();
        var registry = C2Kit.CreateRegistry(3, C2Destination.Hand);
        var match = C2Kit.CreateSceneMatch(bridge, registry, seed);
        await match.Initialize();
        var playerA = match.Players[0];
        var (_, effect) = await C2Kit.PrepareEmitterAsync(match, playerA);

        var castTask = effect.CastAsync(match.Engine);
        var (description, responder) = await bridge.WaitForNextBeginAsync();
        var chosen = C2Kit.PickFixed(effect.LastDrawnIds);
        Assert.True(responder.Complete(
            description.RequestId, TargeterTestKit.IdentifierSelection(C2Kit.SlotName, chosen)));
        await castTask;

        Assert.Null(effect.LastError);
        Assert.Equal(TargetingStatus.Success, effect.LastTargetingStatus);
        var result = effect.LastPlaceResult!;
        Assert.Equal(CardPlaceStatus.Placed, result.Status);
        return new ChainRun(
            effect.LastDrawnIds.ToArray(), effect.LastSelectedId!, result.Card!.Name, result.IsSuccess);
    }
}
