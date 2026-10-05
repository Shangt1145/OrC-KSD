using Orc.Game.Cards.Data;
using Orc.Game.Cards.Data.Components;

namespace Orc.Game.Cards;

/// <summary>
/// 卡牌定义（P2＝A 改造后形态）：**组件定义集为唯一真源** ＋ 兼容读面。
/// ①主构造＝"名称 ＋ 组件定义集"（数据体路径：<c>CardDataBody</c> 的组件逐步转换而来）；
/// ②兼容构造＝既有 12 参数（代码注册路径，构造时转换为组件定义集）——既有调用与测试零改动。
/// 读面（<see cref="Faction"/>/<see cref="DeployCost"/>/<see cref="OperateCost"/>/<see cref="Attack"/>/<see cref="Defense"/>/
/// <see cref="Rarity"/>/<see cref="Tags"/>/<see cref="Keywords"/>/<see cref="UnitTypes"/>/<see cref="Category"/>）保留同名同义、内部从组件定义派生。
/// 必填槽位（国籍 / 稀有度）缺失＝定义期 fail-fast 拒绝（<c>factionCost</c> / <c>tagData</c> 组件必需）。
/// 效果声明不入本类型（代码注册路径走 <c>CardEffectRegistry</c>；数据体路径在卡包载入期转为其注册项）。
/// </summary>
public sealed class CardDefinition
{
    /// <summary>创建定义（主构造：名称 ＋ 组件定义集）。</summary>
    /// <param name="name">卡牌名称（非 null/空白）。</param>
    /// <param name="components">组件定义集（每类型至多一份；须含 <c>factionCost</c> 与 <c>tagData</c>）。</param>
    /// <param name="isGuard">守护者标记（代码注册路径加性面；数据体路径恒 false——官方 <c>guard</c> 走未实现留痕）。</param>
    /// <exception cref="ArgumentException">name 为 null/空白；components 含 null/重复类型；缺 factionCost/tagData。</exception>
    /// <exception cref="ArgumentOutOfRangeException">国籍/稀有度为未定义枚举值。</exception>
    public CardDefinition(string name, IEnumerable<ICardDataComponentDefinition> components, bool isGuard = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(components);

        var list = new List<ICardDataComponentDefinition>();
        var seen = new HashSet<Type>();
        foreach (var item in components)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (!seen.Add(item.GetType()))
            {
                throw new ArgumentException(
                    $"卡牌定义含重复组件 '{item.GetType().Name}'（每类型至多一份——拒绝）。", nameof(components));
            }

            list.Add(item);
        }

        var factionCost = Find<FactionCostDefinition>(list)
            ?? throw new ArgumentException(
                "卡牌定义缺少 factionCost 组件（国籍与部署费为必填槽位——缺失＝定义期 fail-fast 拒绝）。",
                nameof(components));

        var tagData = Find<TagDataDefinition>(list)
            ?? throw new ArgumentException(
                "卡牌定义缺少 tagData 组件（稀有度为必填槽位——缺失＝定义期 fail-fast 拒绝）。",
                nameof(components));

        if (!Enum.IsDefined(factionCost.Faction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(components), factionCost.Faction, "国籍为未定义枚举值（配置错误在定义期被拒绝）。");
        }

        if (!Enum.IsDefined(tagData.Rarity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(components), tagData.Rarity, "稀有度为未定义枚举值（配置错误在定义期被拒绝）。");
        }

        var typeCategory = Find<TypeCategoryDefinition>(list);
        var battleStats = Find<BattleStatsDefinition>(list);
        var keywords = Find<KeywordsDefinition>(list);

        Name = name;
        Components = list.ToArray();
        IsGuard = isGuard;

        Faction = factionCost.Faction;
        DeployCost = factionCost.DeployCost;
        Rarity = tagData.Rarity;
        Tags = tagData.Tags;

        Category = typeCategory?.Category ?? CardCategory.Unit;
        UnitTypes = typeCategory?.UnitTypes ?? Array.Empty<UnitType>();

        OperateCost = battleStats?.OperateCost ?? 0;
        Attack = battleStats?.Attack ?? 0;
        Defense = battleStats?.Defense ?? 0;

        Attributes = keywords?.Attributes ?? Array.Empty<string>();
        Keywords = keywords?.Keywords ?? Array.Empty<KeywordDeclaration>();
        UnmappedAttributes = keywords?.UnmappedAttributes ?? Array.Empty<string>();
    }

    /// <summary>
    /// 创建定义（兼容构造：既有 12 参数；内部转换为组件定义集——校验与既有行为一致、fail-fast 保留）。
    /// </summary>
    /// <exception cref="ArgumentException">name 为 null/空白；国籍或稀有度缺失；词条清单含空白/未注册/重复标识；单位类型或开放 tag 含重复项。</exception>
    /// <exception cref="ArgumentOutOfRangeException">category 为未定义值；国籍/稀有度/单位类型为未定义枚举值。</exception>
    public CardDefinition(
        string name,
        int deployCost,
        int operateCost,
        int attack,
        int defense,
        CardCategory category = CardCategory.Unit,
        IEnumerable<KeywordDeclaration>? keywords = null,
        IEnumerable<UnitType>? unitTypes = null,
        bool isGuard = false,
        Faction? faction = null,
        Rarity? rarity = null,
        IEnumerable<string>? tags = null)
        : this(
            name,
            BuildComponents(category, deployCost, operateCost, attack, defense, keywords, unitTypes, faction, rarity, tags),
            isGuard)
    {
    }

    /// <summary>兼容构造 → 组件定义集（校验先于构造：配置错误在此明确拒绝）。</summary>
    private static IReadOnlyList<ICardDataComponentDefinition> BuildComponents(
        CardCategory category,
        int deployCost,
        int operateCost,
        int attack,
        int defense,
        IEnumerable<KeywordDeclaration>? keywords,
        IEnumerable<UnitType>? unitTypes,
        Faction? faction,
        Rarity? rarity,
        IEnumerable<string>? tags)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "未定义的卡牌类别（配置错误在定义期被拒绝）。");
        }

        if (faction is null)
        {
            throw new ArgumentException(
                "未提供国籍（国籍为必填槽位——新定义卡必须显式提供；缺失＝定义期 fail-fast 拒绝）。", nameof(faction));
        }

        if (!Enum.IsDefined(faction.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(faction), faction, "国籍为未定义枚举值（配置错误在定义期被拒绝）。");
        }

        if (rarity is null)
        {
            throw new ArgumentException(
                "未提供稀有度（稀有度为必填槽位——新定义卡必须显式提供；缺失＝定义期 fail-fast 拒绝）。", nameof(rarity));
        }

        if (!Enum.IsDefined(rarity.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "稀有度为未定义枚举值（配置错误在定义期被拒绝）。");
        }

        var resolvedKeywords = ResolveKeywords(keywords);
        var resolvedUnitTypes = ResolveUnitTypes(unitTypes);
        var resolvedTags = ResolveTags(tags);

        var list = new List<ICardDataComponentDefinition>
        {
            new TypeCategoryDefinition(category, resolvedUnitTypes),
            new FactionCostDefinition(faction.Value, deployCost),
        };

        if (category == CardCategory.Unit)
        {
            list.Add(new BattleStatsDefinition(operateCost, attack, defense));
        }

        list.Add(new TagDataDefinition(rarity.Value, resolvedTags));

        if (resolvedKeywords.Count > 0)
        {
            list.Add(new KeywordsDefinition(Array.Empty<string>(), resolvedKeywords, Array.Empty<string>()));
        }

        return list;
    }

    /// <summary>词条声明校验与拷贝（fail-fast：空白标识 / 未注册标识 / 同标识重复项均被拒绝；登记序保留）。</summary>
    private static IReadOnlyList<KeywordDeclaration> ResolveKeywords(IEnumerable<KeywordDeclaration>? keywords)
    {
        if (keywords is null)
        {
            return Array.Empty<KeywordDeclaration>();
        }

        var list = new List<KeywordDeclaration>();
        foreach (var declaration in keywords)
        {
            if (string.IsNullOrWhiteSpace(declaration.Id))
            {
                throw new ArgumentException("词条声明清单含 null/空白标识（配置错误在定义期被拒绝）。", nameof(keywords));
            }

            if (!KeywordRegistry.IsDefined(declaration.Id))
            {
                throw new ArgumentException(
                    $"词条声明清单含未实现标识 '{declaration.Id}'（合法标识集＝词条注册面内容：{string.Join(" / ", KeywordRegistry.Registered)}；fail-fast）。",
                    nameof(keywords));
            }

            if (list.Any(item => string.Equals(item.Id, declaration.Id, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"词条声明清单含重复项 '{declaration.Id}'（同标识即重复〔不论参值〕——非静默去重、非保留原样，与 hooks 声明先例一致）。",
                    nameof(keywords));
            }

            list.Add(declaration);
        }

        return list.ToArray();
    }

    /// <summary>单位类型清单校验与拷贝（fail-fast：未定义枚举值 / 重复项均被拒绝；登记序保留）。</summary>
    private static IReadOnlyList<UnitType> ResolveUnitTypes(IEnumerable<UnitType>? unitTypes)
    {
        if (unitTypes is null)
        {
            return Array.Empty<UnitType>();
        }

        var list = new List<UnitType>();
        foreach (var unitType in unitTypes)
        {
            if (!Enum.IsDefined(unitType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unitTypes), unitType, "单位类型清单含未定义枚举值（配置错误在定义期被拒绝）。");
            }

            if (list.Contains(unitType))
            {
                throw new ArgumentException(
                    $"单位类型清单含重复项 '{unitType}'（重复无行为意义——fail-fast，与词条清单同精神）。",
                    nameof(unitTypes));
            }

            list.Add(unitType);
        }

        return list.ToArray();
    }

    /// <summary>开放 tag 清单校验与拷贝（fail-fast：null 元素 / null 或空白值 / 重复项均被拒绝；登记序保留）。</summary>
    private static IReadOnlyList<string> ResolveTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new ArgumentException("开放 tag 清单含 null/空白值（配置错误在定义期被拒绝）。", nameof(tags));
            }

            if (list.Contains(tag))
            {
                throw new ArgumentException(
                    $"开放 tag 清单含重复项 '{tag}'（重复＝定义声明的明确错误——fail-fast，与词条/单位类型先例一致）。",
                    nameof(tags));
            }

            list.Add(tag);
        }

        return list.ToArray();
    }

    private static T? Find<T>(IReadOnlyList<ICardDataComponentDefinition> components)
        where T : class, ICardDataComponentDefinition
    {
        foreach (var item in components)
        {
            if (item is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    /// <summary>卡牌名称（实例化时取用）。</summary>
    public string Name { get; }

    /// <summary>组件定义集（声明序；唯一真源——读面均由此派生）。</summary>
    public IReadOnlyList<ICardDataComponentDefinition> Components { get; }

    /// <summary>卡牌类别（派生自 <c>typeCategory</c> 组件；缺省＝单位）。</summary>
    public CardCategory Category { get; }

    /// <summary>部署费初始值（派生自 <c>factionCost</c> 组件）。</summary>
    public int DeployCost { get; }

    /// <summary>行动费初始值（派生自 <c>battleStats</c> 组件；非单位卡＝0）。</summary>
    public int OperateCost { get; }

    /// <summary>攻击力初始值（派生自 <c>battleStats</c> 组件；非单位卡＝0）。</summary>
    public int Attack { get; }

    /// <summary>防御力初始值（＝HP；派生自 <c>battleStats</c> 组件；非单位卡＝0）。</summary>
    public int Defense { get; }

    /// <summary>词条声明清单（派生自 <c>keywords</c> 组件；空＝无词条）。</summary>
    public IReadOnlyList<KeywordDeclaration> Keywords { get; }

    /// <summary>数据体原始词条标识（保真；代码注册路径＝空）。</summary>
    public IReadOnlyList<string> Attributes { get; }

    /// <summary>未映射/未实现词条标识（只读留痕面；不参与机制、不写日志）。</summary>
    public IReadOnlyList<string> UnmappedAttributes { get; }

    /// <summary>单位类型清单（派生自 <c>typeCategory</c> 组件；登记序；空＝零类型）。</summary>
    public IReadOnlyList<UnitType> UnitTypes { get; }

    /// <summary>守护者标记（代码注册路径加性面；数据体路径恒 false）。</summary>
    public bool IsGuard { get; }

    /// <summary>国籍（派生自 <c>factionCost</c> 组件终值；实例侧只读）。</summary>
    public Faction Faction { get; }

    /// <summary>稀有度（派生自 <c>tagData</c> 组件终值；实例侧只读）。</summary>
    public Rarity Rarity { get; }

    /// <summary>开放 tag 清单（派生自 <c>tagData</c> 组件；登记序；空＝无开放 tag）。</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>卡牌定义集条目：id（卡牌库注册键）＋定义。</summary>
public sealed record CardDefinitionEntry(string Id, CardDefinition Definition);
