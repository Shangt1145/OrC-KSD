namespace Orc.Game.Cards.Data;

/// <summary>
/// 词条标识映射表（P9a＝a1；代码常量、单一真源）：数据体里的**英文标识** → 项目 <see cref="KeywordIds"/> 中文标识。
/// 覆盖两类：①官方语料标识（`blitz`/`fury`/…/`heavyArmor1`）；②Orc 自有标识（`suppressed`/`forecast`/`deathrattle` 等——
/// 官方语料无对应词条，供社区写数据体时使用）。
/// 参值型（P9c＝c1）：显式前缀表（`heavyArmor2` → {重甲, 2}、`intel3` → {情报, 3}）。
/// 未命中（含官方未实现项 `guard`/`shock`/`bond`/`alpine`/`covert`/`salvage`/`OnlySpawnable`/`BecomesVeteran:`/`VeteranOf:`）
/// ＝返回 false，由调用侧归入未实现留痕面（不 fail-fast、不写日志——P9b＝b1）。
/// </summary>
internal static class CardAttributeMap
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        // 官方 ↔ 项目（已实现）
        ["blitz"] = KeywordIds.Blitz,
        ["fury"] = KeywordIds.Fury,
        ["smokescreen"] = KeywordIds.SmokeScreen,
        ["ambush"] = KeywordIds.Ambush,
        ["pincer"] = KeywordIds.Pincer,
        ["mobilize"] = KeywordIds.Mobilize,

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

    /// <summary>
    /// 映射一个数据体标识为词条声明（未命中＝false——调用侧归入未实现留痕面）。
    /// 参值型按"前缀 ＋ 数字后缀"拆解（后缀须为正整数）。
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
}
