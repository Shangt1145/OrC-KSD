namespace Orc.Game.Cards.Data;

/// <summary>
/// 词条标识映射表（P9a＝a1；代码常量、单一真源）：数据体里的**英文标识** → 项目 <see cref="KeywordIds"/> 中文标识。
/// 覆盖两类：①官方语料标识（`blitz`/`fury`/…/`heavyArmor1`）；②Orc 自有标识（`suppressed`/`forecast`/`deathrattle` 等——
/// 官方语料无对应词条，供社区写数据体时使用）。
/// 参值型（P9c＝c1）：显式前缀表（`heavyArmor2` → {重甲, 2}、`intel3` → {情报, 3}）。
/// S1 加性（老兵数据映射转正式承载）：`BecomesVeteran:<老兵卡id>` / `VeteranOf:<基础卡id>` 经
/// <see cref="TryMapVeteran"/> 识别——**不再**归未实现留痕面（原留痕口径随本单退役）；畸形声明（空 id / 多重）
/// 由调用侧（<c>KeywordsDefinition.Read</c>）按既有「留痕」口径承载（不 fail-fast、不阻断加载）。
/// S2 加性（隐蔽数据映射转正式承载）：官方标识 `covert` → 「隐蔽」标记（<see cref="KeywordIds.Covert"/>；
/// 词条标记方案——与被压制/烟幕同族；不打对战词条标）——**不再**归未实现留痕面。
/// 批 4 加性（守护数据映射转正式承载）：官方标识 `guard` → 「守护」词条（<see cref="KeywordIds.Guard"/>；
/// 标记型——行为经守护维护链读点双通道承载；打对战词条标）——**不再**归未实现留痕面。
/// 批 5 加性（冲击数据映射转正式承载）：官方标识 `shock` → 「冲击」词条（<see cref="KeywordIds.Shock"/>；
/// 标记型——免反击经 C5 判定器条款、消耗经攻击执行段尾部承载；打对战词条标）——**不再**归未实现留痕面。
/// 其余未命中（含官方未实现项 `bond`/`alpine`/`salvage`/`OnlySpawnable`）
/// ＝返回 false，由调用侧归入未实现留痕面（不 fail-fast、不写日志——P9b＝b1）。
/// </summary>
internal static class CardAttributeMap
{
    /// <summary>老兵升级链前缀（基础卡声明：指向老兵卡 id；数据映射正式承载＝卡定义端口，供升级执行查找老兵版本定义）。</summary>
    public const string BecomesVeteranPrefix = "BecomesVeteran:";

    /// <summary>老兵来源前缀（老兵卡声明：指向基础卡 id；数据映射正式承载＝「老兵」标记＋卡定义端口——单一真源）。</summary>
    public const string VeteranOfPrefix = "VeteranOf:";

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        // 官方 ↔ 项目（已实现）
        ["blitz"] = KeywordIds.Blitz,
        ["fury"] = KeywordIds.Fury,
        ["smokescreen"] = KeywordIds.SmokeScreen,
        ["ambush"] = KeywordIds.Ambush,
        ["pincer"] = KeywordIds.Pincer,
        ["mobilize"] = KeywordIds.Mobilize,
        ["covert"] = KeywordIds.Covert,
        ["guard"] = KeywordIds.Guard,
        ["shock"] = KeywordIds.Shock,

        // Orc 自有（官方语料无对应；供社区标识使用）
        ["suppressed"] = KeywordIds.Suppressed,
        ["inhibited"] = KeywordIds.Inhibited,
        ["forecast"] = KeywordIds.Forecast,
        ["immune"] = KeywordIds.Immune,
        ["cannotBeSuppressed"] = KeywordIds.CannotBeSuppressed,
        ["cannotBeInhibited"] = KeywordIds.CannotBeInhibited,
        ["deathrattle"] = KeywordIds.Deathrattle,
    };

    private static readonly (string Prefix, string Keyword)[] Valued =
    {
        ("heavyArmor", KeywordIds.Armor),        // heavyArmor1..3 → 重甲 X
        ("intel", KeywordIds.Intelligence),      // intel1..3 → 情报 X
    };

    /// <summary>Exact 反向表（中文标识 → 英文标识；单源＝<see cref="Exact"/> 反转——防双表漂移）。</summary>
    private static readonly Dictionary<string, string> ExactReverse = BuildExactReverse();

    /// <summary>Valued 反向表（中文标识 → 前缀；单源＝<see cref="Valued"/> 反转）。</summary>
    private static readonly Dictionary<string, string> ValuedReverse = BuildValuedReverse();

    /// <summary>
    /// 映射一个数据体标识为词条声明（未命中＝false——调用侧归入未实现留痕面）。
    /// 参值型按"前缀 ＋ 数字后缀"拆解（后缀须为正整数）。
    /// 老兵数据映射（<see cref="BecomesVeteranPrefix"/> / <see cref="VeteranOfPrefix"/>）不经本路径——
    /// 见 <see cref="TryMapVeteran"/>（转正式承载；不再走词条声明映射）。
    /// </summary>
    public static bool TryMap(string attribute, out KeywordDeclaration declaration)
    {
        declaration = default;
        if (string.IsNullOrWhiteSpace(attribute))
        {
            return false;
        }

        if (Exact.TryGetValue(attribute, out var keywordId))
        {
            declaration = new KeywordDeclaration(keywordId);
            return true;
        }

        foreach (var (prefix, keyword) in Valued)
        {
            if (attribute.Length <= prefix.Length
                || !attribute.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var suffix = attribute[prefix.Length..];
            if (int.TryParse(suffix, out var value) && value > 0)
            {
                declaration = new KeywordDeclaration(keyword, value);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 识别老兵数据映射标识（S1 正式承载；`BecomesVeteran:<老兵卡id>` / `VeteranOf:<基础卡id>` 两个前缀）。
    /// 命中前缀＝true 并输出类别与后缀 id 原文（**不做**合法性判定——空/空白后缀与多重声明的承载决策由调用侧
    /// 统一执行：合法唯一＝正式承载；畸形/多重＝按既有「留痕」口径归留痕面）；非本前缀＝false（走常规词条映射路径）。
    /// </summary>
    /// <param name="attribute">数据体标识原文。</param>
    /// <param name="isBecomesVeteran">true＝`BecomesVeteran:`（基础卡声明升级链）；false＝`VeteranOf:`（老兵卡声明来源）。</param>
    /// <param name="cardId">后缀（对端卡 id 原文；可能为空/空白——由调用侧判定）。</param>
    public static bool TryMapVeteran(string? attribute, out bool isBecomesVeteran, out string cardId)
    {
        isBecomesVeteran = false;
        cardId = string.Empty;
        if (string.IsNullOrWhiteSpace(attribute))
        {
            return false;
        }

        if (attribute.StartsWith(BecomesVeteranPrefix, StringComparison.Ordinal))
        {
            isBecomesVeteran = true;
            cardId = attribute[BecomesVeteranPrefix.Length..];
            return true;
        }

        if (attribute.StartsWith(VeteranOfPrefix, StringComparison.Ordinal))
        {
            cardId = attribute[VeteranOfPrefix.Length..];
            return true;
        }

        return false;
    }

    /// <summary>
    /// 反向映射（X1 加性·写方向；Exact 反转 16 项）：词条标识 → 数据体英文标识。
    /// 值位语义：Exact 标识无值位——带值词条经本路径不可表达（调用侧按「不可还原」口径警告处理）。
    /// </summary>
    /// <param name="keywordId">词条标识（项目 <see cref="KeywordIds"/> 标识）。</param>
    /// <param name="attribute">数据体英文标识（未命中＝空串）。</param>
    public static bool TryGetExactAttribute(string keywordId, out string attribute)
    {
        attribute = string.Empty;
        if (string.IsNullOrWhiteSpace(keywordId))
        {
            return false;
        }

        if (ExactReverse.TryGetValue(keywordId, out var found))
        {
            attribute = found;
            return true;
        }

        return false;
    }

    /// <summary>参值型判定（写方向分类用：重甲/情报——参值形态经 <see cref="TryGetValuedAttribute"/> 生成）。</summary>
    public static bool IsValuedKeyword(string keywordId)
        => !string.IsNullOrWhiteSpace(keywordId) && ValuedReverse.ContainsKey(keywordId);

    /// <summary>
    /// 反向映射（X1 加性·写方向；Valued 反转 2 项）：参值型词条 → `heavyArmorN` / `intelN`。
    /// 参值须为正整数（与读侧一致——int 范围内无附加上界）；缺值/非正＝不可表达（调用侧按「不可还原」口径警告处理）。
    /// </summary>
    /// <param name="keywordId">词条标识（重甲/情报）。</param>
    /// <param name="value">参值（须为正整数）。</param>
    /// <param name="attribute">数据体英文标识（未命中/参值非法＝空串）。</param>
    public static bool TryGetValuedAttribute(string keywordId, int value, out string attribute)
    {
        attribute = string.Empty;
        if (value <= 0 || string.IsNullOrWhiteSpace(keywordId))
        {
            return false;
        }

        if (ValuedReverse.TryGetValue(keywordId, out var prefix))
        {
            attribute = prefix + value;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> BuildExactReverse()
    {
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (attribute, keywordId) in Exact)
        {
            reverse[keywordId] = attribute;
        }

        return reverse;
    }

    private static Dictionary<string, string> BuildValuedReverse()
    {
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (prefix, keyword) in Valued)
        {
            reverse[keyword] = prefix;
        }

        return reverse;
    }
}
