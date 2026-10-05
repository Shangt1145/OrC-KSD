using Orc.Cards;
using Orc.Core;
using Orc.Game;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Players;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// B1④ 补验（随机词条完整验证）：以 A 链真实词条体系（词条组件化＋9 词条）驱动的完整链——
/// 『效果施放 → 从词条池经对局随机服务取样 → 对单位授予（词条管理组件登记） → 词条行为实际生效』。
/// 池来源＝对战词条打标面（<see cref="KeywordRegistry.BattleKeywordUniverse"/>——读取引用＋校验、不另立副本）；
/// 主链池＝打标子集（选取理由＝行为断言目标全含〔重甲/烟幕〕、固定种子下取样结果可验证）。
/// 形态：PickN（不放回·场景 A）＋循环 PickOne（允许重复·场景 B）；边界：空池/单元素/池不足（场景 C/D/E）；
/// 确定性：同种子全链复现（取样序列＋授予结果＋行为断言）、异种子取样序列变化（稳定构造·场景 F）。
/// 行为消费路径：重甲＝真实战斗结算（反击方向减伤、预期值＝授予所用参值 1）；烟幕＝攻击候选筛选排除
/// （标记型·被消费的真实地点）。读取面覆盖全部已授予词条。
/// </summary>
public class RandomBattleKeywordChainTests
{
    private const string ChainHostId = "u_bk_chain";
    private const string ChainEffectId = "effect.rand.battlekw";

    // ---------- 装配辅助 ----------

    /// <summary>宿主单位定义（随机词条宿主：攻 3 / 防 5——行为断言数值基准）。</summary>
    private static CardDefinitionEntry ChainHostDefinition()
        => new(ChainHostId, new CardDefinition(
            "随机词条宿主", deployCost: 1, operateCost: 1, attack: 3, defense: 5,
            unitTypes: new[] { UnitType.Infantry }, faction: Faction.Germany, rarity: Rarity.Standard));

    /// <summary>创建完整链对局（宿主定义＋效果声明；bridge 供攻击交互驱动、可空）。</summary>
    private static Match CreateChainMatch(
        int seed, IReadOnlyList<string> pool, int count, bool usePickN, MockTargeterBridge? bridge = null)
    {
        var registry = new CardEffectRegistry();
        registry.Register(ChainEffectId, _ => new RandomKeywordGrantEffect(pool, count, usePickN));
        registry.Declare(ChainHostId, new[] { ChainEffectId });
        return CommandTestKit.CreateCommandMatch(
            bridge: bridge,
            seed: seed,
            effectRegistry: registry,
            extraDefinitions: new[] { ChainHostDefinition() });
    }

    /// <summary>链场景统一准备（宿主＋对照（无烟幕）＋敌方坦克——所有场景同序，保证同种子流一致性）。</summary>
    private static async Task<(UnitCard Host, UnitCard Control, UnitCard Enemy, RandomKeywordGrantEffect Effect)> ArrangeChainSceneAsync(
        Match match)
    {
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, ChainHostId, 1);
        var control = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 2);
        var enemy = await CommandTestKit.PrepareOnFrontAsync(match, playerB, CommandTestKit.TankId, 0);
        var effect = Assert.IsType<RandomKeywordGrantEffect>(Assert.Single(host.Effects));
        return (host, control, enemy, effect);
    }

    // ---------- 完整链运行（场景 A/F 共用）：施放 → 取样 → 授予 → 行为断言 ----------

    private sealed record FullChainRun(
        IReadOnlyList<string> Draw,
        IReadOnlyList<string> Granted,
        IReadOnlyList<bool> GrantResults,
        int? ArmorValue,
        int HostDefenseAfterAttack,
        int EnemyDefenseAfterAttack,
        bool EnemyCandidatesExcludeHost,
        bool EnemyCandidatesContainControl);

    /// <summary>
    /// 完整链运行（效果运行期闭环）：施放 → 取样（PickN·池＝打标子集〔重甲/烟幕〕·N=2）→ 对宿主真实授予
    /// → 读取全部已授予词条 → 行为断言（①重甲：宿主攻击敌方坦克，反击伤害经真实战斗结算减 1；
    /// ②烟幕：敌方回合检查敌方攻击候选——被烟幕排除）。
    /// </summary>
    private static async Task<FullChainRun> RunFullChainAsync(int seed)
    {
        var bridge = new MockTargeterBridge();
        var match = CreateChainMatch(
            seed, BattleKeywordChainKit.Pool(KeywordIds.Armor, KeywordIds.SmokeScreen),
            count: 2, usePickN: true, bridge);
        await match.Initialize();
        var (host, control, enemy, effect) = await ArrangeChainSceneAsync(match);

        await effect.CastAsync(match.Engine); // 效果运行期闭环（取样＋授予）

        var draw = effect.DrawLog.ToArray();
        var granted = BattleKeywordRules.GetBattleKeywords(host).ToArray();
        var armor = KeywordRules.GetKeywordValue(host, KeywordIds.Armor);

        // 行为①（重甲·真实战斗结算的反击方向）：宿主主动攻击敌方坦克——敌方反击 3-1（重甲 1）＝2
        // → 宿主防御 5-2=3（若减伤未生效则为 5-3=2，可区分）。
        CommandTestKit.Activate(host);
        bridge.CollectScript = CommandTestKit.AllRefsScript(match);
        var attack = await CommandTestKit.RunCommandAsync(match, bridge, host, enemy.Ref);
        Assert.Equal(CommandResultStatus.Success, attack.Status);
        var hostDefense = host.Modifiers.GetEffectiveValue(CardStatFields.Defense);
        var enemyDefense = enemy.Modifiers.GetEffectiveValue(CardStatFields.Defense);

        // 行为②（烟幕·攻击候选筛选的真实消费）：敌方回合检查敌方坦克的攻击候选——宿主被烟幕排除、
        // 无烟幕对照仍在候选（证明候选面本身工作正常）。
        await match.EndTurn(); // → T2（B）
        CommandTestKit.Activate(enemy);
        var availability = match.CommandManager.GetCommandAvailability(enemy);
        var excludesHost = !availability.Attack.Candidates.Contains(host.Ref);
        var containsControl = availability.Attack.Candidates.Contains(control.Ref);

        return new FullChainRun(
            draw, granted, effect.GrantResults.ToArray(), armor,
            hostDefense, enemyDefense, excludesHost, containsControl);
    }

    // ---------- 场景 A：PickN（不放回）——组合共存＋读取全部＋行为断言 ----------

    [Fact]
    public async Task Chain_PickN_Grant_Combination_Reads_All_And_Behaviors_Take_Effect()
    {
        var run = await RunFullChainAsync(seed: 42);

        // 取样（PickN·不放回）：N=2 取池内两枚——互异；固定种子 42 下的确定性输出（回归锚点；
        // 顺序 [烟幕,重甲] 与池序不同——同时证明非恒取首项）。
        Assert.Equal(2, run.Draw.Count);
        Assert.Equal(new[] { KeywordIds.SmokeScreen, KeywordIds.Armor }, run.Draw);
        Assert.Equal(new[] { true, true }, run.GrantResults);

        // 授予（真实词条体系）：两枚共存于同一单位；读取面覆盖全部已授予词条（打标全集登记序）。
        Assert.Equal(new[] { KeywordIds.SmokeScreen, KeywordIds.Armor }, run.Granted);
        Assert.Equal(1, run.ArmorValue); // 参值口径：参值词条随机授予＝默认参值 1（A2 工程约定）

        // 行为①（重甲）：敌方防御 5-3=2（攻击伤害正常）；宿主防御 5-(3-1)=3（反击伤害经既有结算路径减 1）。
        Assert.Equal(2, run.EnemyDefenseAfterAttack);
        Assert.Equal(3, run.HostDefenseAfterAttack);

        // 行为②（烟幕·被消费真实地点）：敌方攻击候选排除宿主、保留无烟幕对照。
        Assert.True(run.EnemyCandidatesExcludeHost);
        Assert.True(run.EnemyCandidatesContainControl);
    }

    // ---------- 场景 B：循环 PickOne（允许重复）——互异共存＋重复幂等 ----------

    [Fact]
    public async Task Chain_LoopPickOne_Combination_Coexists_And_Repeat_Is_Idempotent()
    {
        // 循环 PickOne（允许重复选中）：种子选取确保两次互异选中（「互异 N≥2 共存」·首选口径）＋
        // 第 3 次重复选中——重复处置按既有授予语义（幂等：false、无操作、登记/参值不重不丢）。
        var pool = BattleKeywordChainKit.Pool(KeywordIds.Armor, KeywordIds.SmokeScreen);
        var match = CreateChainMatch(seed: 3, pool, count: 3, usePickN: false);
        await match.Initialize();
        var (host, _, _, effect) = await ArrangeChainSceneAsync(match);

        await effect.CastAsync(match.Engine);

        Assert.Equal(new[] { KeywordIds.Armor, KeywordIds.SmokeScreen, KeywordIds.Armor }, effect.DrawLog); // [A, B, A]
        Assert.Equal(new[] { true, true, false }, effect.GrantResults); // 第 3 次重复＝幂等 false
        Assert.Equal(new[] { KeywordIds.SmokeScreen, KeywordIds.Armor }, BattleKeywordRules.GetBattleKeywords(host)); // 互异共存（读取全部）
        Assert.Equal(1, host.Keywords.GetValue(KeywordIds.Armor)); // 重复授予不覆盖参值（幂等——参值保持）
    }

    // ---------- 场景 C：池边界（正常完成型）——单元素池 ----------

    [Fact]
    public async Task Chain_SingleElement_Pool_Completes_Normally()
    {
        // 池边界（正常完成型）：单元素池（从打标面选取子集）——PickN(1) 必取该元素、授予、读取。
        // 说明：取样不足 N≥2 的场景（单元素池）以场景内实际词条全部验证为准（Q&A-1 之 1.2）。
        var pool = BattleKeywordChainKit.Pool(KeywordIds.Armor);
        var match = CreateChainMatch(seed: 42, pool, count: 1, usePickN: true);
        await match.Initialize();
        var (host, _, _, effect) = await ArrangeChainSceneAsync(match);

        await effect.CastAsync(match.Engine);

        Assert.Equal(new[] { KeywordIds.Armor }, effect.DrawLog);
        Assert.Equal(new[] { true }, effect.GrantResults);
        Assert.True(host.Keywords.Has(KeywordIds.Armor));
        Assert.Equal(1, host.Keywords.GetValue(KeywordIds.Armor));
        Assert.Equal(new[] { KeywordIds.Armor }, BattleKeywordRules.GetBattleKeywords(host));
        Assert.Null(effect.LastRejection);
    }

    // ---------- 场景 D：池边界（受控失败型）——空池 ----------

    [Fact]
    public async Task Chain_Empty_Pool_Is_Controlled_Failure_Not_Swallowed()
    {
        // 池边界（受控失败型）：空池（从打标面选取空子集）——不抛未受控中断（不抛断链）＋
        // 拒绝语义如实承载、不被静默吞噬（受控、行为明确、可断言——非异常上抛）；不授予、不重试。
        var pool = BattleKeywordChainKit.Pool();
        var match = CreateChainMatch(seed: 42, pool, count: 1, usePickN: false);
        await match.Initialize();
        var (host, _, _, effect) = await ArrangeChainSceneAsync(match);

        await effect.CastAsync(match.Engine); // 不抛（受控）

        Assert.NotNull(effect.LastRejection);
        Assert.Contains("空池", effect.LastRejection);
        Assert.Empty(effect.DrawLog);
        Assert.Empty(effect.GrantResults);
        Assert.Empty(BattleKeywordRules.GetBattleKeywords(host));

        // 原语层对照（B1 既有语义·引用断言）：空候选集＝明确拒绝（可断言）。
        var standalone = new MatchRandomService(seed: 42);
        Assert.Throws<InvalidOperationException>(() => standalone.PickOne(Array.Empty<string>()));
    }

    // ---------- 场景 E：池边界（受控失败型）——池不足（N＞池数） ----------

    [Fact]
    public async Task Chain_Under_Pool_Is_Controlled_Failure_Not_Swallowed()
    {
        // 池边界（受控失败型）：池不足（N＝2 ＞ 池数 1）——按原语既定语义（不放回取样不可满足＝明确拒绝）；
        // 链层受控承载（不抛断链、不静默、不授予、不重试）。
        var pool = BattleKeywordChainKit.Pool(KeywordIds.Armor);
        var match = CreateChainMatch(seed: 42, pool, count: 2, usePickN: true);
        await match.Initialize();
        var (host, _, _, effect) = await ArrangeChainSceneAsync(match);

        await effect.CastAsync(match.Engine); // 不抛（受控）

        Assert.NotNull(effect.LastRejection);
        Assert.Contains("池不足", effect.LastRejection);
        Assert.Empty(effect.DrawLog);
        Assert.Empty(effect.GrantResults);
        Assert.Empty(BattleKeywordRules.GetBattleKeywords(host));

        // 原语层对照（B1 既有语义·引用断言）：n ＞ 候选数＝明确拒绝（不放回不可满足）。
        var standalone = new MatchRandomService(seed: 42);
        Assert.Throws<ArgumentOutOfRangeException>(() => standalone.PickN(new[] { KeywordIds.Armor }, 2));
    }

    // ---------- 场景 F：确定性——同种子全链复现 / 异种子取样序列变化 ----------

    [Fact]
    public async Task Chain_Same_Seed_Replays_Identical_Full_Chain()
    {
        // 同种子全链复现：取样序列 ＋ 授予结果 ＋ 行为断言结果——两次独立运行逐项一致。
        var first = await RunFullChainAsync(seed: 42);
        var second = await RunFullChainAsync(seed: 42);

        Assert.Equal(first.Draw, second.Draw);
        Assert.Equal(first.Granted, second.Granted);
        Assert.Equal(first.GrantResults, second.GrantResults);
        Assert.Equal(first.ArmorValue, second.ArmorValue);
        Assert.Equal(first.HostDefenseAfterAttack, second.HostDefenseAfterAttack);
        Assert.Equal(first.EnemyDefenseAfterAttack, second.EnemyDefenseAfterAttack);
        Assert.Equal(first.EnemyCandidatesExcludeHost, second.EnemyCandidatesExcludeHost);
        Assert.Equal(first.EnemyCandidatesContainControl, second.EnemyCandidatesContainControl);
    }

    [Fact]
    public async Task Chain_Changing_Seed_Changes_Draw_Sequence_Stably()
    {
        // 异种子：至少取样序列变化——稳定构造（选定种子对使差异确定，避免概率性 flaky）。
        var baseline = await RunFullChainAsync(seed: 42);
        var alternate = await RunFullChainAsync(seed: 43);

        Assert.NotEqual(baseline.Draw, alternate.Draw);
    }

    // ---------- 「取样路径真实」独立证据（链形态）——跨种子覆盖池内元素 ----------

    [Fact]
    public async Task Chain_Draw_Path_Is_Real_Covers_Pool_Across_Seeds()
    {
        // 「取样路径真实」独立证据：跨种子取样结果覆盖池内两枚（非固定映射、非恒取首项）——
        // 取样结果均为池元素（真实来自服务对池的取样）；种子集为稳定构造（实测锚点）。
        var pool = BattleKeywordChainKit.Pool(KeywordIds.Armor, KeywordIds.SmokeScreen);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in new[] { 42, 43 })
        {
            var match = CreateChainMatch(seed, pool, count: 1, usePickN: true);
            await match.Initialize();
            var (_, _, _, effect) = await ArrangeChainSceneAsync(match);
            await effect.CastAsync(match.Engine);

            var draw = Assert.Single(effect.DrawLog); // 单取样：恰一枚
            Assert.Contains(draw, pool); // 取样结果 ∈ 池
            covered.Add(draw);
        }

        Assert.Equal(2, covered.Count); // 跨种子覆盖池内两枚
    }

}

/// <summary>
/// 完整链池构建（打标面子集——「不另立清单副本」的落地形态）：
/// 元素须取自/校验于对战词条打标面（<see cref="KeywordRegistry.BattleKeywordUniverse"/>）。
/// 子集选取理由：①主链行为断言目标全含（重甲＝真实战斗结算消费、烟幕＝攻击候选筛选消费——均为
/// 打标面行为面完整词条，不含元规则/未接通项）；②固定种子下取样结果可验证（小池＋N 固定值）。
/// 边界构造：空集/单元素集＝从权威面选取的退化子集（合法边界输入——取样原语接收池参数）。
/// </summary>
internal static class BattleKeywordChainKit
{
    /// <summary>构建池（子集元素须取自打标面；校验而非副本；空集/单元素为边界构造）。</summary>
    public static IReadOnlyList<string> Pool(params string[] selected)
    {
        var universe = KeywordRegistry.BattleKeywordUniverse;
        foreach (var keyword in selected)
        {
            Assert.Contains(keyword, universe); // 子集元素须取自打标面（读取引用＋校验——不复制清单）
        }

        Assert.Equal(selected.Length, selected.Distinct().Count()); // 池为集合语义：无重复项
        return selected.ToArray();
    }
}
