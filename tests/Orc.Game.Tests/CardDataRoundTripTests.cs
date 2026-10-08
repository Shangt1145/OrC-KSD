using System.Text.Json;
using Orc.Cards;
using Orc.Game.Cards;
using Orc.Game.Cards.Data;
using Orc.Game.Cards.Data.Components;
using Xunit;
using Xunit.Abstractions;

namespace Orc.Game.Tests;

/// <summary>
/// 卡牌数据体**往返**（X1·批 1；读→写→读）：逐组件 round-trip → 整卡样本（dug_in / 102_grenadier /
/// 自建内联 effects 两种形态）→ 目录 round-trip（含效果样本）→ **全量 1646 张**官方语料语义等值。
/// 口径：语义等值＝读面逐字段＋组件集逐字段（保真路径列表逐项一致）；幂等＝J1==J2 字节级（全量/目录级硬性）；
/// "四空"＝Serialize 警告空、Save 失败空、再 Load Failures 空＋Warnings 空。outputs 缺失＝跳过（可辨识）。
/// </summary>
public class CardDataRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public CardDataRoundTripTests(ITestOutputHelper output) => _output = output;

    // ─────────────────────────────────────────────────────────────────────────
    // 样本（内嵌自含——恒定可跑、零环境依赖；真实文件的全量覆盖归全量测试）。
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>官方样本 dug_in（指令卡：order 类别、无 battleStats、无 keywords）。</summary>
    private const string DugInJson = """
    {
      "schemaVersion": 1,
      "id": "dug_in",
      "name": "深挖",
      "components": [
        {
          "component": "typeCategory",
          "type": [
            "order"
          ]
        },
        {
          "component": "factionCost",
          "faction": "Japan",
          "kredits": 0
        },
        {
          "component": "tagData",
          "rarity": "Standard"
        }
      ]
    }
    """;

    /// <summary>官方样本 102_grenadier（含 BecomesVeteran 端口、OnlySpawnable 留痕、shock）。</summary>
    private const string GrenadierJson = """
    {
      "schemaVersion": 1,
      "id": "102_grenadier",
      "name": "第 102 掷弹兵团",
      "components": [
        {
          "component": "typeCategory",
          "type": [
            "infantry"
          ]
        },
        {
          "component": "factionCost",
          "faction": "Germany",
          "kredits": 1
        },
        {
          "component": "battleStats",
          "operationCost": 1,
          "attack": 1,
          "defense": 3
        },
        {
          "component": "tagData",
          "rarity": "Standard"
        },
        {
          "component": "keywords",
          "attributes": [
            "BecomesVeteran:102_grenadier_vet",
            "OnlySpawnable",
            "shock"
          ]
        }
      ]
    }
    """;

    /// <summary>逐组件综合样本（六组件齐全：类别/单位组合、参值、老兵端口、未映射留痕、tags）。</summary>
    private const string ComponentLevelJson = """
    {
      "schemaVersion": 1,
      "id": "component-level",
      "name": "组件级",
      "components": [
        { "component": "typeCategory", "type": ["infantry", "tank"] },
        { "component": "factionCost", "faction": "Soviet", "kredits": 3 },
        { "component": "battleStats", "operationCost": 2, "attack": 4, "defense": 5 },
        { "component": "tagData", "rarity": "Limited", "tags": ["ace", "elite-guard"] },
        { "component": "keywords", "attributes": ["blitz", "fury", "heavyArmor2", "BecomesVeteran:vet-1", "OnlySpawnable"] },
        { "component": "effects", "prefabs": [], "inline": [] }
      ]
    }
    """;

    /// <summary>内联 effects·对象态样本（ref ＋ 两个 inline，声明序）。</summary>
    private const string InlineObjectCardJson = """
    {
      "schemaVersion": 1,
      "id": "inline-object-card",
      "name": "内联对象态",
      "components": [
        { "component": "factionCost", "faction": "Britain", "kredits": 2 },
        { "component": "tagData", "rarity": "Standard" },
        { "component": "effects", "prefabs": ["ref-x"], "inline": [
          { "schemaVersion": 2, "root": { "id": "inline-obj-1", "mainTrigger": { "id": "t1", "stableKey": "内联·一", "kind": "passive", "hooks": ["unit.deployed"], "events": [ { "id": "e1", "entry": "HandleAsync", "assemblyKey": "inline-obj-1" } ] } } },
          { "schemaVersion": 2, "root": { "id": "inline-obj-2", "mainTrigger": { "id": "t2", "stableKey": "内联·二", "kind": "passive", "hooks": ["unit.deployed"], "events": [ { "id": "e2", "entry": "HandleAsync", "assemblyKey": "inline-obj-2" } ] } } }
        ] }
      ]
    }
    """;

    // ─────────────────────────────────────────────────────────────────────────
    // 逐组件 round-trip（数据体路径）。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Component_Level_Round_Trip_Covers_Six_Components_With_Fidelity()
    {
        var body1 = CardDataJson.Deserialize(ComponentLevelJson, out var readWarnings);
        Assert.Empty(readWarnings);
        var entry1 = new CardDefinitionEntry(body1.Id, new CardDefinition(body1.Name, body1.Components));

        var j1 = CardDataJson.Serialize(entry1, out var warnings);
        Assert.Empty(warnings);

        var body2 = CardDataJson.Deserialize(j1, out var roundWarnings);
        Assert.Empty(roundWarnings);
        var entry2 = new CardDefinitionEntry(body2.Id, new CardDefinition(body2.Name, body2.Components));

        // 逐组件抽样核对（语义保真）。
        Assert.True(body2.TryGetComponent<KeywordsDefinition>(out var keywords));
        Assert.Equal(
            new[] { "blitz", "fury", "heavyArmor2", "BecomesVeteran:vet-1", "OnlySpawnable" },
            keywords.Attributes);
        Assert.Equal("vet-1", keywords.BecomesVeteran);
        Assert.Equal(new[] { "OnlySpawnable" }, keywords.UnmappedAttributes);
        Assert.Equal(
            new[] { KeywordIds.Blitz, KeywordIds.Fury, KeywordIds.Armor },
            entry2.Definition.Keywords.Select(item => item.Id));
        Assert.Equal(2, entry2.Definition.Keywords[2].Value);

        Assert.True(body2.TryGetComponent<TypeCategoryDefinition>(out var typeCategory));
        Assert.Equal(CardCategory.Unit, typeCategory.Category);
        Assert.Equal(new[] { UnitType.Infantry, UnitType.Tank }, typeCategory.UnitTypes);

        Assert.True(body2.TryGetComponent<TagDataDefinition>(out var tagData));
        Assert.Equal(new[] { "ace", "elite-guard" }, tagData.Tags);

        // 全量语义等值＋幂等。
        AssertSemanticallyEqual("component-level", entry1.Definition, entry2.Definition);
        var j2 = CardDataJson.Serialize(entry2, out _);
        Assert.Equal(j1, j2);
    }

    [Fact]
    public void Code_Constructed_Card_Synthesis_Round_Trips_With_Member_Equivalence()
    {
        var definition = new CardDefinition(
            "合成整卡",
            2, 1, 3, 4,
            keywords: [new KeywordDeclaration(KeywordIds.Armor, 2), new KeywordDeclaration(KeywordIds.Covert)],
            unitTypes: [UnitType.Artillery],
            faction: Faction.USA,
            rarity: Rarity.Special,
            tags: ["ace"],
            veteranOf: "base-x");
        var entry = new CardDefinitionEntry("synthesis-card", definition);

        var j1 = CardDataJson.Serialize(entry, out var warnings);
        Assert.Empty(warnings);

        var body2 = CardDataJson.Deserialize(j1, out _);
        var readBack = new CardDefinition(body2.Name, body2.Components);

        // 合成路径＝成员级等值（顺序不敏感；默认风格端口在前、关键词中、留痕在后）。
        Assert.Equal("base-x", readBack.VeteranOf);
        Assert.Equal(
            new[] { KeywordIds.Veteran, KeywordIds.Armor, KeywordIds.Covert }.OrderBy(item => item, StringComparer.Ordinal),
            readBack.Keywords.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Equal(2, readBack.Keywords.Single(item => item.Id == KeywordIds.Armor).Value);

        // 其余读面等值。
        AssertScalar("synthesis-card", "Faction", Faction.USA, readBack.Faction);
        AssertScalar("synthesis-card", "DeployCost", 2, readBack.DeployCost);
        AssertScalar("synthesis-card", "OperateCost", 1, readBack.OperateCost);
        AssertScalar("synthesis-card", "Attack", 3, readBack.Attack);
        AssertScalar("synthesis-card", "Defense", 4, readBack.Defense);
        AssertScalar("synthesis-card", "Rarity", Rarity.Special, readBack.Rarity);
        AssertStringList("synthesis-card", "Tags", new[] { "ace" }, readBack.Tags);
        AssertUnitTypeList("synthesis-card", "UnitTypes", new[] { UnitType.Artillery }, readBack.UnitTypes);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 整卡样本（dug_in / 102_grenadier）。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sample_DugIn_Round_Trips_With_Semantic_Equivalence_And_Idempotence()
    {
        var entry1 = LoadEntry(DugInJson);
        Assert.Equal(CardCategory.Command, entry1.Definition.Category);

        var j1 = CardDataJson.Serialize(entry1, out var warnings);
        Assert.Empty(warnings);

        var entry2 = LoadEntry(j1);

        AssertSemanticallyEqual("dug_in", entry1.Definition, entry2.Definition);
        var j2 = CardDataJson.Serialize(entry2, out _);
        Assert.Equal(j1, j2);
    }

    [Fact]
    public void Sample_Grenadier_Round_Trips_With_Semantic_Equivalence_And_Idempotence()
    {
        var entry1 = LoadEntry(GrenadierJson);
        Assert.Equal("102_grenadier_vet", entry1.Definition.BecomesVeteran);
        Assert.Equal(new[] { KeywordIds.Shock }, entry1.Definition.Keywords.Select(item => item.Id));
        Assert.Equal(new[] { "OnlySpawnable" }, entry1.Definition.UnmappedAttributes);

        var j1 = CardDataJson.Serialize(entry1, out var warnings);
        Assert.Empty(warnings);

        var entry2 = LoadEntry(j1);

        AssertSemanticallyEqual("102_grenadier", entry1.Definition, entry2.Definition);
        var j2 = CardDataJson.Serialize(entry2, out _);
        Assert.Equal(j1, j2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 内联 effects（对象态 / 字符串态——读宽写窄）。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Inline_Effects_Object_State_Round_Trips_With_Grouping_And_Order()
    {
        var entry1 = LoadEntry(InlineObjectCardJson);

        var j1 = CardDataJson.Serialize(entry1, out var warnings);
        Assert.Empty(warnings);

        // 写出：prefabs 引用按序、inline 对象态按序（两区分组）。
        using (var document = JsonDocument.Parse(j1))
        {
            var effects = FindComponent(document, "effects");
            Assert.Equal(
                new[] { "ref-x" },
                effects.GetProperty("prefabs").EnumerateArray().Select(item => item.GetString()));
            var inline = effects.GetProperty("inline").EnumerateArray().ToArray();
            Assert.Equal(2, inline.Length);
            Assert.Equal(JsonValueKind.Object, inline[0].ValueKind);
            Assert.Equal(JsonValueKind.Object, inline[1].ValueKind);
            Assert.Equal("inline-obj-1", inline[0].GetProperty("root").GetProperty("id").GetString());
            Assert.Equal("inline-obj-2", inline[1].GetProperty("root").GetProperty("id").GetString());
        }

        // 读回：分组/元素/顺序保持＋幂等。
        var body2 = CardDataJson.Deserialize(j1, out var roundWarnings);
        Assert.Empty(roundWarnings);
        var entry2 = new CardDefinitionEntry(body2.Id, new CardDefinition(body2.Name, body2.Components));
        Assert.True(body2.TryGetComponent<EffectsDefinition>(out var effects2));
        Assert.Equal(new[] { "ref-x" }, effects2.Prefabs);
        Assert.Equal(new[] { "inline-obj-1", "inline-obj-2" }, effects2.Inline.Select(item => item.Root.Id));

        AssertSemanticallyEqual("inline-object-card", entry1.Definition, entry2.Definition);
        var j2 = CardDataJson.Serialize(entry2, out _);
        Assert.Equal(j1, j2);
    }

    [Fact]
    public void Inline_Effects_String_State_Reads_Wide_And_Writes_Narrow_As_Object_State()
    {
        var json = BuildInlineStringCardJson();

        var body1 = CardDataJson.Deserialize(json, out var readWarnings);
        Assert.Empty(readWarnings);
        var entry1 = new CardDefinitionEntry(body1.Id, new CardDefinition(body1.Name, body1.Components));
        Assert.True(body1.TryGetComponent<EffectsDefinition>(out var effects1));
        Assert.Single(effects1.Inline);
        Assert.Equal("inline-str-1", effects1.Inline[0].Root.Id);

        var j1 = CardDataJson.Serialize(entry1, out var warnings);
        Assert.Empty(warnings);

        // 读宽写窄：字符串态读入 → 写出必为对象态。
        using (var document = JsonDocument.Parse(j1))
        {
            var inline = FindComponent(document, "effects").GetProperty("inline").EnumerateArray().ToArray();
            Assert.Single(inline);
            Assert.Equal(JsonValueKind.Object, inline[0].ValueKind);
        }

        // 再读等值。
        var body2 = CardDataJson.Deserialize(j1, out _);
        var entry2 = new CardDefinitionEntry(body2.Id, new CardDefinition(body2.Name, body2.Components));
        Assert.True(body2.TryGetComponent<EffectsDefinition>(out var effects2));
        Assert.Equal("inline-str-1", effects2.Inline[0].Root.Id);
        AssertSemanticallyEqual("inline-string-card", entry1.Definition, entry2.Definition);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 目录 round-trip（含效果样本）。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Directory_Round_Trip_With_Inline_Effects_Preserves_Semantics()
    {
        var sourceDirectory = NewTempDir();
        var targetDirectory = NewTempDir();
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "alpha.card.json"),
                """
                {"schemaVersion":1,"id":"alpha","name":"阿尔法","components":[
                  {"component":"typeCategory","type":["infantry"]},
                  {"component":"factionCost","faction":"Italy","kredits":1},
                  {"component":"tagData","rarity":"Standard"}
                ]}
                """);
            File.WriteAllText(Path.Combine(sourceDirectory, "inline-object-card.card.json"), InlineObjectCardJson);

            var loaded1 = CardDataLoader.LoadDirectory(sourceDirectory);
            Assert.Empty(loaded1.Failures);
            Assert.Empty(loaded1.Warnings);
            Assert.Equal(2, loaded1.Definitions.Count);

            var j1 = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in loaded1.Definitions)
            {
                var json = CardDataJson.Serialize(entry, out var warnings);
                Assert.True(warnings.Count == 0, $"卡 '{entry.Id}' 写出警告非空：{string.Join(" | ", warnings)}");
                j1[entry.Id] = json;
            }

            var save = CardDataWriter.SaveDirectory(targetDirectory, loaded1.Definitions);
            Assert.Equal(2, save.Succeeded.Count);
            Assert.Empty(save.Failures);
            Assert.Empty(save.Warnings);

            var loaded2 = CardDataLoader.LoadDirectory(targetDirectory);
            Assert.Empty(loaded2.Failures);
            Assert.Empty(loaded2.Warnings);
            Assert.Equal(2, loaded2.Definitions.Count);

            // Definitions：逐卡语义等值（先断言卡数相等——无丢失/新增）。
            var byId = loaded2.Definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var entry in loaded1.Definitions)
            {
                Assert.True(byId.TryGetValue(entry.Id, out var actual), $"再读结果缺少卡 '{entry.Id}'。");
                AssertSemanticallyEqual(entry.Id, entry.Definition, actual!.Definition);
            }

            // Prefabs：逐项等值（保序）。
            Assert.Equal(
                loaded1.Prefabs.Select(item => PrefabJson.Serialize(item)),
                loaded2.Prefabs.Select(item => PrefabJson.Serialize(item)));
            Assert.Equal(new[] { "inline-obj-1", "inline-obj-2" }, loaded2.Prefabs.Select(item => item.Root.Id));

            // EffectDeclarations：逐卡等值（id 清单与顺序保持）。
            var declarations1 = loaded1.EffectDeclarations.ToDictionary(
                item => item.CardId, item => item.PrefabIds, StringComparer.Ordinal);
            var declarations2 = loaded2.EffectDeclarations.ToDictionary(
                item => item.CardId, item => item.PrefabIds, StringComparer.Ordinal);
            Assert.Equal(
                declarations1.Keys.OrderBy(key => key, StringComparer.Ordinal),
                declarations2.Keys.OrderBy(key => key, StringComparer.Ordinal));
            foreach (var (cardId, ids) in declarations1)
            {
                Assert.True(declarations2.TryGetValue(cardId, out var ids2), $"再读结果缺少效果声明 '{cardId}'。");
                Assert.Equal(ids, ids2);
            }

            Assert.Equal(new[] { "ref-x", "inline-obj-1", "inline-obj-2" }, declarations2["inline-object-card"]);

            // 幂等：J1 == J2（字节级）。
            foreach (var entry in loaded2.Definitions)
            {
                var j2 = CardDataJson.Serialize(entry, out var warnings2);
                Assert.True(warnings2.Count == 0, $"卡 '{entry.Id}' 再写出警告非空：{string.Join(" | ", warnings2)}");
                Assert.True(
                    string.Equals(j1[entry.Id], j2, StringComparison.Ordinal),
                    $"卡 '{entry.Id}'：J1 != J2（幂等断言失败）。");
            }
        }
        finally
        {
            DeleteTempDir(sourceDirectory);
            DeleteTempDir(targetDirectory);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 全量 1646 张（outputs/kards-cards）——读→写→读语义等值。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Official_Corpus_Full_Round_Trip_Is_Semantically_Equivalent()
    {
        var officialDirectory = Path.Combine(FindRepoRoot(), "outputs", "kards-cards");
        if (!Directory.Exists(officialDirectory))
        {
            _output.WriteLine("官方语料目录缺失（outputs/kards-cards）——跳过全量往返测试。");
            return;
        }

        // 首轮装载：无枯文件/隔离。
        var loaded1 = CardDataLoader.LoadDirectory(officialDirectory);
        Assert.Empty(loaded1.Failures);
        Assert.Empty(loaded1.Warnings);
        Assert.Equal(1646, loaded1.Definitions.Count);
        Assert.Empty(loaded1.Prefabs);
        Assert.Empty(loaded1.EffectDeclarations);

        // 逐卡 Serialize（警告须空——硬指标）。
        var j1 = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in loaded1.Definitions)
        {
            var json = CardDataJson.Serialize(entry, out var warnings);
            Assert.True(warnings.Count == 0, $"卡 '{entry.Id}' 写方向警告非空：{string.Join(" | ", warnings)}");
            j1[entry.Id] = json;
        }

        var targetDirectory = NewTempDir();
        try
        {
            // 落盘：失败集空（成功数＝输入卡数）、警告空。
            var save = CardDataWriter.SaveDirectory(targetDirectory, loaded1.Definitions);
            Assert.Equal(loaded1.Definitions.Count, save.Succeeded.Count);
            Assert.Empty(save.Failures);
            Assert.Empty(save.Warnings);

            // 再装载：Failures 空＋Warnings 空、卡数相等（无丢失/新增）。
            var loaded2 = CardDataLoader.LoadDirectory(targetDirectory);
            Assert.Empty(loaded2.Failures);
            Assert.Empty(loaded2.Warnings);
            Assert.Equal(loaded1.Definitions.Count, loaded2.Definitions.Count);

            var byId = new Dictionary<string, CardDefinitionEntry>(StringComparer.Ordinal);
            foreach (var entry in loaded2.Definitions)
            {
                Assert.True(byId.TryAdd(entry.Id, entry), $"再读结果含重复卡 id '{entry.Id}'。");
            }

            // 逐卡配对语义比较（读面逐字段＋组件集逐字段；保真路径列表逐项一致）＋幂等（J1==J2）。
            foreach (var entry in loaded1.Definitions)
            {
                Assert.True(byId.TryGetValue(entry.Id, out var actual), $"再读结果缺少卡 '{entry.Id}'。");
                AssertSemanticallyEqual(entry.Id, entry.Definition, actual!.Definition);

                var j2 = CardDataJson.Serialize(actual, out var warnings2);
                Assert.True(warnings2.Count == 0, $"卡 '{entry.Id}' 再写出警告非空：{string.Join(" | ", warnings2)}");
                Assert.True(
                    string.Equals(j1[entry.Id], j2, StringComparison.Ordinal),
                    $"卡 '{entry.Id}'：J1 != J2（幂等断言失败）。");
            }
        }
        finally
        {
            DeleteTempDir(targetDirectory);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 语义比较（权威口径：读面逐字段＋组件集逐字段；失败消息含卡 id 与差异字段）。
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssertSemanticallyEqual(string cardId, CardDefinition expected, CardDefinition actual)
    {
        AssertScalar(cardId, "Faction", expected.Faction, actual.Faction);
        AssertScalar(cardId, "DeployCost", expected.DeployCost, actual.DeployCost);
        AssertScalar(cardId, "OperateCost", expected.OperateCost, actual.OperateCost);
        AssertScalar(cardId, "Attack", expected.Attack, actual.Attack);
        AssertScalar(cardId, "Defense", expected.Defense, actual.Defense);
        AssertScalar(cardId, "Rarity", expected.Rarity, actual.Rarity);
        AssertStringList(cardId, "Tags", expected.Tags, actual.Tags);
        AssertScalar(cardId, "Category", expected.Category, actual.Category);
        AssertUnitTypeList(cardId, "UnitTypes", expected.UnitTypes, actual.UnitTypes);
        AssertKeywordList(cardId, "Keywords", expected.Keywords, actual.Keywords);
        AssertStringList(cardId, "Attributes", expected.Attributes, actual.Attributes);
        AssertStringList(cardId, "UnmappedAttributes", expected.UnmappedAttributes, actual.UnmappedAttributes);
        AssertScalar(cardId, "BecomesVeteran", expected.BecomesVeteran, actual.BecomesVeteran);
        AssertScalar(cardId, "VeteranOf", expected.VeteranOf, actual.VeteranOf);

        AssertScalar(cardId, "ComponentCount", expected.Components.Count, actual.Components.Count);

        AssertComponent<TypeCategoryDefinition>(cardId, expected, actual, static (id, e, a) =>
        {
            AssertScalar(id, "typeCategory.category", e.Category, a.Category);
            AssertUnitTypeList(id, "typeCategory.unitTypes", e.UnitTypes, a.UnitTypes);
        });
        AssertComponent<FactionCostDefinition>(cardId, expected, actual, static (id, e, a) =>
        {
            AssertScalar(id, "factionCost.faction", e.Faction, a.Faction);
            AssertScalar(id, "factionCost.kredits", e.DeployCost, a.DeployCost);
        });
        AssertComponent<BattleStatsDefinition>(cardId, expected, actual, static (id, e, a) =>
        {
            AssertScalar(id, "battleStats.operationCost", e.OperateCost, a.OperateCost);
            AssertScalar(id, "battleStats.attack", e.Attack, a.Attack);
            AssertScalar(id, "battleStats.defense", e.Defense, a.Defense);
        });
        AssertComponent<TagDataDefinition>(cardId, expected, actual, static (id, e, a) =>
        {
            AssertScalar(id, "tagData.rarity", e.Rarity, a.Rarity);
            AssertStringList(id, "tagData.tags", e.Tags, a.Tags);
        });
        AssertComponent<KeywordsDefinition>(cardId, expected, actual, static (id, e, a) =>
        {
            AssertKeywordList(id, "keywords.keywords", e.Keywords, a.Keywords);
            AssertStringList(id, "keywords.attributes", e.Attributes, a.Attributes);
            AssertStringList(id, "keywords.unmapped", e.UnmappedAttributes, a.UnmappedAttributes);
            AssertScalar(id, "keywords.becomesVeteran", e.BecomesVeteran, a.BecomesVeteran);
            AssertScalar(id, "keywords.veteranOf", e.VeteranOf, a.VeteranOf);
        });
        AssertComponent<EffectsDefinition>(cardId, expected, actual, static (id, e, a) =>
        {
            AssertStringList(id, "effects.prefabs", e.Prefabs, a.Prefabs);
            AssertScalar(id, "effects.inline.count", e.Inline.Count, a.Inline.Count);
            for (var index = 0; index < e.Inline.Count; index++)
            {
                AssertScalar(
                    id,
                    $"effects.inline[{index}]",
                    PrefabJson.Serialize(e.Inline[index]),
                    PrefabJson.Serialize(a.Inline[index]));
            }
        });
    }

    private static void AssertComponent<TDefinition>(
        string cardId,
        CardDefinition expected,
        CardDefinition actual,
        Action<string, TDefinition, TDefinition> compare)
        where TDefinition : class, ICardDataComponentDefinition
    {
        var expectedComponent = FindComponent<TDefinition>(expected);
        var actualComponent = FindComponent<TDefinition>(actual);
        Assert.True(
            (expectedComponent is null) == (actualComponent is null),
            $"{cardId}：组件 '{typeof(TDefinition).Name}' 在场性不等值（期望 {expectedComponent is not null}、实际 {actualComponent is not null}）。");

        if (expectedComponent is not null && actualComponent is not null)
        {
            compare(cardId, expectedComponent, actualComponent);
        }
    }

    private static TDefinition? FindComponent<TDefinition>(CardDefinition definition)
        where TDefinition : class, ICardDataComponentDefinition
    {
        foreach (var component in definition.Components)
        {
            if (component is TDefinition typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static void AssertScalar<T>(string cardId, string field, T expected, T actual)
        => Assert.True(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{cardId}：字段 '{field}' 不等值——期望 [{expected}]，实际 [{actual}]。");

    private static void AssertStringList(
        string cardId, string field, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
        => Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{cardId}：字段 '{field}' 列表不等值——期望 [{string.Join(", ", expected)}]，实际 [{string.Join(", ", actual)}]。");

    private static void AssertKeywordList(
        string cardId, string field, IReadOnlyList<KeywordDeclaration> expected, IReadOnlyList<KeywordDeclaration> actual)
        => Assert.True(
            expected.SequenceEqual(actual),
            $"{cardId}：字段 '{field}' 词条列表不等值——期望 [{DescribeKeywords(expected)}]，实际 [{DescribeKeywords(actual)}]。");

    private static void AssertUnitTypeList(
        string cardId, string field, IReadOnlyList<UnitType> expected, IReadOnlyList<UnitType> actual)
        => Assert.True(
            expected.SequenceEqual(actual),
            $"{cardId}：字段 '{field}' 单位类型列表不等值——期望 [{string.Join(", ", expected)}]，实际 [{string.Join(", ", actual)}]。");

    private static string DescribeKeywords(IReadOnlyList<KeywordDeclaration> keywords)
        => string.Join(", ", keywords.Select(item => item.Value is null ? item.Id : $"{item.Id}={item.Value}"));

    // ─────────────────────────────────────────────────────────────────────────
    // 辅助。
    // ─────────────────────────────────────────────────────────────────────────

    private static CardDefinitionEntry LoadEntry(string json)
    {
        var body = CardDataJson.Deserialize(json, out var warnings);
        Assert.Empty(warnings);
        return new CardDefinitionEntry(body.Id, new CardDefinition(body.Name, body.Components));
    }

    private static JsonElement FindComponent(JsonDocument document, string componentName)
    {
        foreach (var component in document.RootElement.GetProperty("components").EnumerateArray())
        {
            if (string.Equals(component.GetProperty("component").GetString(), componentName, StringComparison.Ordinal))
            {
                return component;
            }
        }

        throw new InvalidOperationException($"输出缺少组件 '{componentName}'。");
    }

    private static string BuildInlineStringCardJson()
    {
        const string prefabJson = """
        { "schemaVersion": 2, "root": { "id": "inline-str-1", "mainTrigger": { "id": "t1", "stableKey": "字符串态·触发器", "kind": "passive", "hooks": ["unit.deployed"], "events": [ { "id": "e1", "entry": "HandleAsync", "assemblyKey": "inline-str-1" } ] } } }
        """;
        var escaped = JsonSerializer.Serialize(prefabJson);

        return $$"""
        {"schemaVersion":1,"id":"inline-string-card","name":"字符串态内联","components":[
          {"component":"factionCost","faction":"Japan","kredits":1},
          {"component":"tagData","rarity":"Standard"},
          {"component":"effects","inline":[{{escaped}}]}
        ]}
        """;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OrcEngine.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "orc-carddata-" + Guid.NewGuid().ToString("N"));

    private static void DeleteTempDir(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
