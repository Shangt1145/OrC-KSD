using System.Reflection;
using System.Text.Json;
using Orc.Game.Cards;
using Orc.Game.Cards.Data;
using Orc.Game.Cards.Data.Components;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 卡牌数据体**写方向**单元（X1·批 1）：反向映射逐项（Exact 16＋Valued 2＋老兵 2）、
/// keywords 策略（保真/合成/判据 a-d）、其余组件写出边界（typeCategory 三组合、battleStats 非单位、IsGuard）、
/// 文本契约（缩进/LF/尾换行/键序/空值规范）、写出隔离三类（未注册/无 writer/写入异常）与落盘契约
/// （SaveFile/SaveDirectory）。逐项可独立失败；内部构造器经反射构造（构造干预态——不扩张公开 API）。
/// </summary>
public class CardDataWriterTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // 反向映射逐项（16 Exact ＋ 2 Valued）：每项＝独立数据行。
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(KeywordIds.Blitz, "blitz")]
    [InlineData(KeywordIds.Fury, "fury")]
    [InlineData(KeywordIds.SmokeScreen, "smokescreen")]
    [InlineData(KeywordIds.Ambush, "ambush")]
    [InlineData(KeywordIds.Pincer, "pincer")]
    [InlineData(KeywordIds.Mobilize, "mobilize")]
    [InlineData(KeywordIds.Covert, "covert")]
    [InlineData(KeywordIds.Guard, "guard")]
    [InlineData(KeywordIds.Shock, "shock")]
    [InlineData(KeywordIds.Suppressed, "suppressed")]
    [InlineData(KeywordIds.Inhibited, "inhibited")]
    [InlineData(KeywordIds.Forecast, "forecast")]
    [InlineData(KeywordIds.Immune, "immune")]
    [InlineData(KeywordIds.CannotBeSuppressed, "cannotBeSuppressed")]
    [InlineData(KeywordIds.CannotBeInhibited, "cannotBeInhibited")]
    [InlineData(KeywordIds.Deathrattle, "deathrattle")]
    public void Exact_Keyword_Reverse_Generates_English_Attribute_And_Round_Trips(string keywordId, string expected)
    {
        var definition = CodeCard(keywords: [new KeywordDeclaration(keywordId)]);

        var json = CardDataJson.Serialize("rt-exact", definition, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(new[] { expected }, ReadComponentArray(json, "keywords", "attributes"));

        var body = CardDataJson.Deserialize(json, out var readWarnings);
        Assert.Empty(readWarnings);
        Assert.True(body.TryGetComponent<KeywordsDefinition>(out var keywords));
        Assert.Equal(new[] { expected }, keywords.Attributes);
        Assert.Equal(new[] { keywordId }, keywords.Keywords.Select(item => item.Id));
        Assert.Null(keywords.Keywords[0].Value);
    }

    [Theory]
    [InlineData(KeywordIds.Armor, 1, "heavyArmor1")]
    [InlineData(KeywordIds.Armor, 2, "heavyArmor2")]
    [InlineData(KeywordIds.Armor, 3, "heavyArmor3")]
    [InlineData(KeywordIds.Intelligence, 2, "intel2")]
    [InlineData(KeywordIds.Intelligence, 8, "intel8")]
    public void Valued_Keyword_Reverse_Generates_Prefixed_Attribute_And_Round_Trips(
        string keywordId, int value, string expected)
    {
        var definition = CodeCard(keywords: [new KeywordDeclaration(keywordId, value)]);

        var json = CardDataJson.Serialize("rt-valued", definition, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(new[] { expected }, ReadComponentArray(json, "keywords", "attributes"));

        var body = CardDataJson.Deserialize(json, out var readWarnings);
        Assert.Empty(readWarnings);
        Assert.True(body.TryGetComponent<KeywordsDefinition>(out var keywords));
        Assert.Equal(new[] { keywordId }, keywords.Keywords.Select(item => item.Id));
        Assert.Equal(value, keywords.Keywords[0].Value);
    }

    [Fact]
    public void Veteran_Links_Reverse_Generate_And_Round_Trip()
    {
        // BecomesVeteran（升级链信息——不产出「老兵」标记）。
        var becomes = CodeCard(becomesVeteran: "vet-card");
        var becomesJson = CardDataJson.Serialize("rt-vet-becomes", becomes, out var becomesWarnings);
        Assert.Empty(becomesWarnings);
        Assert.Equal(new[] { "BecomesVeteran:vet-card" }, ReadComponentArray(becomesJson, "keywords", "attributes"));

        var becomesBody = CardDataJson.Deserialize(becomesJson, out _);
        Assert.True(becomesBody.TryGetComponent<KeywordsDefinition>(out var becomesKeywords));
        Assert.Equal("vet-card", becomesKeywords.BecomesVeteran);
        Assert.Null(becomesKeywords.VeteranOf);
        Assert.Empty(becomesKeywords.Keywords);

        // VeteranOf（端口正式承载——标记随内容恢复；再写出幂等）。
        var veteran = CodeCard(veteranOf: "base-card");
        var veteranJson = CardDataJson.Serialize("rt-vet-of", veteran, out var veteranWarnings);
        Assert.Empty(veteranWarnings);
        Assert.Equal(new[] { "VeteranOf:base-card" }, ReadComponentArray(veteranJson, "keywords", "attributes"));

        var veteranBody = CardDataJson.Deserialize(veteranJson, out _);
        Assert.True(veteranBody.TryGetComponent<KeywordsDefinition>(out var veteranKeywords));
        Assert.Equal("base-card", veteranKeywords.VeteranOf);
        Assert.Equal(new[] { KeywordIds.Veteran }, veteranKeywords.Keywords.Select(item => item.Id));

        var rewritten = CardDataJson.Serialize(veteranBody, out var rewriteWarnings);
        Assert.Empty(rewriteWarnings);
        Assert.Equal(veteranJson, rewritten);
    }

    [Fact]
    public void Synthesis_Path_Composes_Ports_Then_Keywords_Then_Unmapped()
    {
        // 构造干预态：Attributes 空（合成路径）＋双端口＋参值＋留痕。
        var keywords = CreateKeywordsDefinition(
            attributes: [],
            keywords: [new KeywordDeclaration(KeywordIds.Armor, 2), new KeywordDeclaration(KeywordIds.Intelligence, 3)],
            unmapped: ["mystery-token"],
            becomesVeteran: "vet.x",
            veteranOf: "base.x");
        var body = new CardDataBody("synth-order", "合成", [keywords]);

        var json = CardDataJson.Serialize(body, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(
            new[] { "BecomesVeteran:vet.x", "VeteranOf:base.x", "heavyArmor2", "intel3", "mystery-token" },
            ReadComponentArray(json, "keywords", "attributes"));

        // 读回：端口双恢复、留痕等值、成员等值。
        var round = CardDataJson.Deserialize(json, out var roundWarnings);
        Assert.Empty(roundWarnings);
        Assert.True(round.TryGetComponent<KeywordsDefinition>(out var roundKeywords));
        Assert.Equal("vet.x", roundKeywords.BecomesVeteran);
        Assert.Equal("base.x", roundKeywords.VeteranOf);
        Assert.Equal(new[] { "mystery-token" }, roundKeywords.UnmappedAttributes);
        Assert.Equal(
            new[] { KeywordIds.Veteran, KeywordIds.Armor, KeywordIds.Intelligence },
            roundKeywords.Keywords.Select(item => item.Id));
        Assert.Equal(2, roundKeywords.Keywords[1].Value);
        Assert.Equal(3, roundKeywords.Keywords[2].Value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // keywords：保真优先 / 唯一真源 / 判据 a-d / 空产出。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fidelity_Path_Keeps_Malformed_And_Multiple_Veteran_Declarations_Without_Warning()
    {
        var source = """
        {"schemaVersion":1,"id":"fidelity-vet","name":"保真老兵","components":[
          {"component":"keywords","attributes":["BecomesVeteran:","VeteranOf:dup1","VeteranOf:dup2","salvage"]}
        ]}
        """;

        var body = CardDataJson.Deserialize(source, out var readWarnings);
        Assert.Empty(readWarnings);
        Assert.True(body.TryGetComponent<KeywordsDefinition>(out var loaded));
        Assert.Equal(
            new[] { "BecomesVeteran:", "VeteranOf:dup1", "VeteranOf:dup2", "salvage" },
            loaded.UnmappedAttributes);
        Assert.Null(loaded.BecomesVeteran);
        Assert.Null(loaded.VeteranOf);

        var json = CardDataJson.Serialize(body, out var warnings);

        // 保真即无损失：全量按序原样、零警告（畸形/多重原文随 Attributes 往返）。
        Assert.Empty(warnings);
        Assert.Equal(
            new[] { "BecomesVeteran:", "VeteranOf:dup1", "VeteranOf:dup2", "salvage" },
            ReadComponentArray(json, "keywords", "attributes"));

        var round = CardDataJson.Deserialize(json, out var roundWarnings);
        Assert.Empty(roundWarnings);
        Assert.True(round.TryGetComponent<KeywordsDefinition>(out var roundKeywords));
        Assert.Equal(
            new[] { "BecomesVeteran:", "VeteranOf:dup1", "VeteranOf:dup2", "salvage" },
            roundKeywords.UnmappedAttributes);
    }

    [Fact]
    public void Fidelity_Path_Uses_Attributes_As_Only_Truth_Without_Warning()
    {
        // 构造干预态：Attributes=X 且 Keywords=Y（不一致）——写出按 X、不比对、不警告。
        var keywords = CreateKeywordsDefinition(
            attributes: ["blitz"],
            keywords: [new KeywordDeclaration(KeywordIds.Fury)],
            unmapped: []);
        var body = new CardDataBody("inconsistent", "不一致", [keywords]);

        var json = CardDataJson.Serialize(body, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(new[] { "blitz" }, ReadComponentArray(json, "keywords", "attributes"));
    }

    [Fact]
    public void Bare_Veteran_Marker_Without_Port_Is_Warned()
    {
        // 判据 (a)：裸「老兵」标记（无 VeteranOf 端口）＝警告、跳过（不静默）。
        var definition = CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Veteran)]);

        var json = CardDataJson.Serialize("bare-vet", definition, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("bare-vet", warning);           // 卡 id
        Assert.Contains("keywords", warning);           // 组件名
        Assert.Contains(KeywordIds.Veteran, warning);   // 项原文
        Assert.Contains("无反向映射", warning);          // 原因类别
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "keywords", "attributes"));
    }

    [Fact]
    public void Valued_Keyword_Without_Value_Is_Warned()
    {
        // 判据 (b)：参值型无合法参值（Value 为 null）。
        var definition = CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Armor)]);

        CardDataJson.Serialize("no-value", definition, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("no-value", warning);
        Assert.Contains(KeywordIds.Armor, warning);
        Assert.Contains("参值不可表达", warning);
    }

    [Fact]
    public void Valued_Keyword_With_Zero_Or_Negative_Value_Is_Warned()
    {
        // 判据 (b)：参值型值 ≤0（heavyArmor0 类不合法）。
        foreach (var value in new[] { 0, -2 })
        {
            var definition = CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Armor, value)]);

            CardDataJson.Serialize("bad-value", definition, out var warnings);

            var warning = Assert.Single(warnings);
            Assert.Contains("bad-value", warning);
            Assert.Contains($"{value}", warning);
            Assert.Contains("参值不可表达", warning);
        }
    }

    [Fact]
    public void Exact_Keyword_With_Value_Is_Warned()
    {
        // 判据 (c)：Exact 表词条携带非空值（标识无值位）。
        var definition = CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Blitz, 2)]);

        CardDataJson.Serialize("exact-with-value", definition, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("exact-with-value", warning);
        Assert.Contains(KeywordIds.Blitz, warning);
        Assert.Contains("无值位", warning);
    }

    [Fact]
    public void Blank_Veteran_Port_Id_Is_Warned()
    {
        // 判据 (d)：老兵端口 id 空白（防御性——构造干预态）。
        var keywords = CreateKeywordsDefinition(attributes: [], keywords: [], unmapped: [], veteranOf: " ");
        var body = new CardDataBody("blank-port", "空白端口", [keywords]);

        var json = CardDataJson.Serialize(body, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("blank-port", warning);
        Assert.Contains("VeteranOf", warning);
        Assert.Contains("空白", warning);
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "keywords", "attributes"));
    }

    [Fact]
    public void Empty_Synthesis_Output_Writes_Empty_Attributes_Array_And_Reads_Back_In_Place()
    {
        var keywords = CreateKeywordsDefinition(attributes: [], keywords: [], unmapped: []);
        var body = new CardDataBody("empty-attr", "空产出", [keywords]);

        var json = CardDataJson.Serialize(body, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "keywords", "attributes"));

        // 结构等值：读回＝组件在场空态。
        var round = CardDataJson.Deserialize(json, out _);
        Assert.True(round.TryGetComponent<KeywordsDefinition>(out var roundKeywords));
        Assert.Empty(roundKeywords.Attributes);
        Assert.Empty(roundKeywords.Keywords);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 其余组件写出边界。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TypeCategory_Unit_Without_UnitTypes_Warns_And_Writes_Empty_Array()
    {
        var definition = CodeCard(category: CardCategory.Unit, unitTypes: Array.Empty<UnitType>());

        var json = CardDataJson.Serialize("unit-empty", definition, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("unit-empty", warning);
        Assert.Contains("typeCategory", warning);
        Assert.Contains("缺少单位类型", warning);
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "typeCategory", "type"));

        // 读回断裂实证：该组件被隔离（反序列化被拒）。
        var ok = CardDataJson.TryDeserialize(json, out var body, out var readWarnings, out _);
        Assert.True(ok);
        Assert.False(body!.TryGetComponent<TypeCategoryDefinition>(out _));
        Assert.Contains(readWarnings, item => item.Contains("typeCategory"));
    }

    [Fact]
    public void TypeCategory_Category_Mixed_With_UnitTypes_Warns_And_Writes_Through()
    {
        var definition = CodeCard(category: CardCategory.Command, unitTypes: [UnitType.Infantry]);

        var json = CardDataJson.Serialize("mixed", definition, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("mixed", warning);
        Assert.Contains("混用", warning);
        Assert.Equal(new[] { "order", "infantry" }, ReadComponentArray(json, "typeCategory", "type"));

        var ok = CardDataJson.TryDeserialize(json, out var body, out var readWarnings, out _);
        Assert.True(ok);
        Assert.False(body!.TryGetComponent<TypeCategoryDefinition>(out _));
        Assert.Contains(readWarnings, item => item.Contains("typeCategory"));
    }

    [Fact]
    public void TypeCategory_Duplicated_UnitTypes_Warn_And_Write_Through()
    {
        var typeCategory = CreateTypeCategoryDefinition(CardCategory.Unit, [UnitType.Tank, UnitType.Tank]);
        var body = new CardDataBody("duplicate-types", "重复类型", [typeCategory]);

        var json = CardDataJson.Serialize(body, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("duplicate-types", warning);
        Assert.Contains("重复单位类型", warning);
        Assert.Equal(new[] { "tank", "tank" }, ReadComponentArray(json, "typeCategory", "type"));

        var ok = CardDataJson.TryDeserialize(json, out var round, out var readWarnings, out _);
        Assert.True(ok);
        Assert.False(round!.TryGetComponent<TypeCategoryDefinition>(out _));
        Assert.Contains(readWarnings, item => item.Contains("typeCategory"));
    }

    [Fact]
    public void BattleStats_On_NonUnit_Card_Writes_Through_Without_Warning()
    {
        var source = """
        {"schemaVersion":1,"id":"order-stats","name":"指令带数值","components":[
          {"component":"typeCategory","type":["order"]},
          {"component":"battleStats","operationCost":1,"attack":2,"defense":3}
        ]}
        """;

        var body = CardDataJson.Deserialize(source, out var readWarnings);
        Assert.Empty(readWarnings);

        var json = CardDataJson.Serialize(body, out var warnings);

        // 照写保真、不警告（读回成功——跨组件语义一致性不属写方向职责）。
        Assert.Empty(warnings);
        Assert.Contains("battleStats", ReadComponentNames(json));

        var round = CardDataJson.Deserialize(json, out var roundWarnings);
        Assert.Empty(roundWarnings);
        Assert.True(round.TryGetComponent<BattleStatsDefinition>(out var stats));
        Assert.Equal(2, stats.Attack);
    }

    [Fact]
    public void IsGuard_True_Warns_And_Is_Not_Carried()
    {
        var definition = CodeCard("守护卡", isGuard: true);

        var json = CardDataJson.Serialize("guard-card", definition, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("guard-card", warning);
        Assert.Contains("IsGuard", warning);

        var body = CardDataJson.Deserialize(json, out _);
        var readBack = new CardDefinition(body.Name, body.Components);
        Assert.False(readBack.IsGuard);
    }

    [Fact]
    public void Empty_Collections_Are_Written_As_Explicit_Empty_Arrays()
    {
        var tagData = TagDataDefinition.Read(Parse("{\"rarity\":\"Standard\"}"));
        var keywords = CreateKeywordsDefinition(attributes: [], keywords: [], unmapped: []);
        var effects = EffectsDefinition.Read(Parse("{\"prefabs\":[],\"inline\":[]}"));
        var body = new CardDataBody("empty-arrays", "空集合", [tagData, keywords, effects]);

        var json = CardDataJson.Serialize(body, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "tagData", "tags"));
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "keywords", "attributes"));
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "effects", "prefabs"));
        Assert.Equal(Array.Empty<string>(), ReadComponentArray(json, "effects", "inline"));

        var round = CardDataJson.Deserialize(json, out var roundWarnings);
        Assert.Empty(roundWarnings);
        Assert.True(round.TryGetComponent<TagDataDefinition>(out var roundTags));
        Assert.Empty(roundTags.Tags);
    }

    [Fact]
    public void Empty_Component_Card_Writes_Explicit_Empty_Components_Array_Without_Warning()
    {
        var body = new CardDataBody("empty-card", "空组件");

        var json = CardDataJson.Serialize(body, out var warnings);

        Assert.Empty(warnings);
        Assert.Contains("\"components\": []", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Components_Follow_Definition_Set_Order()
    {
        var source = """
        {"schemaVersion":1,"id":"reversed","name":"反序","components":[
          {"component":"tagData","rarity":"Standard"},
          {"component":"factionCost","faction":"Japan","kredits":0}
        ]}
        """;

        var body = CardDataJson.Deserialize(source, out _);

        var json = CardDataJson.Serialize(body, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(new[] { "tagData", "factionCost" }, ReadComponentNames(json));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 文本契约（缩进 / LF / 尾换行 / 键序 / 无注释尾逗号）。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Text_Contract_Indent_LineFeeds_Trailing_Newline_And_Key_Order()
    {
        var definition = CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Armor, 2)]);
        var json = CardDataJson.Serialize("text-contract", definition, out _);

        // 2 空格缩进多行、LF、尾随换行。
        Assert.StartsWith("{\n  \"schemaVersion\": 1,\n  \"id\": \"text-contract\",\n  \"name\": \"测试卡\",\n  \"components\": [\n", json, StringComparison.Ordinal);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.Contains("\n    {\n      \"component\": \"", json, StringComparison.Ordinal);

        // 顶层键序：schemaVersion → id → name → components。
        var iSchema = json.IndexOf("\"schemaVersion\"", StringComparison.Ordinal);
        var iId = json.IndexOf("\"id\"", StringComparison.Ordinal);
        var iName = json.IndexOf("\"name\"", StringComparison.Ordinal);
        var iComponents = json.IndexOf("\"components\"", StringComparison.Ordinal);
        Assert.True(iSchema >= 0 && iSchema < iId && iId < iName && iName < iComponents);

        // 组件字段序：faction → kredits；operationCost → attack → defense。
        var iFaction = json.IndexOf("\"faction\"", StringComparison.Ordinal);
        var iKredits = json.IndexOf("\"kredits\"", StringComparison.Ordinal);
        Assert.True(iFaction >= 0 && iFaction < iKredits);

        var iOperation = json.IndexOf("\"operationCost\"", StringComparison.Ordinal);
        var iAttack = json.IndexOf("\"attack\"", StringComparison.Ordinal);
        var iDefense = json.IndexOf("\"defense\"", StringComparison.Ordinal);
        Assert.True(iOperation >= 0 && iOperation < iAttack && iAttack < iDefense);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 写出隔离三类（未注册 / 无 writer / 写入异常）。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Unregistered_Component_Is_Warned_And_Skipped()
    {
        var body = new CardDataBody("iso-unregistered", "未注册", [new ProbeDefinition("probe-unregistered")]);

        var json = CardDataJson.Serialize(body, out var warnings);

        var warning = Assert.Single(warnings);
        Assert.Contains("iso-unregistered", warning);
        Assert.Contains("probe-unregistered", warning);
        Assert.Contains("未注册", warning);
        Assert.Empty(ReadComponentNames(json));
    }

    [Fact]
    public void Component_Registered_Without_Writer_Is_Warned_And_Skipped()
    {
        var name = "probe-nowriter-" + Guid.NewGuid().ToString("N");
        try
        {
            // 注册期允许 writer 缺省（合法状态——只读/只装载组件）。
            CardComponentRegistry.Register(
                name,
                CardComponentPhase.Load,
                static (_, _, _) => Task.CompletedTask,
                _ => new ProbeDefinition(name));

            var body = new CardDataBody("iso-nowriter", "无写出器", [new ProbeDefinition(name)]);

            var json = CardDataJson.Serialize(body, out var warnings);

            var warning = Assert.Single(warnings);
            Assert.Contains("iso-nowriter", warning);
            Assert.Contains(name, warning);
            Assert.Contains("无写出器", warning);
            Assert.Empty(ReadComponentNames(json));
        }
        finally
        {
            CardComponentRegistry.Unregister(name);
        }
    }

    [Fact]
    public void Writer_Exception_Is_Warned_And_Skipped_While_Others_Survive()
    {
        var name = "probe-throwing-" + Guid.NewGuid().ToString("N");
        try
        {
            CardComponentRegistry.Register(
                name,
                CardComponentPhase.Load,
                static (_, _, _) => Task.CompletedTask,
                _ => new ProbeDefinition(name),
                writer: (_, _, _) => throw new InvalidOperationException("boom"));

            var tagData = TagDataDefinition.Read(Parse("{\"rarity\":\"Standard\"}"));
            var body = new CardDataBody("iso-throw", "写入异常", [new ProbeDefinition(name), tagData]);

            var json = CardDataJson.Serialize(body, out var warnings);

            var warning = Assert.Single(warnings);
            Assert.Contains("iso-throw", warning);
            Assert.Contains("写入异常", warning);
            Assert.Contains("boom", warning);

            // 其余组件照常（隔离不阻断）。
            Assert.Equal(new[] { "tagData" }, ReadComponentNames(json));
        }
        finally
        {
            CardComponentRegistry.Unregister(name);
        }
    }

    [Fact]
    public void Serialize_Entry_Points_Are_Consistent_And_Fail_Fast_On_Invalid_Input()
    {
        var definition = CodeCard();

        var fromEntry = CardDataJson.Serialize(new CardDefinitionEntry("ep", definition), out var entryWarnings);
        var fromPair = CardDataJson.Serialize("ep", definition, out var pairWarnings);
        var body = CardDataJson.Deserialize(fromPair, out _);
        var fromBody = CardDataJson.Serialize(body, out var bodyWarnings);

        Assert.Empty(entryWarnings);
        Assert.Empty(pairWarnings);
        Assert.Empty(bodyWarnings);
        Assert.Equal(fromEntry, fromPair);
        Assert.Equal(fromPair, fromBody);

        Assert.Throws<ArgumentNullException>(() => CardDataJson.Serialize((CardDataBody)null!, out _));
        Assert.Throws<ArgumentNullException>(() => CardDataJson.Serialize((CardDefinitionEntry)null!, out _));
        Assert.Throws<ArgumentNullException>(() => CardDataJson.Serialize("x", null!, out _));
        Assert.Throws<ArgumentException>(() => CardDataJson.Serialize("  ", definition, out _));
        Assert.Throws<ArgumentException>(() => CardDataJson.Serialize(new CardDefinitionEntry("  ", definition), out _));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 落盘契约（SaveFile / SaveDirectory）。
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveFile_Writes_Single_Card_With_Expected_Name_And_Utf8_No_Bom()
    {
        var baseDirectory = NewTempDir();
        var directory = Path.Combine(baseDirectory, "nested", "sub");
        try
        {
            var entry = new CardDefinitionEntry("saved-card", CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Armor, 2)]));

            var result = CardDataWriter.SaveFile(directory, entry);

            var success = Assert.Single(result.Succeeded);
            Assert.Empty(result.Failures);
            Assert.Empty(result.Warnings);
            Assert.Equal("saved-card", success.CardId);

            var path = Path.Combine(directory, "saved-card.card.json");
            Assert.Equal(path, success.File);
            Assert.True(File.Exists(path));

            var expected = CardDataJson.Serialize(entry, out _);
            Assert.Equal(expected, File.ReadAllText(path));

            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'{', bytes[0]);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }
        finally
        {
            DeleteTempDir(baseDirectory);
        }
    }

    [Theory]
    [InlineData("bad/id")]
    [InlineData("bad\\id")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(" ")]
    public void SaveFile_Rejects_Illegal_Card_Id(string badId)
    {
        var directory = NewTempDir();
        try
        {
            var result = CardDataWriter.SaveFile(directory, new CardDefinitionEntry(badId, CodeCard()));

            Assert.Empty(result.Succeeded);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(badId, failure.CardId);
            Assert.Contains("非法", failure.Error);

            // 拒绝＝不转义、不照写：目录（若在）零文件。
            if (Directory.Exists(directory))
            {
                Assert.Empty(Directory.GetFiles(directory));
            }
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SaveFile_Overwrites_Existing_File()
    {
        var directory = NewTempDir();
        try
        {
            CardDataWriter.SaveFile(directory, new CardDefinitionEntry("over", CodeCard("旧名")));
            var second = CardDataWriter.SaveFile(directory, new CardDefinitionEntry("over", CodeCard("新名")));

            Assert.Single(second.Succeeded);
            Assert.Empty(second.Failures);

            var text = File.ReadAllText(Path.Combine(directory, "over.card.json"));
            Assert.Contains("新名", text, StringComparison.Ordinal);
            Assert.DoesNotContain("旧名", text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SaveDirectory_Writes_All_Cards_And_Loads_Back_Cleanly()
    {
        var directory = NewTempDir();
        try
        {
            var entries = new[]
            {
                new CardDefinitionEntry("dir-a", CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Fury)])),
                new CardDefinitionEntry("dir-b", CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Guard)])),
            };

            var result = CardDataWriter.SaveDirectory(directory, entries);

            Assert.Equal(2, result.Succeeded.Count);
            Assert.Empty(result.Failures);
            Assert.Empty(result.Warnings);
            Assert.Equal(2, Directory.GetFiles(directory, "*.card.json").Length);

            var loaded = CardDataLoader.LoadDirectory(directory);

            Assert.Equal(2, loaded.Definitions.Count);
            Assert.Empty(loaded.Failures);
            Assert.Empty(loaded.Warnings);
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SaveDirectory_Duplicate_Card_Id_Keeps_First_And_Fails_Rest()
    {
        var directory = NewTempDir();
        try
        {
            var first = new CardDefinitionEntry("dup", CodeCard("首见"));
            var duplicate = new CardDefinitionEntry("dup", CodeCard("重复者"));
            var other = new CardDefinitionEntry("other", CodeCard());

            var result = CardDataWriter.SaveDirectory(directory, [first, duplicate, other]);

            Assert.Equal(new[] { "dup", "other" }, result.Succeeded.Select(item => item.CardId));
            var failure = Assert.Single(result.Failures);
            Assert.Equal("dup", failure.CardId);
            Assert.Contains("重复", failure.Error);

            Assert.Contains("首见", File.ReadAllText(Path.Combine(directory, "dup.card.json")), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SaveDirectory_Single_Card_Io_Failure_Is_Isolated()
    {
        var directory = NewTempDir();
        try
        {
            Directory.CreateDirectory(directory);
            // 构造单卡 IO 失败：同卡名位置由「目录」占位（写入必然失败）。
            Directory.CreateDirectory(Path.Combine(directory, "blocked.card.json"));

            var blocked = new CardDefinitionEntry("blocked", CodeCard());
            var ok = new CardDefinitionEntry("fine", CodeCard());

            var result = CardDataWriter.SaveDirectory(directory, [blocked, ok]);

            var success = Assert.Single(result.Succeeded);
            Assert.Equal("fine", success.CardId);
            var failure = Assert.Single(result.Failures);
            Assert.Equal("blocked", failure.CardId);
            Assert.Contains("写入失败", failure.Error);
            Assert.True(File.Exists(Path.Combine(directory, "fine.card.json")));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SaveDirectory_Empty_List_Returns_Empty_Success_With_Zero_Files()
    {
        var directory = NewTempDir();
        try
        {
            var result = CardDataWriter.SaveDirectory(directory, []);

            Assert.Empty(result.Succeeded);
            Assert.Empty(result.Failures);
            Assert.Empty(result.Warnings);
            if (Directory.Exists(directory))
            {
                Assert.Empty(Directory.GetFiles(directory));
            }
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SaveDirectory_Directory_Preparation_Failure_Throws()
    {
        var directory = NewTempDir();
        try
        {
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, "blocker");
            File.WriteAllText(filePath, "x");

            Assert.ThrowsAny<IOException>(() => CardDataWriter.SaveDirectory(
                filePath, [new CardDefinitionEntry("whatever", CodeCard())]));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    [Fact]
    public void SaveDirectory_Collects_Serialization_Warnings_While_Card_Is_Still_Written()
    {
        var directory = NewTempDir();
        try
        {
            // 不可还原关键词（重甲缺值）＝警告不阻断：卡照写（项级降级）。
            var definition = CodeCard(keywords: [new KeywordDeclaration(KeywordIds.Armor)]);
            var entry = new CardDefinitionEntry("warn-but-written", definition);

            var result = CardDataWriter.SaveDirectory(directory, [entry]);

            var success = Assert.Single(result.Succeeded);
            Assert.Equal("warn-but-written", success.CardId);
            Assert.Empty(result.Failures);
            var warning = Assert.Single(result.Warnings);
            Assert.Contains("warn-but-written", warning);
            Assert.Contains("参值不可表达", warning);
            Assert.True(File.Exists(Path.Combine(directory, "warn-but-written.card.json")));
        }
        finally
        {
            DeleteTempDir(directory);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 辅助。
    // ─────────────────────────────────────────────────────────────────────────

    private static CardDefinition CodeCard(
        string name = "测试卡",
        IEnumerable<KeywordDeclaration>? keywords = null,
        string? becomesVeteran = null,
        string? veteranOf = null,
        CardCategory category = CardCategory.Unit,
        IEnumerable<UnitType>? unitTypes = null,
        bool isGuard = false)
        => new(
            name,
            deployCost: 1,
            operateCost: 0,
            attack: 1,
            defense: 1,
            category: category,
            keywords: keywords,
            unitTypes: unitTypes ?? new[] { UnitType.Infantry },
            isGuard: isGuard,
            faction: Faction.Germany,
            rarity: Rarity.Standard,
            becomesVeteran: becomesVeteran,
            veteranOf: veteranOf);

    private static KeywordsDefinition CreateKeywordsDefinition(
        IReadOnlyList<string> attributes,
        IReadOnlyList<KeywordDeclaration> keywords,
        IReadOnlyList<string> unmapped,
        string? becomesVeteran = null,
        string? veteranOf = null)
    {
        var constructor = typeof(KeywordsDefinition).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(IReadOnlyList<string>), typeof(IReadOnlyList<KeywordDeclaration>), typeof(IReadOnlyList<string>), typeof(string), typeof(string)],
            modifiers: null)
            ?? throw new InvalidOperationException("未找到 KeywordsDefinition 内部构造器。");

        return (KeywordsDefinition)constructor.Invoke([attributes, keywords, unmapped, becomesVeteran, veteranOf]);
    }

    private static TypeCategoryDefinition CreateTypeCategoryDefinition(
        CardCategory category,
        IReadOnlyList<UnitType> unitTypes)
    {
        var constructor = typeof(TypeCategoryDefinition).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(CardCategory), typeof(IReadOnlyList<UnitType>)],
            modifiers: null)
            ?? throw new InvalidOperationException("未找到 TypeCategoryDefinition 内部构造器。");

        return (TypeCategoryDefinition)constructor.Invoke([category, unitTypes]);
    }

    private static IReadOnlyList<string> ReadComponentNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("components").EnumerateArray()
            .Select(component => component.GetProperty("component").GetString()!)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadComponentArray(string json, string componentName, string fieldName)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var component in document.RootElement.GetProperty("components").EnumerateArray())
        {
            if (string.Equals(component.GetProperty("component").GetString(), componentName, StringComparison.Ordinal))
            {
                return component.GetProperty(fieldName).EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray();
            }
        }

        throw new InvalidOperationException($"输出缺少组件 '{componentName}'。");
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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

    private sealed class ProbeDefinition(string componentName) : ICardDataComponentDefinition
    {
        public string ComponentName { get; } = componentName;
    }
}
