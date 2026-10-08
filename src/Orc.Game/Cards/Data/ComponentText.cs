using System.Text.Json;

namespace Orc.Game.Cards.Data;

/// <summary>组件定义的 JSON 取值辅助（内聚：键名大小写不敏感、缺项/类型错误＝明确失败——由装载链隔离）。</summary>
internal static class ComponentText
{
    /// <summary>大小写不敏感取属性。</summary>
    public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>取字符串（缺项/非字符串＝null）。</summary>
    public static string? GetString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>取必填整数（缺项/非整数＝<see cref="FormatException"/>）。</summary>
    public static int RequireInt(JsonElement element, string name)
    {
        if (TryGetProperty(element, name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }

        throw new FormatException($"组件缺少整数字段 '{name}'（必填）。");
    }

    /// <summary>取可选整数（缺项＝<paramref name="fallback"/>；非整数＝<see cref="FormatException"/>）。</summary>
    public static int GetInt(JsonElement element, string name, int fallback = 0)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new FormatException($"组件字段 '{name}' 须为整数。");
        }

        return number;
    }

    /// <summary>取字符串数组（缺项＝空；非数组/含非字符串/空白项/重复项＝<see cref="FormatException"/>）。</summary>
    public static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"组件字段 '{name}' 须为字符串数组。");
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"组件字段 '{name}' 须为字符串数组（含非字符串项）。");
            }

            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException($"组件字段 '{name}' 含 null/空白项。");
            }

            if (list.Contains(text, StringComparer.Ordinal))
            {
                throw new FormatException($"组件字段 '{name}' 含重复项 '{text}'。");
            }

            list.Add(text);
        }

        return list;
    }

    /// <summary>取必填枚举（字面值、大小写不敏感；缺项/未定义值＝明确失败）。</summary>
    public static TEnum RequireEnum<TEnum>(JsonElement element, string name)
        where TEnum : struct, Enum
    {
        var text = GetString(element, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException($"组件缺少枚举字段 '{name}'（必填）。");
        }

        if (!Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed))
        {
            throw new ArgumentOutOfRangeException(
                name, text, $"组件字段 '{name}' 的值 '{text}' 不是合法枚举名。");
        }

        if (!Enum.IsDefined(parsed))
        {
            throw new ArgumentOutOfRangeException(
                name, text, $"组件字段 '{name}' 的值为未定义枚举值（配置错误在定义期被拒绝）。");
        }

        return parsed;
    }

    /// <summary>
    /// 写字符串数组属性（X1 加性·写方向辅助）：集合字段空＝显式 <c>[]</c>（不空省——空值统一规范）、
    /// 非空＝按序原样写出（与读侧 <see cref="GetStringArray"/> 对称）。
    /// </summary>
    /// <param name="writer">JSON 写出器（光标位于组件对象内）。</param>
    /// <param name="name">属性名。</param>
    /// <param name="items">字符串项（保序；空＝写空数组）。</param>
    public static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
