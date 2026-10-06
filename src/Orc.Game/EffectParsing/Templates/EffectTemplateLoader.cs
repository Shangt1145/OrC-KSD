namespace Orc.Game.EffectParsing.Templates;

/// <summary>模板加载失败（单文件隔离留痕）。</summary>
/// <param name="File">文件路径。</param>
/// <param name="Error">失败原因。</param>
public sealed record EffectTemplateLoadFailure(string File, string Error);

/// <summary>模板加载结果（纯数据产出）。</summary>
/// <param name="Templates">成功加载的模板（声明序）。</param>
/// <param name="Failures">失败/隔离留痕。</param>
public sealed record EffectTemplateLoadResult(
    IReadOnlyList<EffectTemplate> Templates,
    IReadOnlyList<EffectTemplateLoadFailure> Failures);

/// <summary>
/// 模板效果目录加载器（转换段 S2）：扫描 <c>*.tpl.json</c>，**单文件失败隔离**（不阻断整批）；
/// 同 id 重复＝后到者被隔离。
/// </summary>
public static class EffectTemplateLoader
{
    /// <summary>模板文件模式。</summary>
    public const string FilePattern = "*.tpl.json";

    /// <summary>默认模板目录（相对程序集输出目录）。</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "EffectParsing", "Templates");

    /// <summary>加载目录（不存在＝空结果）。</summary>
    /// <exception cref="ArgumentNullException">directory 为 null。</exception>
    public static EffectTemplateLoadResult LoadDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            return new EffectTemplateLoadResult(Array.Empty<EffectTemplate>(), Array.Empty<EffectTemplateLoadFailure>());
        }

        return LoadFiles(Directory.EnumerateFiles(directory, FilePattern, SearchOption.TopDirectoryOnly));
    }

    /// <summary>加载指定文件集（逐文件隔离）。</summary>
    /// <exception cref="ArgumentNullException">files 为 null。</exception>
    public static EffectTemplateLoadResult LoadFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var templates = new List<EffectTemplate>();
        var failures = new List<EffectTemplateLoadFailure>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                failures.Add(new EffectTemplateLoadFailure(file, $"读取失败：{ex.Message}"));
                continue;
            }

            if (!EffectTemplateJson.TryDeserialize(json, out var template, out var error))
            {
                failures.Add(new EffectTemplateLoadFailure(file, error!));
                continue;
            }

            if (!ids.Add(template!.Id))
            {
                failures.Add(new EffectTemplateLoadFailure(file, $"模板 id 重复：'{template.Id}'（先到者保留）。"));
                continue;
            }

            templates.Add(template);
        }

        return new EffectTemplateLoadResult(templates, failures);
    }

    /// <summary>按 id 建索引（重复 id 抛）。</summary>
    /// <exception cref="ArgumentNullException">templates 为 null。</exception>
    /// <exception cref="InvalidOperationException">id 重复。</exception>
    public static IReadOnlyDictionary<string, EffectTemplate> Index(IEnumerable<EffectTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        var index = new Dictionary<string, EffectTemplate>(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            if (!index.TryAdd(template.Id, template))
            {
                throw new InvalidOperationException($"模板 id 重复：'{template.Id}'。");
            }
        }

        return index;
    }
}
