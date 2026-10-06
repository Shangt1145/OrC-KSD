using System.Text.Json;

namespace Orc.Game.EffectParsing.Lexicon;

/// <summary>词表加载失败（单文件隔离留痕）。</summary>
/// <param name="File">文件路径。</param>
/// <param name="Error">失败原因。</param>
public sealed record LexiconLoadFailure(string File, string Error);

/// <summary>
/// 外置词表加载器（解析段 S6）：<c>Lexicon/*.json</c>，**按类别分文件**（文件名 → 类别），
/// 条目格式 <c>{ "&lt;字面&gt;": { "&lt;载荷键&gt;": "&lt;值&gt;" } }</c>。
/// </summary>
public static class LexiconLoader
{
    /// <summary>默认词表目录（相对程序集输出目录）。</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "EffectParsing", "Lexicon");

    private static readonly Dictionary<string, LexiconCategory> FileCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["triggers.json"] = LexiconCategory.Trigger,
        ["connectives.json"] = LexiconCategory.Connective,
        ["pronouns.json"] = LexiconCategory.Pronoun,
        ["actions.json"] = LexiconCategory.Action,
        ["quantifiers.json"] = LexiconCategory.Quant,
        ["sides.json"] = LexiconCategory.Side,
        ["zones.json"] = LexiconCategory.Zone,
        ["filters.json"] = LexiconCategory.Filter,
        ["conditions.json"] = LexiconCategory.Cond,
        ["numerals.json"] = LexiconCategory.Num,
    };

    /// <summary>加载目录（不存在＝空词表）。</summary>
    /// <exception cref="ArgumentNullException">directory 为 null。</exception>
    public static LexiconSet LoadDirectory(string directory, out IReadOnlyList<LexiconLoadFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            failures = Array.Empty<LexiconLoadFailure>();
            return new LexiconSet(Array.Empty<LexiconEntry>());
        }

        return LoadFiles(Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly), out failures);
    }

    /// <summary>加载指定文件集（逐文件隔离）。</summary>
    /// <exception cref="ArgumentNullException">files 为 null。</exception>
    public static LexiconSet LoadFiles(IEnumerable<string> files, out IReadOnlyList<LexiconLoadFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(files);

        var entries = new List<LexiconEntry>();
        var errors = new List<LexiconLoadFailure>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (!FileCategories.TryGetValue(name, out var category))
            {
                errors.Add(new LexiconLoadFailure(file, $"未知词表文件（文件名 → 类别映射缺失）：'{name}'。"));
                continue;
            }

            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                errors.Add(new LexiconLoadFailure(file, $"读取失败：{ex.Message}"));
                continue;
            }

            Dictionary<string, Dictionary<string, string>>? dto;
            try
            {
                dto = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
            }
            catch (JsonException ex)
            {
                errors.Add(new LexiconLoadFailure(file, $"JSON 非法：{ex.Message}"));
                continue;
            }

            if (dto is null)
            {
                errors.Add(new LexiconLoadFailure(file, "词表为空。"));
                continue;
            }

            foreach (var pair in dto)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    errors.Add(new LexiconLoadFailure(file, "含空白字面。"));
                    continue;
                }

                entries.Add(new LexiconEntry(
                    pair.Key,
                    category,
                    new Dictionary<string, string>(pair.Value ?? new Dictionary<string, string>(), StringComparer.Ordinal)));
            }
        }

        LexiconSet lexicons;
        try
        {
            lexicons = new LexiconSet(entries);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(new LexiconLoadFailure("<lexicon>", ex.Message));
            failures = errors;
            return new LexiconSet(Array.Empty<LexiconEntry>());
        }

        failures = errors;
        return lexicons;
    }
}
