using System.Text.Json;
using Orc.Game.Cards;
using Orc.Game.Cards.Data;
using Orc.Game.Cards.Data.Components;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 卡牌数据体（S1/S2/S3 验收锚点）：JSON 读写与版本、组件隔离与重复拒绝、六个内置组件定义的
/// 反序列化与自校验、type 列表拆解规则（P10）、词条映射与未实现留痕（P9）、注册面、以及
/// <see cref="CardDefinition"/> 的组件集主构造与派生读面（P2＝A）。
/// </summary>
public class CardDataBodyTests
{
    private const string SampleJson = """
    {
      "schemaVersion": 1,
      "id": "13e_dragons",
      "name": "第 13 龙骑兵团",
      "components": [
        { "component": "typeCategory", "type": ["infantry"] },
        { "component": "factionCost", "faction": "France", "kredits": 0 },
        { "component": "battleStats", "operationCost": 0, "attack": 1, "defense": 2 },
        { "component": "tagData", "rarity": "Standard" },
        { "component": "keywords", "attributes": ["blitz"] }
      ]
    }
    """;

    [Fact]
    public void Read_Parses_Metadata_And_Components_In_Declaration_Order()
    {
        var body = CardDataJson.Deserialize(SampleJson, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal("13e_dragons", body.Id);
        Assert.Equal("第 13 龙骑兵团", body.Name);
        Assert.Equal(
            new[] { "typeCategory", "factionCost", "battleStats", "tagData", "keywords" },
            body.Components.Select(item => item.ComponentName));

        Assert.True(body.TryGetComponent<FactionCostDefinition>(out var factionCost));
        Assert.Equal(Faction.France, factionCost.Faction);
        Assert.Equal(0, factionCost.DeployCost);

        Assert.True(body.TryGetComponent<BattleStatsDefinition>(out var stats));
        Assert.Equal(0, stats.OperateCost);
        Assert.Equal(1, stats.Attack);
        Assert.Equal(2, stats.Defense);
    }

    [Fact]
    public void Read_Rejects_Schema_Version_Mismatch_Without_Silent_Downgrade()
    {
        var json = SampleJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);

        var ok = CardDataJson.TryDeserialize(json, out var body, out _, out var error);

        Assert.False(ok);
        Assert.Null(body);
        Assert.NotNull(error);
        Assert.Contains("schema", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_Isolates_Unknown_Component_And_Keeps_Others()
    {
        var json = SampleJson.Replace(
            """{ "component": "tagData", "rarity": "Standard" }""",
            """{ "component": "tagData", "rarity": "Standard" }, { "component": "notRegistered", "x": 1 }""",
            StringComparison.Ordinal);

        var body = CardDataJson.Deserialize(json, out var warnings);

        Assert.Single(warnings);
        Assert.Contains("notRegistered", warnings[0], StringComparison.Ordinal);
        Assert.True(body.TryGetComponent<TagDataDefinition>(out _));
    }

    [Fact]
    public void Read_Rejects_Duplicate_Component()
    {
        var json = SampleJson.Replace(
            """{ "component": "tagData", "rarity": "Standard" }""",
            """{ "component": "tagData", "rarity": "Standard" }, { "component": "tagData", "rarity": "Elite" }""",
            StringComparison.Ordinal);

        var ok = CardDataJson.TryDeserialize(json, out var body, out _, out var error);

        Assert.False(ok);
        Assert.Null(body);
        Assert.NotNull(error);
    }

    [Fact]
    public void Deserialize_Throws_On_Missing_Id()
    {
        var json = SampleJson.Replace("""  "id": "13e_dragons",""", string.Empty, StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => CardDataJson.Deserialize(json, out _));
    }

    [Fact]
    public void TypeCategory_Splits_UnitType_List()
    {
        var definition = TypeCategoryDefinition.Read(ParseType("""["infantry","tank"]"""));

        Assert.Equal(CardCategory.Unit, definition.Category);
        Assert.Equal(new[] { UnitType.Infantry, UnitType.Tank }, definition.UnitTypes);
    }

    [Fact]
    public void TypeCategory_Maps_Category_Words()
    {
        Assert.Equal(CardCategory.Command, TypeCategoryDefinition.Read(ParseType("""["order"]""")).Category);
        Assert.Equal(CardCategory.Counter, TypeCategoryDefinition.Read(ParseType("""["countermeasure"]""")).Category);
    }

    [Fact]
    public void TypeCategory_Rejects_Illegal_Combinations()
    {
        Assert.Throws<FormatException>(() => TypeCategoryDefinition.Read(ParseType("""["order","infantry"]""")));
        Assert.Throws<FormatException>(() => TypeCategoryDefinition.Read(ParseType("""["order","countermeasure"]""")));
        Assert.Throws<FormatException>(() => TypeCategoryDefinition.Read(ParseType("[]")));
        Assert.Throws<FormatException>(() => TypeCategoryDefinition.Read(ParseType("""["infantry","infantry"]""")));
        Assert.Throws<FormatException>(() => TypeCategoryDefinition.Read(ParseType("""["unknownType"]""")));
    }

    [Fact]
    public void Keywords_Maps_Official_Identifiers_And_Traces_Unmapped()
    {
        var definition = KeywordsDefinition.Read(ParseAttributes("""["blitz","guard","heavyArmor2","intel3"]"""));

        Assert.Equal(
            new[] { KeywordIds.Blitz, KeywordIds.Armor, KeywordIds.Intelligence },
            definition.Keywords.Select(item => item.Id));
        Assert.Equal(new int?[] { null, 2, 3 }, definition.Keywords.Select(item => item.Value));
        Assert.Equal(new[] { "guard" }, definition.UnmappedAttributes);
        Assert.Equal(new[] { "blitz", "guard", "heavyArmor2", "intel3" }, definition.Attributes);
    }

    [Fact]
    public void Keywords_Traces_Unmapped_Without_Failing()
    {
        var definition = KeywordsDefinition.Read(ParseAttributes("""["shock","BecomesVeteran:102_grenadier"]"""));

        Assert.Empty(definition.Keywords);
        Assert.Equal(2, definition.UnmappedAttributes.Count);
    }

    [Fact]
    public void Registry_Contains_Six_BuiltIn_Components_In_Registration_Order()
    {
        Assert.Equal(
            new[] { "typeCategory", "factionCost", "battleStats", "tagData", "keywords", "effects" },
            CardComponentRegistry.Registered.Select(entry => entry.Name));

        Assert.Equal(CardComponentPhase.Construction, CardComponentRegistry.Registered[0].Phase);
        Assert.Equal(CardComponentPhase.Load, CardComponentRegistry.Registered[3].Phase);
    }

    [Fact]
    public void Registry_Rejects_Duplicate_Registration()
    {
        var name = "test-duplicate-" + Guid.NewGuid().ToString("N");

        try
        {
            CardComponentRegistry.Register<FactionCostDefinition>(
                name, CardComponentPhase.Load, static (_, _, _) => Task.CompletedTask, FactionCostDefinition.Read);

            Assert.Throws<InvalidOperationException>(() => CardComponentRegistry.Register<FactionCostDefinition>(
                name, CardComponentPhase.Load, static (_, _, _) => Task.CompletedTask, FactionCostDefinition.Read));
        }
        finally
        {
            CardComponentRegistry.Unregister(name);
        }
    }

    [Fact]
    public void CardDefinition_From_Component_Set_Derives_Read_Faces()
    {
        var body = CardDataJson.Deserialize(SampleJson, out _);

        var definition = new CardDefinition(body.Name, body.Components);

        Assert.Equal("第 13 龙骑兵团", definition.Name);
        Assert.Equal(CardCategory.Unit, definition.Category);
        Assert.Equal(Faction.France, definition.Faction);
        Assert.Equal(0, definition.DeployCost);
        Assert.Equal(0, definition.OperateCost);
        Assert.Equal(1, definition.Attack);
        Assert.Equal(2, definition.Defense);
        Assert.Equal(Rarity.Standard, definition.Rarity);
        Assert.Equal(new[] { UnitType.Infantry }, definition.UnitTypes);
        Assert.Equal(new[] { KeywordIds.Blitz }, definition.Keywords.Select(item => item.Id));
        Assert.Empty(definition.UnmappedAttributes);
        Assert.Empty(definition.Tags);
        Assert.False(definition.IsGuard);
        Assert.Equal(5, definition.Components.Count);
    }

    [Fact]
    public void CardDefinition_Requires_FactionCost_And_TagData_Components()
    {
        var tagOnly = new ICardDataComponentDefinition[]
        {
            TagDataDefinition.Read(Parse("{\"rarity\": \"Standard\"}")),
        };

        Assert.Throws<ArgumentException>(() => new CardDefinition("缺国籍", tagOnly));
    }

    [Fact]
    public void Effects_Parses_References_And_Inline_Prefabs()
    {
        var json = """
        {"schemaVersion":1,"id":"c","name":"n","components":[
          {"component":"factionCost","faction":"Germany","kredits":1},
          {"component":"tagData","rarity":"Standard"},
          {"component":"effects","prefabs":["p1"],"inline":[
            {"schemaVersion":2,"root":{"id":"p2","mainTrigger":{"id":"t","stableKey":"s","kind":"passive",
              "hooks":["unit.deployed"],"events":[{"id":"e","entry":"HandleAsync","assemblyKey":"k"}]}}}]}]}
        """;

        var body = CardDataJson.Deserialize(json, out var warnings);

        Assert.Empty(warnings);
        Assert.True(body.TryGetComponent<EffectsDefinition>(out var effects));
        Assert.Equal(new[] { "p1" }, effects.Prefabs);
        Assert.Single(effects.Inline);
        Assert.Equal("p2", effects.Inline[0].Root.Id);
    }

    [Fact]
    public void Loader_Scans_Directory_Isolates_Bad_File_And_Merges_Inline_Declarations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orc-carddata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "ok.card.json"), SampleJson);
            File.WriteAllText(
                Path.Combine(directory, "inline.card.json"),
                """
                {"schemaVersion":1,"id":"inline-card","name":"内联卡","components":[
                  {"component":"factionCost","faction":"Soviet","kredits":2},
                  {"component":"tagData","rarity":"Limited"},
                  {"component":"effects","inline":[
                    {"schemaVersion":2,"root":{"id":"inline-prefab","mainTrigger":{"id":"t","stableKey":"s",
                      "kind":"passive","hooks":["unit.deployed"],
                      "events":[{"id":"e","entry":"HandleAsync","assemblyKey":"k"}]}}}]}]}
                """);
            File.WriteAllText(Path.Combine(directory, "bad.card.json"), "{ not json");

            var result = CardDataLoader.LoadDirectory(directory);

            // 单文件隔离：坏文件不阻断整批。
            Assert.Equal(2, result.Definitions.Count);
            Assert.Single(result.Failures);

            // 内联预制体被注册进产出集，并按 id 并入该卡的效果声明（装载链因此只有"引用"一条）。
            Assert.Single(result.Prefabs);
            Assert.Equal("inline-prefab", result.Prefabs[0].Root.Id);
            Assert.Contains(
                result.EffectDeclarations,
                entry => entry.CardId == "inline-card"
                    && entry.PrefabIds.Count == 1
                    && entry.PrefabIds[0] == "inline-prefab");

            // 定义派生读面可用。
            var ok = result.Definitions.Single(entry => entry.Id == "13e_dragons");
            Assert.Equal(1, ok.Definition.Attack);
            Assert.Equal(Faction.France, ok.Definition.Faction);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonElement ParseType(string arrayJson) => Parse("{\"type\": " + arrayJson + "}");

    private static JsonElement ParseAttributes(string arrayJson) => Parse("{\"attributes\": " + arrayJson + "}");

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
