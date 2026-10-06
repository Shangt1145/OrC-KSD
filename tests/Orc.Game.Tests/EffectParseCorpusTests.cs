using System.Text;
using System.Text.Json;
using Orc.Game.EffectParsing.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Orc.Game.Tests;

/// <summary>
/// 效果解析器·**语料对拍**：拿真实卡面语料（<c>docs/初始设计/kards官方卡牌.json</c> 的 <c>zh-Hans</c>）
/// 全量跑一遍解析器，产出覆盖率与"未解析原因 TOP"报告（.md），用于量化"子集边界"。
/// 语料文件缺失＝跳过（不失败）。
/// </summary>
public class EffectParseCorpusTests
{
    private readonly ITestOutputHelper _output;

    public EffectParseCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Corpus_Coverage_Report()
    {
        var corpusPath = FindCorpus();
        if (corpusPath is null)
        {
            _output.WriteLine("语料文件缺失，跳过对拍。");
            return;
        }

        var parser = EffectParser.CreateDefault(out var lexiconFailures);
        Assert.Empty(lexiconFailures);

        var texts = ReadCardTexts(corpusPath);
        Assert.NotEmpty(texts);

        var withEffects = 0;
        var totalEffects = 0;
        var totalUnresolved = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<(string Text, string Reason)>();

        foreach (var text in texts)
        {
            var result = parser.Parse(text);
            if (result.Effects.Count > 0)
            {
                withEffects++;
            }

            totalEffects += result.Effects.Count;
            totalUnresolved += result.Unresolved.Count;

            foreach (var record in result.Unresolved)
            {
                var reason = Normalize(record.Reason);
                reasons[reason] = reasons.TryGetValue(reason, out var count) ? count + 1 : 1;
                if (samples.Count < 20)
                {
                    samples.Add((text, record.Reason));
                }
            }
        }

        var report = BuildReport(corpusPath, texts.Count, withEffects, totalEffects, totalUnresolved, reasons, samples);
        var directory = Path.Combine(AppContext.BaseDirectory, "EffectParsing", "report");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "corpus-coverage.md");
        File.WriteAllText(reportPath, report);

        _output.WriteLine($"语料：{texts.Count} 条；产出效果：{withEffects} 条；效果总数：{totalEffects}；未解析记录：{totalUnresolved}");
        foreach (var pair in reasons.OrderByDescending(p => p.Value).Take(12))
        {
            _output.WriteLine($"  {pair.Value,5}  {pair.Key}");
        }

        Assert.True(File.Exists(reportPath));
        Assert.True(withEffects > 0, "语料对拍未产出任何效果。");
    }

    private static string BuildReport(
        string corpusPath,
        int total,
        int withEffects,
        int totalEffects,
        int totalUnresolved,
        IReadOnlyDictionary<string, int> reasons,
        IReadOnlyList<(string Text, string Reason)> samples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 效果解析器·语料对拍报告").AppendLine();
        builder.Append("语料：`").Append(corpusPath).AppendLine("`（`zh-Hans` 卡面文本）").AppendLine();
        builder.AppendLine("## 汇总").AppendLine();
        builder.AppendLine("| 指标 | 值 |").AppendLine("|---|---|");
        builder.Append("| 卡面文本总数 | ").Append(total).AppendLine(" |");
        builder.Append("| 产出 ≥1 效果 | ").Append(withEffects).AppendLine(" |");
        builder.Append("| 产出 0 效果 | ").Append(total - withEffects).AppendLine(" |");
        builder.Append("| 效果总数 | ").Append(totalEffects).AppendLine(" |");
        builder.Append("| 未解析记录总数 | ").Append(totalUnresolved).AppendLine(" |");
        var rate = total == 0 ? 0 : withEffects * 100.0 / total;
        builder.Append("| 覆盖率 | ").Append(rate.ToString("F1")).AppendLine("% |").AppendLine();

        builder.AppendLine("## 未解析原因 TOP").AppendLine();
        builder.AppendLine("| 次数 | 原因 |").AppendLine("|---|---|");
        foreach (var pair in reasons.OrderByDescending(p => p.Value).Take(30))
        {
            builder.Append("| ").Append(pair.Value).Append(" | ").Append(pair.Key.Replace("|", "\\|", StringComparison.Ordinal)).AppendLine(" |");
        }

        builder.AppendLine().AppendLine("## 未解析样例（前 20）").AppendLine();
        builder.AppendLine("| 卡面 | 原因 |").AppendLine("|---|---|");
        foreach (var (text, reason) in samples)
        {
            builder.Append("| ")
                .Append(text.Replace("\n", "\\n", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal))
                .Append(" | ")
                .Append(reason.Replace("|", "\\|", StringComparison.Ordinal))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    /// <summary>把原因里的原文片段抹掉，便于聚合计数。</summary>
    private static string Normalize(string reason)
    {
        var index = reason.IndexOf("：'", StringComparison.Ordinal);
        if (index >= 0)
        {
            var tail = reason.IndexOf('）');
            return tail > index ? reason[..(index + 1)] + reason[(tail + 1)..] : reason[..(index + 1)];
        }

        return reason;
    }

    private static List<string> ReadCardTexts(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var texts = new List<string>();
        if (!document.RootElement.TryGetProperty("cards", out var cards))
        {
            return texts;
        }

        foreach (var card in cards.EnumerateArray())
        {
            if (!card.TryGetProperty("json", out var json)
                || !json.TryGetProperty("text", out var text)
                || !text.TryGetProperty("zh-Hans", out var zh))
            {
                continue;
            }

            var value = zh.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                texts.Add(value!);
            }
        }

        return texts;
    }

    private static string? FindCorpus()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "初始设计", "kards官方卡牌.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(directory.FullName, "OrcEngine.sln")))
            {
                break;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
