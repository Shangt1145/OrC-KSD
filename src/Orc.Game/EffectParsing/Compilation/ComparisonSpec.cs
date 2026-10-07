namespace Orc.Game.EffectParsing.Compilation;

/// <summary>
/// **数值比较条件规范串**（E1-57）的合法性校验：`左度量:算子:右操作数`。
/// <para>目的＝**不把未受控文本拼进 csx**：规范串由映射层结构化产出，此处再做一次"只允许词表内取值"的把关，
/// 非法即渲染为 `false`（显式占位），绝不放行到编译期。</para>
/// </summary>
internal static class ComparisonSpec
{
    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        "gte", "lte", "gt", "lt", "eq",
    };

    private static readonly HashSet<string> Sides = new(StringComparer.Ordinal)
    {
        "friendly", "enemy", "both",
    };

    private static readonly HashSet<string> Stats = new(StringComparer.Ordinal)
    {
        "attack", "defense", "opCost", "deployCost",
    };

    /// <summary>规范串是否合法（算子/侧/字段/右操作数全在词表内）。</summary>
    public static bool IsValid(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var parts = spec.Split(':');
        return parts.Length == 3
               && Operators.Contains(parts[1])
               && IsMeasure(parts[0])
               && IsRightOperand(parts[2]);
    }

    private static bool IsRightOperand(string operand) =>
        operand.Length > 1 && operand[0] == '#' && int.TryParse(operand[1..], out _)
        || IsMeasure(operand);

    private static bool IsMeasure(string measure)
    {
        if (measure.StartsWith("count=", StringComparison.Ordinal)
            || measure.StartsWith("points=", StringComparison.Ordinal))
        {
            var side = Attribute(measure, "s");
            return side is null || Sides.Contains(side);
        }

        // S3：在场回合数（主体恒为宿主自身——`s=self`；缺省同义）。
        if (measure.StartsWith("turns=", StringComparison.Ordinal))
        {
            var side = Attribute(measure, "s");
            return side is null || string.Equals(side, "self", StringComparison.Ordinal);
        }

        if (measure.StartsWith("stat=", StringComparison.Ordinal))
        {
            var field = Attribute(measure, "f");
            var side = Attribute(measure, "s");
            var zone = Attribute(measure, "z");
            return field is not null && Stats.Contains(field)
                   && (side is null || Sides.Contains(side))
                   && (zone is null || zone == "hq");
        }

        return false;
    }

    private static string? Attribute(string spec, string key)
    {
        var body = spec[(spec.IndexOf('=', StringComparison.Ordinal) + 1)..];
        foreach (var pair in body.Split(';'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && string.Equals(pair[..separator], key, StringComparison.Ordinal))
            {
                return pair[(separator + 1)..];
            }
        }

        return null;
    }
}
