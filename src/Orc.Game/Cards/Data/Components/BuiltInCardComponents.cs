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
/// </summary>
public sealed class KeywordsDefinition : ICardDataComponentDefinition
{
    /// <summary>数据体组件类型名。</summary>
    public const string DefinitionName = "keywords";

    /// <summary>数据体原始标识（保真保留）。</summary>
    public IReadOnlyList<string> Attributes { get; }

    /// <summary>映射成功且已注册的词条声明（登记序）。</summary>
    public IReadOnlyList<KeywordDeclaration> Keywords { get; }

    /// <summary>未映射/未实现项（只读留痕面）。</summary>
    public IReadOnlyList<string> UnmappedAttributes { get; }

    internal KeywordsDefinition(
        IReadOnlyList<string> attributes,
        IReadOnlyList<KeywordDeclaration> keywords,
        IReadOnlyList<string> unmapped)
    {
        Attributes = attributes;
        Keywords = keywords;
        UnmappedAttributes = unmapped;
    }

    /// <inheritdoc />
    public string ComponentName => DefinitionName;

    /// <summary>反序列化（<c>attributes</c> 可选字符串数组；映射与分流见类型注释）。</summary>
    public static KeywordsDefinition Read(JsonElement element)
    {
        var attributes = ComponentText.GetStringArray(element, "attributes");
        var keywords = new List<KeywordDeclaration>();
        var unmapped = new List<string>();

        foreach (var attribute in attributes)
        {
            if (!CardAttributeMap.TryMap(attribute, out var declaration)
                || !KeywordRegistry.IsDefined(declaration.Id)
                || keywords.Any(item => string.Equals(item.Id, declaration.Id, StringComparison.Ordinal)))
            {
                unmapped.Add(attribute); // 未映射 / 未实现 / 同标识重复：留痕（不 fail-fast）
                continue;
            }

            keywords.Add(declaration);
        }

        return new KeywordsDefinition(attributes, keywords, unmapped);
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
            isBuiltIn: true);

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
            isBuiltIn: true);

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
            isBuiltIn: true);

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
            isBuiltIn: true);

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
            isBuiltIn: true);

        CardComponentRegistry.Register(
            EffectsDefinition.DefinitionName,
            CardComponentPhase.Load,
            static (_, _, _) => Task.CompletedTask, // 空操作：声明已在载入期转为效果注册项，装载走框架步骤
            EffectsDefinition.Read,
            isBuiltIn: true);
    }
}
