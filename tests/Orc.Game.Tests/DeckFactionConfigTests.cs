using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// S10 功能点①验收（主国/盟国——卡组构筑配置）：
/// ①1 配置载入与可读面（提供合法配置 → 初始化后可读；未提供 → null、不抛错）；
/// ①2 校验面（4 类非法值＋成对缺失 → 创建期 fail-fast）；
/// ①3「主国空军」筛选消费（国籍==主国 AND 类型==空军——测试内组合筛选，不新增机制级筛选 API；
/// 「空军」维度定型＝开放 tag〔既有先例：CardTagDataTests「日本精英空军」样本〕）。
/// </summary>
public class DeckFactionConfigTests
{
    // ---------- 筛选样本（「主国空军」组合筛选） ----------

    private const string GerAirId = "s10_ger_air";
    private const string JpAirId = "s10_jp_air";
    private const string GerGroundId = "s10_ger_ground";

    /// <summary>筛选样本定义集：德国空军 / 日本空军 / 德国陆军（「空军」＝开放 tag；单位类型取合理值）。</summary>
    private static IReadOnlyList<CardDefinitionEntry> CreateFilterDefinitions() => new[]
    {
        new CardDefinitionEntry(GerAirId, new CardDefinition("德国空军", 1, 1, 3, 2,
            unitTypes: new[] { UnitType.Fighter }, faction: Faction.Germany, rarity: Rarity.Standard, tags: new[] { "空军" })),
        new CardDefinitionEntry(JpAirId, new CardDefinition("日本空军", 1, 1, 3, 2,
            unitTypes: new[] { UnitType.Fighter }, faction: Faction.Japan, rarity: Rarity.Standard, tags: new[] { "空军" })),
        new CardDefinitionEntry(GerGroundId, new CardDefinition("德国陆军", 1, 1, 2, 4,
            unitTypes: new[] { UnitType.Infantry }, faction: Faction.Germany, rarity: Rarity.Standard)),
    };

    /// <summary>创建对局（可选双方构筑配置——「调用方提供数据、Match 负责装配」）。</summary>
    private static Match CreateMatch(
        PlayerDeckConfiguration? configA = null,
        PlayerDeckConfiguration? configB = null,
        IEnumerable<CardDefinitionEntry>? extraDefinitions = null)
        => new(
            GameTestData.CreateDeck(),
            GameTestData.CreateDeck(),
            GameTestData.CreateDefinitions().Concat(extraDefinitions ?? Enumerable.Empty<CardDefinitionEntry>()),
            seed: 42,
            deckConfigForPlayerA: configA,
            deckConfigForPlayerB: configB);

    // ---------- ①1 配置载入与可读面 ----------

    [Fact]
    public async Task Deck_Configuration_Loads_And_Is_Readable_After_Initialize()
    {
        var match = CreateMatch(
            new PlayerDeckConfiguration(Faction.Germany, Faction.Finland),
            new PlayerDeckConfiguration(Faction.Japan, Faction.Italy));
        await match.Initialize();

        // 提供合法配置 → 初始化后可读（两属性值正确；按玩家分别提供）
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        Assert.Equal(Faction.Germany, playerA.MainFaction);
        Assert.Equal(Faction.Finland, playerA.AllyFaction);
        Assert.Equal(Faction.Japan, playerB.MainFaction);
        Assert.Equal(Faction.Italy, playerB.AllyFaction);
    }

    [Fact]
    public async Task Deck_Configuration_Default_Is_Null_Without_Throwing()
    {
        // 未提供（既有构造形态——全部既有测试如此）：读面 null、不抛错、既有构造不受影响
        var match = CreateMatch();
        await match.Initialize();

        Assert.Null(match.Players[0].MainFaction);
        Assert.Null(match.Players[0].AllyFaction);
        Assert.Null(match.Players[1].MainFaction);
        Assert.Null(match.Players[1].AllyFaction);
    }

    [Fact]
    public async Task Deck_Configuration_Ally_From_Remaining_Majors_Is_Valid()
    {
        // 盟国值域＝余下 4 个主国＋5 个盟国（9 值）：抽验「盟国＝另一主国」（主国=Germany、盟国=Japan）
        var match = CreateMatch(new PlayerDeckConfiguration(Faction.Germany, Faction.Japan));
        await match.Initialize();

        Assert.Equal(Faction.Japan, match.Players[0].AllyFaction);
    }

    // ---------- ①2 校验面（4 类非法值＋成对缺失——创建期 fail-fast） ----------

    [Fact]
    public void Deck_Configuration_Undefined_Enum_Values_Are_Rejected_At_Creation()
    {
        // 未定义枚举值（主国 / 盟国两向）
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMatch(
            new PlayerDeckConfiguration((Faction)99, Faction.Finland)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMatch(
            new PlayerDeckConfiguration(Faction.Germany, (Faction)99)));

        // 对局保持不可达：异常在创建期抛出（对局未创建＝无残留）
    }

    [Fact]
    public void Deck_Configuration_Main_Faction_Must_Be_One_Of_The_Major_Five()
    {
        // 主国非五主国（France/Poland/…/Neutral 均非法）
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMatch(
            new PlayerDeckConfiguration(Faction.France, Faction.Finland)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMatch(
            new PlayerDeckConfiguration(Faction.Neutral, Faction.Finland)));
    }

    [Fact]
    public void Deck_Configuration_Ally_Must_Differ_From_Main_And_Not_Neutral()
    {
        // 盟国==主国
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMatch(
            new PlayerDeckConfiguration(Faction.Germany, Faction.Germany)));
        // 盟国==Neutral（Neutral 不得作为盟国）
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateMatch(
            new PlayerDeckConfiguration(Faction.Germany, Faction.Neutral)));
    }

    [Fact]
    public void Deck_Configuration_Must_Be_Paired()
    {
        // 成对口径：只提供其一＝明确拒绝（缺主国 / 缺盟国）
        Assert.Throws<ArgumentException>(() => CreateMatch(
            new PlayerDeckConfiguration(null, Faction.Finland)));
        Assert.Throws<ArgumentException>(() => CreateMatch(
            new PlayerDeckConfiguration(Faction.Germany, null)));

        // 对照：两值齐备＝创建成功（未初始化亦可——校验通过即构造完成）
        var match = CreateMatch(new PlayerDeckConfiguration(Faction.Germany, Faction.Finland));
        Assert.Equal(MatchState.Preparing, match.State);
    }

    // ---------- ①3「主国空军」筛选（测试内组合筛选） ----------

    [Fact]
    public async Task Main_Faction_Card_Filter_Combines_Nation_And_Air_Type()
    {
        var match = CreateMatch(
            new PlayerDeckConfiguration(Faction.Japan, Faction.Germany),
            new PlayerDeckConfiguration(Faction.Germany, Faction.Finland),
            CreateFilterDefinitions());
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];

        // 加载筛选样本（测试内卡集合——实例化＋加载）
        var samples = new List<CardBase>();
        foreach (var id in new[] { GerAirId, JpAirId, GerGroundId })
        {
            var card = match.CardLibrary.Instantiate(id);
            await card.LoadAsync(playerA);
            samples.Add(card);
        }

        // 「主国空军」＝ 国籍==主国 AND 类型==空军（组合筛选——跨组件读取：合并组件〔国籍〕＋标签组件〔空军 tag〕）
        static List<CardBase> FilterMainFactionAir(IEnumerable<CardBase> cards, Faction? mainFaction)
            => cards.Where(card =>
            {
                var factionCost = card.GetData<FactionCostData>();
                var tagData = card.GetData<TagData>();
                return factionCost.Faction == mainFaction && tagData.ContainsTag("空军");
            }).ToList();

        // 玩家A（主国=Japan）：命中「日本空军」恰一枚
        var resultA = FilterMainFactionAir(samples, playerA.MainFaction);
        Assert.Single(resultA);
        Assert.Equal("日本空军", resultA[0].Name);

        // 玩家B（主国=Germany）：同集合、不同主国读面 → 命中「德国空军」恰一枚（非空军/非主国不命中）
        var resultB = FilterMainFactionAir(samples, playerB.MainFaction);
        Assert.Single(resultB);
        Assert.Equal("德国空军", resultB[0].Name);

        // 未配置主国（null）＝不匹配（消费端降级——不抛错）
        Assert.Empty(FilterMainFactionAir(samples, mainFaction: null));
    }
}
