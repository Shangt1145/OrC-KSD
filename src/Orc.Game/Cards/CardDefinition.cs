namespace Orc.Game.Cards;

/// <summary>
/// 卡牌定义（代码注册形态）：类别（单位/指令/反制）＋名称＋四项基础数值（部署费 / 行动费 / 攻击力 / 防御力）；
/// id 由注册键携带（定义自身不含 id，避免冗余与不一致）。
/// 名称不可为 null/空白（注册即配置、fail-fast）；类别须为已定义值（配置错误在定义期被拒绝）；
/// 数值域校验后置（负数等，本批不做）。
/// 类别缺省＝单位（兼容既有构造调用；三类卡的区分经显式指定类别）。
/// 〔2C 加性受控变更〕新增三个静态声明字段（定义＝单一真源、不可运行时增删）：
/// ①词条清单（加载时逐条登记至 KeywordData ＋装载主动词条逻辑；未实现标识/重复项＝定义期明确错误 fail-fast——本批合法仅四枚）；
/// ②单位类型清单（单位化时填充 UnitStateData.UnitTypes——部署/加入两路径一致；未声明＝空列表＝零类型·行为按基线〔视同步兵〕；重复项 fail-fast）；
/// ③守护者标记（守护机制：相邻单位获「被守护」——需求空白点的实现裁决，交付汇报标注）。
/// 〔W1-1 G12 加性受控变更〕再加两维卡牌元数据（定义＝单一真源、不可运行时增删）：
/// ④必填强类型槽位（国籍 / 稀有度——新定义卡须显式提供，缺失/未定义枚举值＝定义期 fail-fast 拒绝；全类别必填）；
/// ⑤开放 tag 清单（子类别标记：海军/T-34/谢尔曼等——纯分类、不承载机制行为；加载时装配到实例 TagData）。
/// </summary>
public sealed class CardDefinition
{
    /// <summary>创建定义。</summary>
    /// <param name="name">卡牌名称（非 null/空白）。</param>
    /// <param name="deployCost">部署费。</param>
    /// <param name="operateCost">行动费。</param>
    /// <param name="attack">攻击力。</param>
    /// <param name="defense">防御力。</param>
    /// <param name="category">卡牌类别（缺省＝单位）。</param>
    /// <param name="keywords">词条清单（2C 加性；可缺省＝无词条；标识须为已实现词条、不得重复——未实现/重复＝定义期拒绝）。</param>
    /// <param name="unitTypes">单位类型清单（2C 加性；可缺省＝空列表＝零类型；枚举值须已定义、不得重复——重复＝定义期拒绝）。</param>
    /// <param name="isGuard">守护者标记（2C 加性；缺省＝false；守护机制的来源标记——相邻单位/HQ 获「被守护」）。</param>
    /// <param name="faction">国籍（W1-1 加性、必填强类型槽位：新定义卡须显式提供；缺失＝定义期 fail-fast 拒绝、无可用默认值）。</param>
    /// <param name="rarity">稀有度（W1-1 加性、必填强类型槽位：新定义卡须显式提供；缺失＝定义期 fail-fast 拒绝、无可用默认值）。</param>
    /// <param name="tags">开放 tag 清单（W1-1 加性；可缺省＝空列表＝无开放 tag；null 元素/空白/重复项＝定义期拒绝）。</param>
    /// <exception cref="ArgumentException">name 为 null/空白；国籍或稀有度缺失（未提供/为 null）；词条清单含 null/空白/未实现标识/重复项；单位类型清单含重复项；开放 tag 清单含 null/空白/重复项。</exception>
    /// <exception cref="ArgumentOutOfRangeException">category 为未定义的卡牌类别；国籍/稀有度为未定义枚举值；单位类型含未定义枚举值。</exception>
    public CardDefinition(
        string name,
        int deployCost,
        int operateCost,
        int attack,
        int defense,
        CardCategory category = CardCategory.Unit,
        IEnumerable<string>? keywords = null,
        IEnumerable<UnitType>? unitTypes = null,
        bool isGuard = false,
        Faction? faction = null,
        Rarity? rarity = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "未定义的卡牌类别（配置错误在定义期被拒绝）。");
        }

        // W1-1：必填强类型槽位（国籍 / 稀有度）——缺失＝定义期 fail-fast 拒绝（不设可用默认值，「省略即默认」不成立）；
        // 未定义枚举值＝拒绝（沿用既有 Enum 校验风格）。
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

        Name = name;
        DeployCost = deployCost;
        OperateCost = operateCost;
        Attack = attack;
        Defense = defense;
        Category = category;
        Keywords = ResolveKeywords(keywords);
        UnitTypes = ResolveUnitTypes(unitTypes);
        IsGuard = isGuard;
        Faction = faction.Value;
        Rarity = rarity.Value;
        Tags = ResolveTags(tags);
    }

    /// <summary>词条清单校验与拷贝（fail-fast：null 元素 / null 或空白标识 / 未实现标识 / 重复项均被拒绝；登记序保留）。</summary>
    private static IReadOnlyList<string> ResolveKeywords(IEnumerable<string>? keywords)
    {
        if (keywords is null)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                throw new ArgumentException("词条清单含 null/空白标识（配置错误在定义期被拒绝）。", nameof(keywords));
            }

            if (!KeywordIds.IsDefined(keyword))
            {
                throw new ArgumentException(
                    $"词条清单含未实现标识 '{keyword}'（本批合法标识仅：{string.Join(" / ", KeywordIds.All)}；fail-fast）。",
                    nameof(keywords));
            }

            if (list.Contains(keyword))
            {
                throw new ArgumentException(
                    $"词条清单含重复项 '{keyword}'（重复＝定义声明的明确错误——非静默去重、非保留原样，与 hooks 声明先例一致）。",
                    nameof(keywords));
            }

            list.Add(keyword);
        }

        return list.ToArray();
    }

    /// <summary>单位类型清单校验与拷贝（fail-fast：null 元素 / 未定义枚举值 / 重复项均被拒绝；登记序保留）。</summary>
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

    /// <summary>开放 tag 清单校验与拷贝（W1-1 fail-fast：null 元素 / null 或空白值 / 重复项均被拒绝；登记序保留）。</summary>
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

    /// <summary>卡牌名称（实例化时取用）。</summary>
    public string Name { get; }

    /// <summary>卡牌类别（单位 / 指令 / 反制；实例化按此产出对应基类实例）。</summary>
    public CardCategory Category { get; }

    /// <summary>部署费初始值。</summary>
    public int DeployCost { get; }

    /// <summary>行动费初始值。</summary>
    public int OperateCost { get; }

    /// <summary>攻击力初始值。</summary>
    public int Attack { get; }

    /// <summary>防御力初始值（＝HP）。</summary>
    public int Defense { get; }

    /// <summary>词条清单（2C 加性；固定字面值之一、登记序；空列表＝无词条——加载时无副作用）。</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>单位类型清单（2C 加性；登记序；空列表＝零类型——行为按基线〔视同步兵〕；单位化时填充）。</summary>
    public IReadOnlyList<UnitType> UnitTypes { get; }

    /// <summary>守护者标记（2C 加性；true＝守护者——相邻单位/HQ 获「被守护」；守护者自身不可被守护）。</summary>
    public bool IsGuard { get; }

    /// <summary>国籍（W1-1 必填槽位终值；11 值全量域；实例侧只读——本批不支持运行时修改）。</summary>
    public Faction Faction { get; }

    /// <summary>稀有度（W1-1 必填槽位终值：Standard/Limited/Special/Elite；实例侧只读——本批不支持运行时修改）。</summary>
    public Rarity Rarity { get; }

    /// <summary>开放 tag 清单（W1-1 加性；登记序；空列表＝无开放 tag——加载时装配为空集合、无副作用）。</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>卡牌定义集条目：id（卡牌库注册键）＋定义。</summary>
public sealed record CardDefinitionEntry(string Id, CardDefinition Definition);
