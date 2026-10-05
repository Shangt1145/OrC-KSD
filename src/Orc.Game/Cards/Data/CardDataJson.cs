using System.Text.Json;

namespace Orc.Game.Cards.Data;

/// <summary>
/// 卡牌数据体 JSON 读写（S1）：明文 UTF-8、camelCase、读宽（键名大小写不敏感／容忍注释与尾逗号）。
/// 形态：<c>{ schemaVersion, id, name, components: [ { component: "&lt;类型名&gt;", ...字段 } ] }</c>。
/// 版本（沿用 <c>PrefabJson</c> 口径）：<c>schemaVersion</c> 不符＝结构化失败（不静默降级）。
/// 隔离口径：未知组件名／单个组件反序列化失败＝写入告警集并跳过该组件（其余组件照常）；重复组件＝整卡拒绝。
/// </summary>
public static class CardDataJson
{
    /// <summary>数据体 schema 版本（本版＝1）。</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读回数据体（失败＝抛，fail-fast）。</summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="warnings">隔离告警集（未知组件／组件反序列化失败）。</param>
    /// <exception cref="ArgumentNullException">json 为 null。</exception>
    /// <exception cref="FormatException">JSON 非法、schema 版本不符、缺 id/name、重复组件。</exception>
    public static CardDataBody Deserialize(string json, out IReadOnlyList<string> warnings)
    {
        var result = Read(json);
        if (result.Body is null)
        {
            throw new FormatException(result.Error ?? "卡牌数据体非法。");
        }

        warnings = result.Warnings;
        return result.Body;
    }

    /// <summary>读回数据体（结构化：失败不抛）。</summary>
    /// <exception cref="ArgumentNullException">json 为 null。</exception>
    public static bool TryDeserialize(
        string json,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CardDataBody? body,
        out IReadOnlyList<string> warnings,
        out string? error)
    {
        var result = Read(json);
        body = result.Body;
        warnings = result.Warnings;
        error = result.Error;
        return result.Body is not null;
    }

    /// <summary>读取数据体（结构化结果；不抛——JSON 解析失败也转结构化错误）。</summary>
    /// <exception cref="ArgumentNullException">json 为 null。</exception>
    public static CardDataReadResult Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var warnings = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocOptions);
        }
        catch (JsonException ex)
        {
            return new CardDataReadResult(null, warnings, $"卡牌数据体 JSON 非法：{ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new CardDataReadResult(null, warnings, "卡牌数据体根节点须为对象。");
            }

            if (!TryGetPropertyInsensitive(root, "schemaVersion", out var schemaElement)
                || schemaElement.ValueKind != JsonValueKind.Number
                || !schemaElement.TryGetInt32(out var schemaVersion))
            {
                return new CardDataReadResult(null, warnings, "卡牌数据体缺少 schemaVersion（必填、整数）。");
            }

            if (schemaVersion != SchemaVersion)
            {
                return new CardDataReadResult(
                    null,
                    warnings,
                    $"卡牌数据体 schema 版本不符：期望 {SchemaVersion}，实际 {schemaVersion}（不静默降级）。");
            }

            var id = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                return new CardDataReadResult(null, warnings, "卡牌数据体缺少 id（必填）。");
            }

            var name = GetString(root, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return new CardDataReadResult(null, warnings, "卡牌数据体缺少 name（必填）。");
            }

            var components = new List<ICardDataComponentDefinition>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            if (TryGetPropertyInsensitive(root, "components", out var componentsElement)
                && componentsElement.ValueKind != JsonValueKind.Null)
            {
                if (componentsElement.ValueKind != JsonValueKind.Array)
                {
                    return new CardDataReadResult(null, warnings, "components 须为数组。");
                }

                foreach (var element in componentsElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        warnings.Add("components 含非对象项（已跳过）。");
                        continue;
                    }

                    var componentName = GetString(element, "component");
                    if (string.IsNullOrWhiteSpace(componentName))
                    {
                        warnings.Add("components 项缺少 component 名（已跳过）。");
                        continue;
                    }

                    if (!seenNames.Add(componentName))
                    {
                        return new CardDataReadResult(
                            null, warnings, $"数据体含重复组件 '{componentName}'（同一组件每卡至多一份——拒绝）。");
                    }

                    if (!CardComponentRegistry.TryResolve(componentName, out var entry))
                    {
                        warnings.Add($"未知组件 '{componentName}'（未注册——隔离：该组件跳过，其余照常）。");
                        continue;
                    }

                    try
                    {
                        var definition = entry.Reader(element)
                            ?? throw new FormatException("反序列化返回 null。");

                        components.Add(definition);
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException
                        or ArgumentOutOfRangeException or InvalidOperationException)
                    {
                        warnings.Add($"组件 '{componentName}' 反序列化失败（隔离）：{ex.Message}");
                    }
                }
            }

            return new CardDataReadResult(new CardDataBody(id!, name!, components), warnings, null);
        }
    }

    private static bool TryGetPropertyInsensitive(JsonElement element, string name, out JsonElement value)
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

    private static string? GetString(JsonElement element, string name)
        => TryGetPropertyInsensitive(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
