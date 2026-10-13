using System.Globalization;

namespace Orc.Core;

// ─────────────────────────────────────────────────────────────────────────────
// 资产命名文法（资产化 S2）：`{类型名}：{资产名}@{包GUID8}#{版本}`
//   · 类型名＝加载方/来源（json 数据 / csx 逻辑 / asm 程序集）；
//   · 包GUID8＝所属包稳定身份前 8 位（包内资产继承包的 GUID）；
//   · 版本＝x.y.z 语义化（随包版本）。
// 读宽写窄（对齐项目既有原则）：解析接受全角/半角冒号与大小写；写出规范（全角冒号、小写类型名与 GUID8）。
// 版本**约束**（`*`/`^` 通配、择版）归包管理器（S6），本文件只承载**具体版本**的资产名。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>资产加载方/来源（类型名）。</summary>
public enum AssetLoadKind
{
    /// <summary>数据 JSON（枚举/资产值域等）。</summary>
    Json = 0,

    /// <summary>csx 逻辑（资产服务/判定器/效果 handler 等）。</summary>
    Csx = 1,

    /// <summary>可选程序集（csx 表达不了/性能敏感的逃生舱）。</summary>
    Asm = 2,
}

/// <summary>资产作用域（P11 甲）：进程级（静态注册面；冷加载期执行）／对局级（对局装配期执行）。</summary>
public enum AssetScope
{
    /// <summary>对局级（默认）：在有对局的装配期执行（判定器/资产服务/效果 handler 等）。</summary>
    Match = 0,

    /// <summary>进程级：冷加载期执行（词条/数据组件等静态注册面）。</summary>
    Process = 1,
}

/// <summary>对局级资产的执行相（P11 甲）：CommandManager 之前／之后。</summary>
public enum AssetActivationPhase
{
    /// <summary>CommandManager 之前（判定器/资产服务——协作者以延后求值访问）。</summary>
    PreCommand = 0,

    /// <summary>CommandManager 之后（需触发器实例/玩家面——流程触发器登记、具名触发器、效果库装配）。</summary>
    PostCommand = 1,
}

/// <summary>语义化版本（x.y.z）。</summary>
/// <param name="Major">主版本。</param>
/// <param name="Minor">次版本。</param>
/// <param name="Patch">修订版本。</param>
public readonly record struct AssetVersion(int Major, int Minor, int Patch) : IComparable<AssetVersion>
{
    /// <summary>解析 <c>x.y.z</c>（三段非负整数）。</summary>
    public static bool TryParse(string? text, out AssetVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new AssetVersion(major, minor, patch);
        return true;
    }

    /// <summary>解析 <c>x.y.z</c>；非法＝<see cref="FormatException"/>。</summary>
    public static AssetVersion Parse(string text)
        => TryParse(text, out var version)
            ? version
            : throw new FormatException($"非法语义化版本 '{text}'（期望 x.y.z）。");

    /// <inheritdoc />
    public int CompareTo(AssetVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>
/// 资产全名（全局唯一）：<c>{类型名}：{资产名}@{包GUID8}#{版本}</c>。
/// </summary>
/// <param name="Kind">加载方/来源。</param>
/// <param name="LocalName">资产短名（包内唯一）。</param>
/// <param name="PackageGuid8">所属包稳定身份前 8 位（小写十六进制）。</param>
/// <param name="Version">具体版本。</param>
public sealed record AssetName(AssetLoadKind Kind, string LocalName, string PackageGuid8, AssetVersion Version)
{
    /// <summary>规范分隔符（写作用；全角冒号）。</summary>
    public const char CanonicalSeparator = '：';

    /// <summary>类型名 token（规范小写）。</summary>
    public string KindToken => Kind switch
    {
        AssetLoadKind.Json => "json",
        AssetLoadKind.Csx => "csx",
        AssetLoadKind.Asm => "asm",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "未定义的资产加载方。"),
    };

    /// <summary>解析类型名 token（大小写不敏感）。</summary>
    public static bool TryParseKind(string? token, out AssetLoadKind kind)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "json": kind = AssetLoadKind.Json; return true;
            case "csx": kind = AssetLoadKind.Csx; return true;
            case "asm": kind = AssetLoadKind.Asm; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>按规范形式写出（读宽写窄）。</summary>
    public override string ToString()
        => $"{KindToken}{CanonicalSeparator}{LocalName}@{PackageGuid8}#{Version}";

    /// <summary>
    /// 解析资产全名（宽容：接受全角/半角冒号、大小写；<paramref name="guid8"/> 与 <paramref name="localName"/> 统一小写规整）。
    /// </summary>
    public static bool TryParse(string? text, out AssetName? name)
    {
        name = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hashIndex = text.LastIndexOf('#');
        if (hashIndex <= 0 || hashIndex == text.Length - 1)
        {
            return false;
        }

        if (!AssetVersion.TryParse(text[(hashIndex + 1)..], out var version))
        {
            return false;
        }

        var head = text[..hashIndex];
        var atIndex = head.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == head.Length - 1)
        {
            return false;
        }

        var guid8 = head[(atIndex + 1)..].Trim();
        if (guid8.Length != 8 || !IsHex(guid8))
        {
            return false;
        }

        var ownerAndKind = head[..atIndex];
        var sepIndex = ownerAndKind.IndexOfAny([CanonicalSeparator, ':']);
        if (sepIndex <= 0)
        {
            return false;
        }

        if (!TryParseKind(ownerAndKind[..sepIndex], out var kind))
        {
            return false;
        }

        var localName = ownerAndKind[(sepIndex + 1)..].Trim();
        if (localName.Length == 0)
        {
            return false;
        }

        name = new AssetName(kind, localName, guid8.ToLowerInvariant(), version);
        return true;
    }

    /// <summary>解析资产全名；非法＝<see cref="FormatException"/>。</summary>
    public static AssetName Parse(string text)
        => TryParse(text, out var name)
            ? name!
            : throw new FormatException(
                $"非法资产名 '{text}'（期望 {{类型名}}{CanonicalSeparator}{{资产名}}@{{包GUID8}}#{{x.y.z}}）。");

    private static bool IsHex(string text)
    {
        foreach (var c in text)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
