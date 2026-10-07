using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Orc.Game.EffectParsing.Dsl;

namespace Orc.Game.EffectParsing.Compilation;

/// <summary>op 语句模板加载失败（单文件隔离留痕）。</summary>
/// <param name="File">文件路径。</param>
/// <param name="Error">失败原因。</param>
public sealed record OpTemplateLoadFailure(string File, string Error);

/// <summary>
/// op → csx 片段模板目录（转换段 S3）：每个 op 一个 <c>&lt;op&gt;.csx.tpl</c> 文本模板，
/// 模板＝**handler body 片段**，占位符语法 <c>{{字段}}</c>。
/// <para>占位符契约：</para>
/// <list type="bullet">
///   <item><c>{{amount}}</c>/<c>{{count}}</c>/<c>{{attack}}</c>/<c>{{defense}}</c> → 整数字面量。</item>
///   <item><c>{{value}}</c> → **参值实参后缀**（词条效果化·批 0；仅 <c>grant</c>）：提供 n（含 0/负数）→ <c>", n"</c>（逗号＋空格＋整数字面量）；未提供 → 空串（调用不携带参值——逐字节保持既有调用形态）。</item>
///   <item><c>{{op}}</c>/<c>{{keyword}}</c>/<c>{{zone}}</c> → C# 字符串字面量（含引号、已转义）。</item>
///   <item><c>{{script}}</c> → 原样注入的 csx 源码（逃生舱）。</item>
///   <item><c>{{target}}</c>/<c>{{filter}}</c> → DSL 片段的紧凑 JSON（供注释/诊断，非可执行表达式）。</item>
/// </list>
/// </summary>
public sealed class OpTemplateCatalog
{
    /// <summary>op 模板文件模式。</summary>
    public const string FilePattern = "*.csx.tpl";

    private const string FileSuffix = ".csx.tpl";

    private static readonly Regex Placeholder = new(
        @"\{\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.Ordinal)
    {
        "op", "amount", "count", "attack", "defense", "keyword", "zone", "script", "target", "filter",
        "sel", "side", "filterUnitType", "filterKeyword", "name", "rawText", "until",
        "selZone", "field", "auraFilter", "selThreshold", "value",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, string> _bodies;

    private OpTemplateCatalog(Dictionary<string, string> bodies) => _bodies = bodies;

    /// <summary>默认 op 模板目录（相对程序集输出目录）。</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "EffectParsing", "Templates", "ops");

    /// <summary>已加载的 op 名（声明序）。</summary>
    public IReadOnlyCollection<string> Ops => _bodies.Keys;

    /// <summary>加载目录（不存在＝空目录）。</summary>
    /// <exception cref="ArgumentNullException">directory 为 null。</exception>
    public static OpTemplateCatalog LoadDirectory(string directory, out IReadOnlyList<OpTemplateLoadFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            failures = Array.Empty<OpTemplateLoadFailure>();
            return new OpTemplateCatalog(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        return LoadFiles(Directory.EnumerateFiles(directory, FilePattern, SearchOption.TopDirectoryOnly), out failures);
    }

    /// <summary>加载指定文件集（逐文件隔离）。</summary>
    /// <exception cref="ArgumentNullException">files 为 null。</exception>
    public static OpTemplateCatalog LoadFiles(IEnumerable<string> files, out IReadOnlyList<OpTemplateLoadFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(files);

        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<OpTemplateLoadFailure>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var op = name.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)
                ? name[..^FileSuffix.Length]
                : Path.GetFileNameWithoutExtension(name);

            if (string.IsNullOrWhiteSpace(op))
            {
                errors.Add(new OpTemplateLoadFailure(file, "无法从文件名推导出 op 名。"));
                continue;
            }

            string body;
            try
            {
                body = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                errors.Add(new OpTemplateLoadFailure(file, $"读取失败：{ex.Message}"));
                continue;
            }

            var unknown = FindUnknownPlaceholders(body);
            if (unknown.Count > 0)
            {
                errors.Add(new OpTemplateLoadFailure(file, $"含未知占位符：{string.Join(", ", unknown)}。"));
                continue;
            }

            if (!bodies.TryAdd(op, body))
            {
                errors.Add(new OpTemplateLoadFailure(file, $"op 模板重复：'{op}'（先到者保留）。"));
            }
        }

        failures = errors;
        return new OpTemplateCatalog(bodies);
    }

    /// <summary>是否含某 op 的模板。</summary>
    public bool Has(string op) => _bodies.ContainsKey(op);

    /// <summary>
    /// 渲染某 op 的 csx 片段（占位符全部替换；残留占位符＝抛）。
    /// </summary>
    /// <exception cref="ArgumentNullException">op 为 null。</exception>
    /// <exception cref="InvalidOperationException">缺少模板、缺少参数或存在残留占位符。</exception>
    public string Render(DslOp op)
    {
        ArgumentNullException.ThrowIfNull(op);

        if (!_bodies.TryGetValue(op.Op, out var body))
        {
            throw new InvalidOperationException($"缺少 op 模板：'{op.Op}'（应在 {FilePattern} 中提供 {op.Op}{FileSuffix}）。");
        }

        var rendered = Placeholder.Replace(body, match =>
        {
            var name = match.Groups["name"].Value;
            return Resolve(op, name) ?? throw new InvalidOperationException($"op '{op.Op}' 无法解析占位符 '{{{{{name}}}}}'（参数缺失）。");
        });

        var leftover = FindUnknownPlaceholders(rendered);
        if (leftover.Count > 0)
        {
            throw new InvalidOperationException($"op '{op.Op}' 渲染后仍有未替换占位符：{string.Join(", ", leftover)}。");
        }

        return rendered;
    }

    private static string? Resolve(DslOp op, string name) => name switch
    {
        "op" => StringLiteral(op.Op),
        "amount" => Number(op.Amount),
        "count" => Number(op.Count),
        // buff 类原语允许只给其一：缺省按 0（增减量）渲染，而非报错。
        "attack" => (op.Attack ?? 0).ToString(CultureInfo.InvariantCulture),
        "defense" => (op.Defense ?? 0).ToString(CultureInfo.InvariantCulture),
        // 词条效果化·批 0：参值（可空整数）＝**实参后缀**——提供 n → ", n"（逗号＋整数字面量）；
        // 未提供 → 空串（**不携带**——既有调用形态逐字节保持）。"未提供 vs 显式 0"经此区分（空 vs ", 0"）。
        "value" => op.Value is { } value ? ", " + value.ToString(CultureInfo.InvariantCulture) : string.Empty,
        // 字符串参数缺省一律渲染为 C# null（不是参数错误——由原语注册表决定必填性）。
        "keyword" => op.Keyword is null ? "null" : StringLiteral(op.Keyword),
        "name" => op.Name is null ? "null" : StringLiteral(op.Name),
        // 原 op 文本（供注释安全使用：换行折为空格、注释终止符断开）——needsCsx 用。
        "rawText" => SanitizeComment(op.Script),
        "zone" => op.Zone is null ? "null" : StringLiteral(op.Zone),
        "script" => op.Script,
        // E1-41：期限 → EffectDuration 枚举字面量（游戏层类型，全限定——csx 默认导入不含 Orc.Game.*）。
        "until" => op.Until switch
        {
            null or "" => "Orc.Game.Effects.EffectDuration.Permanent",
            "turnEnd" => "Orc.Game.Effects.EffectDuration.TurnEnd",
            "nextOwnerTurnStart" => "Orc.Game.Effects.EffectDuration.NextOwnerTurnStart",
            _ => "Orc.Game.Effects.EffectDuration.Permanent",
        },
        // E1-56：光环的目标字段（`attack`／`defense`／`opCost`／`deployCost`）。
        "field" => op.Field is null ? "null" : StringLiteral(op.Field),
        // E1-56：光环受益谓词（**一次渲染全部实参**——谓词本体在游戏层构造，不进 csx）。
        "auraFilter" =>
            "new Orc.Game.Effects.EffectAuraFilter("
            + $"{StringLiteralOrNull(op.Target?.Side)}, {StringLiteralOrNull(op.Target?.Filter?.Faction)}, "
            + $"{StringLiteralOrNull(op.Target?.Filter?.UnitType)}, {StringLiteralOrNull(op.Target?.Filter?.Keyword)}, "
            + $"{(op.Target?.Filter?.ExcludeSelf == true ? "true" : "false")}, "

            + $"{StringLiteralOrNull(op.Target?.Zone)})",
        // E1-56：选择器区域（`frontline`／`support`）——E1-41 前一直是 `null`（**丢 zone 的近似**）。
        "selZone" => op.Target?.Zone is null ? "null" : StringLiteral(op.Target.Zone),
        // E1-57：目标阈值（`花费不大于 3 的单位`）——渲染为游戏层 `EffectThreshold`（或 null）。
        "selThreshold" => op.Target?.Filter is
            { ThresholdField: { } thresholdField, ThresholdOp: { } thresholdOp, ThresholdValue: { } thresholdValue }
                ? "new Orc.Game.Effects.EffectThreshold("
                  + $"{StringLiteral(thresholdField)}, {StringLiteral(thresholdOp)}, "
                  + $"{thresholdValue.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
                : "null",
        // 选择器字段（供 EffectRuntime.SelectAsync 的 EffectSelector 实参）。
        // E1-41：**缺省＝self**——中文卡面省略主语时主语即"本单位"；原缺省 `one` 会取"第一个单位"（打错人）。
        "sel" => StringLiteral(op.Target?.Sel ?? "self"),
        "side" => op.Target?.Side is null ? "null" : StringLiteral(op.Target.Side),
        "filterUnitType" => op.Target?.Filter?.UnitType is null ? "null" : StringLiteral(op.Target.Filter.UnitType),
        "filterKeyword" => op.Target?.Filter?.Keyword is null ? "null" : StringLiteral(op.Target.Filter.Keyword),
        // target/filter 供注释/诊断使用：缺省渲染为 JSON null。
        "target" => op.Target is null ? "null" : CompactJson(SelectorDto.From(op.Target)),
        "filter" => op.Filter is null ? "null" : CompactJson(FilterDto.From(op.Filter)),
        _ => null,
    };

    /// <summary>注释安全化：换行折为空格、<c>*/</c> 断开（供把原 op 文本放进 csx 注释）。</summary>
    private static string SanitizeComment(string? text) =>
        text?.Replace("*/", "* /", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ') ?? string.Empty;

    private static string? Number(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string StringLiteralOrNull(string? value) =>
        value is null ? "null" : StringLiteral(value);

    private static string StringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string CompactJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static List<string> FindUnknownPlaceholders(string text)
    {
        var unknown = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in Placeholder.Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (!KnownPlaceholders.Contains(name) && !unknown.Contains(name))
            {
                unknown.Add(name);
            }
        }

        return unknown;
    }

    private sealed class SelectorDto
    {
        public string? Sel { get; set; }

        public string? Side { get; set; }

        public string? Zone { get; set; }

        public FilterDto? Filter { get; set; }

        public int? Count { get; set; }

        public static SelectorDto From(DslSelector selector) => new()
        {
            Sel = selector.Sel,
            Side = selector.Side,
            Zone = selector.Zone,
            Filter = selector.Filter is null ? null : FilterDto.From(selector.Filter),
            Count = selector.Count,
        };
    }

    private sealed class FilterDto
    {
        public string? UnitType { get; set; }

        public string? Keyword { get; set; }

        public static FilterDto From(DslFilter filter) => new()
        {
            UnitType = filter.UnitType,
            Keyword = filter.Keyword,
        };
    }
}
