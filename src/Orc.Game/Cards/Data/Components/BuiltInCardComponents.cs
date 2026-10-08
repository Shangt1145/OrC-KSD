using System.Text.Json;

namespace Orc.Game.Cards.Data.Components;

/// <summary>阵营〔国籍〕＋部署费（＝花费）组件定义（数据体组件名 <c>factionCost</c>；相位＝构造期）。</summary>
public sealed class FactionCostDefinition : ICardDataComponentDefinition
{
    /// <summary>数据体组件类型名。</summary>
    public const string DefinitionName = "factionCost";

    /// <summary>数据体字段：国籍（官方 <c>faction</c> 字面值）。</summary>
    public Faction Faction { get; }

    /// <summary>数据体字段：部署费（官方 <c>kredits</c>）。</summary>
    public int DeployCost { get; }

    internal FactionCostDefinition(Faction faction, int deployCost)
    {
        Faction = faction;
        DeployCost = deployCost;
    }

    /// <inheritdoc />
    public string ComponentName => DefinitionName;

    /// <summary>反序列化（国籍＝必填枚举字面值、部署费＝必填整数）。</summary>
    public static FactionCostDefinition Read(JsonElement element)
        => new(
            ComponentText.RequireEnum<Faction>(element, "faction"),
            ComponentText.RequireInt(element, "kredits"));

    /// <summary>序列化（写方向对称）：国籍＝枚举字面值、部署费＝整数（规范字段序：faction → kredits）。</summary>
    public static void Write(FactionCostDefinition definition, Utf8JsonWriter writer, IList<string> warnings)
    {
        writer.WriteString("faction", definition.Faction.ToString());
        writer.WriteNumber("kredits", definition.DeployCost);
    }
}

/// <summary>对战数值组件定义（数据体组件名 <c>battleStats</c>；相位＝构造期；仅单位卡）。</summary>
public sealed class BattleStatsDefinition : ICardDataComponentDefinition
{
    /// <summary>数据体组件类型名。</summary>
    public const string DefinitionName = "battleStats";

    /// <summary>数据体字段：行动费（官方 <c>operationCost</c>）。</summary>
    public int OperateCost { get; }

    /// <summary>数据体字段：攻击力（官方 <c>attack</c>）。</summary>
    public int Attack { get; }

    /// <summary>数据体字段：防御力（官方 <c>defense</c>）。</summary>
    public int Defense { get; }

    internal BattleStatsDefinition(int operateCost, int attack, int defense)
    {
        OperateCost = operateCost;
        Attack = attack;
        Defense = defense;
    }

    /// <inheritdoc />
    public string ComponentName => DefinitionName;

    /// <summary>反序列化（三字段均必填整数）。</summary>
    public static BattleStatsDefinition Read(JsonElement element)
        => new(
            ComponentText.RequireInt(element, "operationCost"),
            ComponentText.RequireInt(element, "attack"),
            ComponentText.RequireInt(element, "defense"));

    /// <summary>
    /// 序列化（写方向对称）：三字段（规范字段序：operationCost → attack → defense）。
    /// 非单位卡带本组件＝照写保真、不警告（读回成功——跨组件语义一致性不属写方向职责，归预检器）。
    /// </summary>
    public static void Write(BattleStatsDefinition definition, Utf8JsonWriter writer, IList<string> warnings)
    {
        writer.WriteNumber("operationCost", definition.OperateCost);
        writer.WriteNumber("attack", definition.Attack);
        writer.WriteNumber("defense", definition.Defense);
    }
}

/// <summary>标签数据组件定义（数据体组件名 <c>tagData</c>；相位＝加载期）：稀有度（必填）＋开放 tag（可选）。</summary>
public sealed class TagDataDefinition : ICardDataComponentDefinition
{
    /// <summary>数据体组件类型名。</summary>
    public const string DefinitionName = "tagData";

    /// <summary>数据体字段：稀有度（官方 <c>rarity</c> 字面值）。</summary>
    public Rarity Rarity { get; }

    /// <summary>数据体字段：开放 tag（子类别；官方语料无对应，Orc 扩展）。</summary>
    public IReadOnlyList<string> Tags { get; }

    internal TagDataDefinition(Rarity rarity, IReadOnlyList<string> tags)
    {
        Rarity = rarity;
        Tags = tags;
    }

    /// <inheritdoc />
    public string ComponentName => DefinitionName;

    /// <summary>反序列化（稀有度＝必填枚举字面值、tags＝可选字符串数组）。</summary>
    public static TagDataDefinition Read(JsonElement element)
        => new(
            ComponentText.RequireEnum<Rarity>(element, "rarity"),
            ComponentText.GetStringArray(element, "tags"));

    /// <summary>序列化（写方向对称）：稀有度＝枚举字面值（必写）、tags＝按序（空＝显式 <c>[]</c>；规范字段序：rarity → tags）。</summary>
    public static void Write(TagDataDefinition definition, Utf8JsonWriter writer, IList<string> warnings)
    {
        writer.WriteString("rarity", definition.Rarity.ToString());
        ComponentText.WriteStringArray(writer, "tags", definition.Tags);
    }
}

/// <summary>
/// 类别与单位类型组件定义（数据体组件名 <c>typeCategory</c>；相位＝构造期）：
/// <c>type</c> 列表拆解（P10）——类别词 <c>order</c>→Command、<c>countermeasure</c>→Counter（至多一个、须独占列表）；
/// 单位类型词（<c>infantry</c>/<c>tank</c>/<c>artillery</c>/<c>fighter</c>/<c>bomber</c>）可多个；空列表＝拒绝；未定义词＝拒绝（装载链隔离）。
/// </summary>
public sealed class TypeCategoryDefinition : ICardDataComponentDefinition
{
    /// <summary>数据体组件类型名。</summary>
    public const string DefinitionName = "typeCategory";

    /// <summary>解析出的卡牌类别。</summary>
    public CardCategory Category { get; }

    /// <summary>解析出的单位类型清单（登记序；非单位卡＝空）。</summary>
    public IReadOnlyList<UnitType> UnitTypes { get; }

    internal TypeCategoryDefinition(CardCategory category, IReadOnlyList<UnitType> unitTypes)
    {
        Category = category;
        UnitTypes = unitTypes;
    }

    /// <inheritdoc />
    public string ComponentName => DefinitionName;

    /// <summary>反序列化（<c>type</c> 必填非空数组；拆解规则见类型注释）。</summary>
    public static TypeCategoryDefinition Read(JsonElement element)
    {
        var tokens = ComponentText.GetStringArray(element, "type");
        if (tokens.Count == 0)
        {
            throw new FormatException(
                "typeCategory 组件缺少 type（必填、非空数组——无法确定卡牌类别）。");
        }

        CardCategory? category = null;
        var unitTypes = new List<UnitType>();

        foreach (var token in tokens)
        {
            var resolved = ResolveCategory(token);
            if (resolved is not null)
            {
                if (category is not null)
                {
                    throw new FormatException($"type 列表含多个类别词（'{token}'——类别至多一个）。");
                }

                category = resolved.Value;
                continue;
            }

            var unitType = ResolveUnitType(token)
                ?? throw new FormatException($"type 列表含未定义词 '{token}'（配置错误）。");

            if (unitTypes.Contains(unitType))
            {
                throw new FormatException($"type 列表含重复单位类型 '{token}'。");
            }

            unitTypes.Add(unitType);
        }

        if (category is not null)
        {
            if (tokens.Count != 1 || unitTypes.Count > 0)
            {
                throw new FormatException("类别词须独占 type 列表（不可与单位类型混用）。");
            }

            return new TypeCategoryDefinition(category.Value, Array.Empty<UnitType>());
        }

        return new TypeCategoryDefinition(CardCategory.Unit, unitTypes);
    }

    /// <summary>类别反向词：指令＝<c>order</c>（读侧 <see cref="ResolveCategory"/> 的对偶）。</summary>
    private const string CommandWord = "order";

    /// <summary>类别反向词：反制＝<c>countermeasure</c>。</summary>
    private const string CounterWord = "countermeasure";

    /// <summary>
    /// 序列化（写方向对称；类别/单位类型反向词：Command→<c>order</c>、Counter→<c>countermeasure</c>、
    /// 单位类型→官方小写词）：写序＝类别词在前、单位类型按登记序。
    /// 三类「读不回」组合（单位卡缺单位类型／类别词与单位类型混用／单位类型重复）＝警告＋照写
    /// （保真意图、不静默、不降级、不伪造——读回断裂由再读侧揭示）。
    /// </summary>
    public static void Write(TypeCategoryDefinition definition, Utf8JsonWriter writer, IList<string> warnings)
    {
        var words = new List<string>();

        if (definition.Category == CardCategory.Command)
        {
            words.Add(CommandWord);
        }
        else if (definition.Category == CardCategory.Counter)
        {
            words.Add(CounterWord);
        }

        if (definition.Category is not CardCategory.Unit && definition.UnitTypes.Count > 0)
        {
            warnings.Add("组件 'typeCategory' 类别词与单位类型混用（读回必然被拒——已照写）。");
        }

        var seen = new HashSet<UnitType>();
        var hasDuplicate = false;
        foreach (var unitType in definition.UnitTypes)
        {
            if (!seen.Add(unitType))
            {
                hasDuplicate = true;
            }

            words.Add(unitType.ToString().ToLowerInvariant());
        }

        if (definition.Category is CardCategory.Unit && definition.UnitTypes.Count == 0)
        {
            warnings.Add("组件 'typeCategory' 单位卡缺少单位类型（读回必然被拒——已照写）。");
        }

        if (hasDuplicate)
        {
            warnings.Add("组件 'typeCategory' 含重复单位类型（读回必然被拒——已照写）。");
        }

        ComponentText.WriteStringArray(writer, "type", words);
    }

    private static CardCategory? ResolveCategory(string token)
        => token.ToLowerInvariant() switch
        {
            "order" => CardCategory.Command,
            "countermeasure" => CardCategory.Counter,
            _ => null,
        };

    private static UnitType? ResolveUnitType(string token)
    {
        if (!Enum.TryParse<UnitType>(token, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return null;
        }

        return parsed;
    }
}

/// <summary>
/// 词条组件定义（数据体组件名 <c>keywords</c>；相位＝加载期）：
/// <c>attributes</c> 为**英文标识**数组（官方语义）；经映射表转项目标识（P9a）——命中＝进 <see cref="Keywords"/>，
/// 未命中（含官方未实现项）＝进 <see cref="UnmappedAttributes"/> 留痕（不 fail-fast、不写日志——P9b＝b1）。
/// S1 加性（老兵数据映射转正式承载）：`BecomesVeteran:&lt;老兵卡id&gt;` → <see cref="BecomesVeteran"/> 端口；
/// `VeteranOf:&lt;基础卡id&gt;` → <see cref="VeteranOf"/> 端口＋「老兵」标记进 <see cref="Keywords"/>
/// （随加载/授予落地到实例——内容即真源）。承载判定＝**唯一合法条**：该前缀恰一条且后缀非空白＝正式承载；
/// 畸形（空 id）与多重声明＝按既有「留痕」口径归 <see cref="UnmappedAttributes"/>（不 fail-fast、不阻断加载）。
/// </summary>
public sealed class KeywordsDefinition : ICardDataComponentDefinition
{
    /// <summary>数据体组件类型名。</summary>
    public const string DefinitionName = "keywords";

    /// <summary>数据体原始标识（保真保留）。</summary>
    public IReadOnlyList<string> Attributes { get; }

    /// <summary>映射成功且已注册的词条声明（登记序；含由 <c>VeteranOf:</c> 正式承载产出的「老兵」标记）。</summary>
    public IReadOnlyList<KeywordDeclaration> Keywords { get; }

    /// <summary>未映射/未实现项（只读留痕面；含畸形/多重老兵声明）。</summary>
    public IReadOnlyList<string> UnmappedAttributes { get; }

    /// <summary>
    /// 升级链信息（S1 正式承载；基础卡声明 <c>BecomesVeteran:&lt;老兵卡id&gt;</c>——唯一合法条承载，
    /// 畸形/多重＝留痕不进本面；未声明＝null）。供升级执行查找老兵版本定义（**不作**「是否老兵」判据）。
    /// </summary>
    public string? BecomesVeteran { get; }

    /// <summary>
    /// 老兵来源（S1 正式承载；老兵卡声明 <c>VeteranOf:&lt;基础卡id&gt;</c>——唯一合法条承载，
    /// 畸形/多重＝留痕不进本面；未声明＝null）。与「老兵」标记（<see cref="KeywordIds.Veteran"/>——随本映射
    /// 一并产出、随内容落地）为同一承载体的两侧：实例读面以标记为准、本端口供升级链校验（防双真源）。
    /// </summary>
    public string? VeteranOf { get; }

    internal KeywordsDefinition(
        IReadOnlyList<string> attributes,
        IReadOnlyList<KeywordDeclaration> keywords,
        IReadOnlyList<string> unmapped,
        string? becomesVeteran = null,
        string? veteranOf = null)
    {
        Attributes = attributes;
        Keywords = keywords;
        UnmappedAttributes = unmapped;
        BecomesVeteran = becomesVeteran;
        VeteranOf = veteranOf;
    }

    /// <inheritdoc />
    public string ComponentName => DefinitionName;

    /// <summary>反序列化（<c>attributes</c> 可选字符串数组；映射与分流见类型注释）。</summary>
    public static KeywordsDefinition Read(JsonElement element)
    {
        var attributes = ComponentText.GetStringArray(element, "attributes");

        // 预扫描（S1）：老兵数据映射条数与候选值——承载判定＝「该前缀恰一条且后缀非空白」（唯一合法条）。
        var becomesTotal = 0;
        string? becomesCandidate = null;
        var veteranTotal = 0;
        string? veteranCandidate = null;
        foreach (var attribute in attributes)
        {
            if (!CardAttributeMap.TryMapVeteran(attribute, out var isBecomesVeteran, out var cardId))
            {
                continue;
            }

            if (isBecomesVeteran)
            {
                becomesTotal++;
                if (becomesTotal == 1)
                {
                    becomesCandidate = cardId;
                }
            }
            else
            {
                veteranTotal++;
                if (veteranTotal == 1)
                {
                    veteranCandidate = cardId;
                }
            }
        }

        var becomesVeteran = becomesTotal == 1 && !string.IsNullOrWhiteSpace(becomesCandidate)
            ? becomesCandidate
            : null;
        var veteranOf = veteranTotal == 1 && !string.IsNullOrWhiteSpace(veteranCandidate)
            ? veteranCandidate
            : null;

        var keywords = new List<KeywordDeclaration>();
        var unmapped = new List<string>();

        foreach (var attribute in attributes)
        {
            if (CardAttributeMap.TryMapVeteran(attribute, out var isBecomesVeteran, out var cardId))
            {
                // S1 分流：唯一合法条＝正式承载；畸形/多重＝留痕（不 fail-fast）。
                var formallyCarried = isBecomesVeteran
                    ? becomesVeteran is not null && string.Equals(cardId, becomesVeteran, StringComparison.Ordinal)
                    : veteranOf is not null && string.Equals(cardId, veteranOf, StringComparison.Ordinal);
                if (!formallyCarried)
                {
                    unmapped.Add(attribute);
                    continue;
                }

                if (!isBecomesVeteran)
                {
                    // VeteranOf 正式承载：产出「老兵」标记（随内容落地——单一真源）。
                    if (keywords.Any(item => string.Equals(item.Id, KeywordIds.Veteran, StringComparison.Ordinal)))
                    {
                        unmapped.Add(attribute); // 同标识重复：留痕（与常规词条映射同口径）
                        continue;
                    }

                    keywords.Add(new KeywordDeclaration(KeywordIds.Veteran));
                }

                continue; // BecomesVeteran：升级链信息（静态端口），不进词条声明
            }

            if (!CardAttributeMap.TryMap(attribute, out var declaration)
                || !KeywordRegistry.IsDefined(declaration.Id)
                || keywords.Any(item => string.Equals(item.Id, declaration.Id, StringComparison.Ordinal)))
            {
                unmapped.Add(attribute); // 未映射 / 未实现 / 同标识重复：留痕（不 fail-fast）
                continue;
            }

            keywords.Add(declaration);
        }

        return new KeywordsDefinition(attributes, keywords, unmapped, becomesVeteran, veteranOf);
    }

    /// <summary>
    /// 序列化（写方向对称；策略＝<see cref="Attributes"/> 保真优先）：
    /// ①保真路径（Attributes 非空）＝原始英文标识全量按序原样写回——含未映射/畸形/多重老兵声明原文，
    /// 保真即无损失（不比对、不警告；以 Attributes 为唯一真源，派生面不一致态同样按 Attributes 输出）；
    /// ②合成路径（Attributes 空＝代码构造卡）＝老兵端口在前 → 词条经反向映射/老兵端口生成（VeteranOf 端口
    /// 吸收「老兵」标记）→ 留痕原文在后；不可还原项（无反向映射／参值不可表达／裸标记／端口 id 空白）
    /// ＝输出警告、跳过该项（不静默丢失）；
    /// ③空产出＝写空 <c>attributes</c> 数组（组件在场空态——保结构等值）。
    /// </summary>
    public static void Write(KeywordsDefinition definition, Utf8JsonWriter writer, IList<string> warnings)
    {
        if (definition.Attributes.Count > 0)
        {
            // 保真路径：全量按序原样（含畸形/多重原文——读回后仍按既有口径归留痕，语义等值）。
            ComponentText.WriteStringArray(writer, "attributes", definition.Attributes);
            return;
        }

        // 合成路径：端口在前、留痕在后（输出确定性——同输入同输出）。
        var attributes = new List<string>();

        if (definition.BecomesVeteran is not null)
        {
            if (string.IsNullOrWhiteSpace(definition.BecomesVeteran))
            {
                warnings.Add("组件 'keywords' 项 'BecomesVeteran' 的卡 id 空白（不可还原——已跳过）。");
            }
            else
            {
                attributes.Add(CardAttributeMap.BecomesVeteranPrefix + definition.BecomesVeteran);
            }
        }

        var veteranOfCarried = false;
        if (definition.VeteranOf is not null)
        {
            if (string.IsNullOrWhiteSpace(definition.VeteranOf))
            {
                warnings.Add("组件 'keywords' 项 'VeteranOf' 的卡 id 空白（不可还原——已跳过）。");
            }
            else
            {
                attributes.Add(CardAttributeMap.VeteranOfPrefix + definition.VeteranOf);
                veteranOfCarried = true;
            }
        }

        foreach (var keyword in definition.Keywords)
        {
            if (string.Equals(keyword.Id, KeywordIds.Veteran, StringComparison.Ordinal))
            {
                if (veteranOfCarried)
                {
                    // 端口吸收标记（单一真源的对偶——不再单独处理、不警告）。
                    continue;
                }

                warnings.Add(
                    $"组件 'keywords' 项 '{KeywordIds.Veteran}' 无反向映射（裸「老兵」标记无 VeteranOf 端口——不可还原，已跳过）。");
                continue;
            }

            if (keyword.Value is null)
            {
                if (CardAttributeMap.IsValuedKeyword(keyword.Id))
                {
                    warnings.Add(
                        $"组件 'keywords' 项 '{keyword.Id}' 参值不可表达（参值型须为正整数，实际缺失——不可还原，已跳过）。");
                    continue;
                }

                if (CardAttributeMap.TryGetExactAttribute(keyword.Id, out var exact))
                {
                    attributes.Add(exact);
                    continue;
                }

                warnings.Add($"组件 'keywords' 项 '{keyword.Id}' 无反向映射（不可还原，已跳过）。");
                continue;
            }

            var value = keyword.Value.Value;
            if (CardAttributeMap.IsValuedKeyword(keyword.Id))
            {
                if (CardAttributeMap.TryGetValuedAttribute(keyword.Id, value, out var valued))
                {
                    attributes.Add(valued);
                    continue;
                }

                warnings.Add(
                    $"组件 'keywords' 项 '{keyword.Id}'（值 {value}）参值不可表达（参值型须为正整数——不可还原，已跳过）。");
                continue;
            }

            if (CardAttributeMap.TryGetExactAttribute(keyword.Id, out _))
            {
                warnings.Add(
                    $"组件 'keywords' 项 '{keyword.Id}'（值 {value}）参值不可表达（该标识无值位——不可还原，已跳过）。");
                continue;
            }

            warnings.Add($"组件 'keywords' 项 '{keyword.Id}' 无反向映射（不可还原，已跳过）。");
        }

        // 留痕＝可还原原文（非静默丢失留痕项）：原样补入合成输出、不落警告。
        foreach (var unmapped in definition.UnmappedAttributes)
        {
            attributes.Add(unmapped);
        }

        ComponentText.WriteStringArray(writer, "attributes", attributes);
    }
}

/// <summary>
/// 效果组件定义（数据体组件名 <c>effects</c>；相位＝加载期）：
/// <c>prefabs</c> 为效果预制体 id 引用清单（内联形态由载入器先行注册并取出 id——P6＝a）。
/// 实际装载走既有框架步骤（<c>CardEffectLoader</c>）——载入器在卡包载入期把本清单转为
/// <c>CardEffectRegistry.DeclarePrefab</c> 注册项（Q17：生成既有注册项）。
/// </summary>
public sealed class EffectsDefinition : ICardDataComponentDefinition
{
    /// <summary>数据体组件类型名。</summary>
    public const string DefinitionName = "effects";

    /// <summary>效果预制体 id 引用清单（声明序＝装载序）。</summary>
    public IReadOnlyList<string> Prefabs { get; }

    /// <summary>内联效果预制体（S8；Q24＝c——单文件自包含、随卡分发；载入期注册进预制体注册面）。</summary>
    public IReadOnlyList<Orc.Cards.EffectSnapshot> Inline { get; }

    internal EffectsDefinition(
        IReadOnlyList<string> prefabs,
        IReadOnlyList<Orc.Cards.EffectSnapshot>? inline = null)
    {
        Prefabs = prefabs;
        Inline = inline ?? Array.Empty<Orc.Cards.EffectSnapshot>();
    }

    /// <inheritdoc />
    public string ComponentName => DefinitionName;

    /// <summary>反序列化（<c>prefabs</c> 可选字符串数组；<c>inline</c> 可选内联预制体数组——版本/结构校验失败＝隔离）。</summary>
    public static EffectsDefinition Read(JsonElement element)
    {
        var inline = new List<Orc.Cards.EffectSnapshot>();
        if (ComponentText.TryGetProperty(element, "inline", out var inlineElement)
            && inlineElement.ValueKind != JsonValueKind.Null)
        {
            if (inlineElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("effects 组件字段 'inline' 须为数组。");
            }

            foreach (var item in inlineElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object && item.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("effects 组件 inline 项须为对象（或 JSON 文本）。");
                }

                var text = item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText();
                inline.Add(Orc.Cards.PrefabJson.Deserialize(text));
            }
        }

        return new EffectsDefinition(ComponentText.GetStringArray(element, "prefabs"), inline);
    }

    /// <summary>
    /// 序列化（写方向对称；默认策略）：prefabs 引用原样按序 → inline 对象态按序（经 <c>PrefabJson</c> 同版本
    /// v2 序列化后以对象嵌入——读宽写窄：字符串态读入统一写为对象态）；两区分组保序；空＝显式 <c>[]</c>。
    /// </summary>
    public static void Write(EffectsDefinition definition, Utf8JsonWriter writer, IList<string> warnings)
    {
        ComponentText.WriteStringArray(writer, "prefabs", definition.Prefabs);

        writer.WriteStartArray("inline");
        foreach (var snapshot in definition.Inline)
        {
            using var document = JsonDocument.Parse(Orc.Cards.PrefabJson.Serialize(snapshot));
            document.RootElement.WriteTo(writer);
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// 内置组件注册（S3；注册序即第一段加载顺序）：typeCategory → factionCost → battleStats → tagData → keywords → effects。
/// 注册先于使用（对齐 <see cref="KeywordRegistry"/> 先例）；由 <see cref="CardComponentRegistry"/> 静态构造一次性执行。
/// </summary>
internal static class BuiltInCardComponents
{
    /// <summary>注册六个内置组件（重复调用＝注册面拒绝——本方法仅由注册面静态构造调用一次）。</summary>
    internal static void Register()
    {
        // 构造期段（顺序：类别 → 阵营/花费 → 对战数值）。
        CardComponentRegistry.Register(
            TypeCategoryDefinition.DefinitionName,
            CardComponentPhase.Construction,
            static (_, _, _) => Task.CompletedTask, // 空操作：数据经 CardDefinition 派生读面消费（P4c）
            TypeCategoryDefinition.Read,
            isBuiltIn: true,
            writer: TypeCategoryDefinition.Write);

        CardComponentRegistry.Register(
            FactionCostDefinition.DefinitionName,
            CardComponentPhase.Construction,
            static (card, definition, _) =>
            {
                var typed = (FactionCostDefinition)definition;
                card.AddData(new FactionCostData(typed.Faction, typed.DeployCost));
                return Task.CompletedTask;
            },
            FactionCostDefinition.Read,
            isBuiltIn: true,
            writer: FactionCostDefinition.Write);

        CardComponentRegistry.Register(
            BattleStatsDefinition.DefinitionName,
            CardComponentPhase.Construction,
            static (card, definition, _) =>
            {
                var typed = (BattleStatsDefinition)definition;
                card.AddData(new BattleStatsData(typed.OperateCost, typed.Attack, typed.Defense));
                return Task.CompletedTask;
            },
            BattleStatsDefinition.Read,
            isBuiltIn: true,
            writer: BattleStatsDefinition.Write);

        // 加载期段（顺序：元数据 → 词条 → 效果）。
        CardComponentRegistry.Register(
            TagDataDefinition.DefinitionName,
            CardComponentPhase.Load,
            static (card, definition, _) =>
            {
                var typed = (TagDataDefinition)definition;
                var data = new TagData(typed.Rarity);
                foreach (var tag in typed.Tags)
                {
                    data.AddTag(tag);
                }

                card.AddData(data);
                return Task.CompletedTask;
            },
            TagDataDefinition.Read,
            isBuiltIn: true,
            writer: TagDataDefinition.Write);

        CardComponentRegistry.Register(
            KeywordsDefinition.DefinitionName,
            CardComponentPhase.Load,
            static (card, definition, context) =>
            {
                var typed = (KeywordsDefinition)definition;
                var loadContext = context.KeywordLoadContext;
                foreach (var declaration in typed.Keywords)
                {
                    card.Keywords.GrantCore(declaration.Id, declaration.Value, loadContext);
                }

                return Task.CompletedTask;
            },
            KeywordsDefinition.Read,
            isBuiltIn: true,
            writer: KeywordsDefinition.Write);

        CardComponentRegistry.Register(
            EffectsDefinition.DefinitionName,
            CardComponentPhase.Load,
            static (_, _, _) => Task.CompletedTask, // 空操作：声明已在载入期转为效果注册项，装载走框架步骤
            EffectsDefinition.Read,
            isBuiltIn: true,
            writer: EffectsDefinition.Write);
    }
}
