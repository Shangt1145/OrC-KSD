using System.Text.Json;
using Orc.Core;

namespace Orc.Packages;

/// <summary>包类型（S5）：插件（冷加载、静态资产）／卡包（热加载、纯卡牌数据）。</summary>
public enum PackageKind
{
    /// <summary>插件。</summary>
    Plugin = 0,

    /// <summary>卡包。</summary>
    CardPack = 1,
}

/// <summary>包级依赖（manifest 的 <c>dependsOn</c> 条目）：目标包 id ＋ 版本约束（<c>x.*.*</c> / <c>x.y.*</c> 等）。</summary>
/// <param name="Id">目标包 id。</param>
/// <param name="Version">版本约束（语义化；第一段锁定）。</param>
public sealed record PackageDependency(string Id, string Version);

/// <summary>
/// 包内资产声明（manifest 的 <c>assets</c> 条目）：加载方/来源类型 ＋ 包内短名 ＋ 相对路径 ＋ 作用域/相。
/// 资产**版本随包版本**（无独立版本字段）；资产全名经 <see cref="PackageManifest.AssetNameOf"/> 派生。
/// </summary>
/// <param name="Type">加载方/来源 token（<c>json</c> / <c>csx</c> / <c>asm</c>）。</param>
/// <param name="Name">包内短名（包内唯一）。</param>
/// <param name="Path">相对包根的资源路径。</param>
/// <param name="Scope">作用域（默认 <see cref="AssetScope.Match"/>）。</param>
/// <param name="Phase">对局级执行相（默认 <see cref="AssetActivationPhase.PreCommand"/>；Process 作用域下无意义）。</param>
public sealed record PackageAsset(
    string Type,
    string Name,
    string Path,
    AssetScope Scope = AssetScope.Match,
    AssetActivationPhase Phase = AssetActivationPhase.PreCommand);

/// <summary>
/// 包清单（manifest.json；S5）。字段：<c>kind</c> / <c>id</c> / <c>guid</c> / <c>version</c> /
/// <c>dependsOn[]</c> / <c>assets[]</c>。读宽写窄（键名大小写不敏感；非法＝结构化失败或抛错由调用方定）。
/// </summary>
/// <param name="Kind">包类型。</param>
/// <param name="Id">人类可读包 id。</param>
/// <param name="Guid">稳定身份（跨版本不变；资产名继承其前 8 位）。</param>
/// <param name="Version">包版本（＝包内全部资产版本）。</param>
/// <param name="Dependencies">包级依赖。</param>
/// <param name="Assets">包内资产声明。</param>
public sealed record PackageManifest(
    PackageKind Kind,
    string Id,
    Guid Guid,
    AssetVersion Version,
    IReadOnlyList<PackageDependency> Dependencies,
    IReadOnlyList<PackageAsset> Assets)
{
    /// <summary>包 GUID 前 8 位（小写十六进制；资产名继承用）。</summary>
    public string Guid8 => Guid.ToString("N")[..8];

    /// <summary>缓存域（<c>{包GUID8}#{版本}</c>；插件脚本求值缓存键用）。</summary>
    public string CacheScope => $"{Guid8}#{Version}";

    /// <summary>派生资产全名：<c>{类型名}：{资产名}@{包GUID8}#{包版本}</c>。</summary>
    /// <exception cref="ArgumentNullException">asset 为 null。</exception>
    /// <exception cref="FormatException">资产类型 token 非法。</exception>
    public AssetName AssetNameOf(PackageAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!AssetName.TryParseKind(asset.Type, out var kind))
        {
            throw new FormatException($"资产 '{asset.Name}' 的类型 '{asset.Type}' 非法（期望 json/csx/asm）。");
        }

        return new AssetName(kind, asset.Name, Guid8, Version);
    }

    /// <summary>解析并校验 manifest JSON 文本（非法＝<see cref="FormatException"/>）。</summary>
    public static PackageManifest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    /// <summary>从 JSON 元素读取并校验（非法＝<see cref="FormatException"/>）。</summary>
    public static PackageManifest Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("manifest 根须为对象。");
        }

        var kind = ReadKind(RequireString(root, "kind"));
        var id = RequireString(root, "id");
        var guidText = RequireString(root, "guid");
        if (!Guid.TryParse(guidText, out var guid))
        {
            throw new FormatException($"manifest guid '{guidText}' 非法（期望 GUID）。");
        }

        var versionText = RequireString(root, "version");
        if (!AssetVersion.TryParse(versionText, out var version))
        {
            throw new FormatException($"manifest version '{versionText}' 非法（期望 x.y.z）。");
        }

        return new PackageManifest(kind, id, guid, version, ReadDependencies(root), ReadAssets(root));
    }

    /// <summary>写出规范 manifest JSON（读宽写窄；字段序固定）。</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", KindToken(Kind));
            writer.WriteString("id", Id);
            writer.WriteString("guid", Guid.ToString());
            writer.WriteString("version", Version.ToString());

            writer.WriteStartArray("dependsOn");
            foreach (var dependency in Dependencies)
            {
                writer.WriteStartObject();
                writer.WriteString("id", dependency.Id);
                writer.WriteString("version", dependency.Version);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("assets");
            foreach (var asset in Assets)
            {
                writer.WriteStartObject();
                writer.WriteString("type", asset.Type.ToLowerInvariant());
                writer.WriteString("name", asset.Name);
                writer.WriteString("path", asset.Path);
                writer.WriteString("scope", ScopeToken(asset.Scope));
                writer.WriteString("phase", PhaseToken(asset.Phase));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string KindToken(PackageKind kind) => kind switch
    {
        PackageKind.Plugin => "plugin",
        PackageKind.CardPack => "cardpack",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定义的包类型。"),
    };

    private static PackageKind ReadKind(string token) => token.Trim().ToLowerInvariant() switch
    {
        "plugin" => PackageKind.Plugin,
        "cardpack" or "card_pack" => PackageKind.CardPack,
        _ => throw new FormatException($"manifest kind '{token}' 非法（期望 plugin/cardpack）。"),
    };

    private static IReadOnlyList<PackageDependency> ReadDependencies(JsonElement root)
    {
        var result = new List<PackageDependency>();
        if (!TryGet(root, "dependsOn", out var array) || array.ValueKind == JsonValueKind.Null)
        {
            return result;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("manifest dependsOn 须为数组。");
        }

        foreach (var item in array.EnumerateArray())
        {
            var id = RequireString(item, "id");
            var version = RequireString(item, "version");
            result.Add(new PackageDependency(id, version));
        }

        return result;
    }

    private static IReadOnlyList<PackageAsset> ReadAssets(JsonElement root)
    {
        var result = new List<PackageAsset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!TryGet(root, "assets", out var array) || array.ValueKind == JsonValueKind.Null)
        {
            return result;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("manifest assets 须为数组。");
        }

        foreach (var item in array.EnumerateArray())
        {
            var type = RequireString(item, "type");
            if (!AssetName.TryParseKind(type, out _))
            {
                throw new FormatException($"资产类型 '{type}' 非法（期望 json/csx/asm）。");
            }

            var name = RequireString(item, "name");
            var path = RequireString(item, "path");
            if (!seen.Add(name))
            {
                throw new FormatException($"manifest 资产短名 '{name}' 重复（包内须唯一）。");
            }

            result.Add(new PackageAsset(type, name, path, ReadScope(item), ReadPhase(item)));
        }

        return result;
    }

    /// <summary>作用域 token（规范小写）。</summary>
    public static string ScopeToken(AssetScope scope) => scope switch
    {
        AssetScope.Process => "process",
        AssetScope.Match => "match",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未定义的资产作用域。"),
    };

    /// <summary>执行相 token（规范小写）。</summary>
    public static string PhaseToken(AssetActivationPhase phase) => phase switch
    {
        AssetActivationPhase.PostCommand => "postCommand",
        AssetActivationPhase.PreCommand => "preCommand",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "未定义的资产执行相。"),
    };

    private static AssetScope ReadScope(JsonElement item)
    {
        if (!TryGet(item, "scope", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return AssetScope.Match; // 缺省＝对局级（向后兼容）
        }

        return value.GetString()?.Trim().ToLowerInvariant() switch
        {
            null or "" or "match" => AssetScope.Match,
            "process" => AssetScope.Process,
            var other => throw new FormatException($"资产 scope '{other}' 非法（期望 process/match）。"),
        };
    }

    private static AssetActivationPhase ReadPhase(JsonElement item)
    {
        if (!TryGet(item, "phase", out var value) || value.ValueKind != JsonValueKind.String)
        {
            return AssetActivationPhase.PreCommand; // 缺省＝前置相（向后兼容）
        }

        return value.GetString()?.Trim().ToLowerInvariant() switch
        {
            null or "" or "precommand" => AssetActivationPhase.PreCommand,
            "postcommand" => AssetActivationPhase.PostCommand,
            var other => throw new FormatException($"资产 phase '{other}' 非法（期望 preCommand/postCommand）。"),
        };
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
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

    private static string RequireString(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"manifest 缺少字符串字段 '{name}'。");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException($"manifest 字段 '{name}' 为空白。");
        }

        return text!;
    }
}
