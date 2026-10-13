namespace Orc.Game.Cards;

/// <summary>
/// 单位类型标识（KARDS 模仿；资产化 S4：由 <c>enum</c> 改为**字符串常量类**——真源＝资产服务，
/// 本类＝内置项的可读符号，标识字面＝对外数据契约基线）。
/// 以列表承载（<see cref="UnitStateData.UnitTypes"/>）：允许 0 个 / 多个（不设非空校验）；扩展留。
/// 卡数据取值经 <see cref="TryNormalize"/> 归一（大小写不敏感——读宽写窄）。
/// </summary>
public static class UnitType
{
    /// <summary>步兵。</summary>
    public const string Infantry = "infantry";

    /// <summary>坦克。</summary>
    public const string Tank = "tank";

    /// <summary>炮兵。</summary>
    public const string Artillery = "artillery";

    /// <summary>战斗机。</summary>
    public const string Fighter = "fighter";

    /// <summary>轰炸机。</summary>
    public const string Bomber = "bomber";

    /// <summary>内置单位类型标识清单（登记序）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Infantry, Tank, Artillery, Fighter, Bomber };

    /// <summary>是否为已定义单位类型标识（null/空白＝false、不抛错）。</summary>
    public static bool IsDefined(string? value)
        => value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>归一到规范标识（大小写不敏感；未定义＝false——由资产服务/数据层判失败）。</summary>
    public static bool TryNormalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lowered = value.Trim().ToLowerInvariant();
        if (!All.Contains(lowered, StringComparer.Ordinal))
        {
            return false;
        }

        canonical = lowered;
        return true;
    }
}
