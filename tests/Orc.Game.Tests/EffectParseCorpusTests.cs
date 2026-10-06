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
        var silent = 0;
        var semanticsComplete = 0;
        var withPlaceholderCondition = 0;
        var withNeedsCsx = 0;
        var silentSamples = new List<string>();
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<(string Text, string Reason)>();

        foreach (var text in texts)
        {
            var result = parser.Parse(text);
            if (result.Effects.Count > 0)
            {
                withEffects++;

                // 口径（E1-41 甲）：报告**分层披露**——总产出 / 语义完整 / 含占位条件 / 含 needsCsx。
                var placeholder = false;
                var needsCsx = false;
                foreach (var effect in result.Effects)
                {
                    foreach (var fill in effect.Fills.Values)
                    {
                        foreach (var op in fill.Ops)
                        {
                            if (op.Condition is { Kind: "raw" })
                            {
                                placeholder = true;
                            }

                            if (op.Op is "needsCsx" or "csx")
                            {
                                needsCsx = true;
                            }
                        }
                    }
                }

                if (placeholder)
                {
                    withPlaceholderCondition++;
                }

                if (needsCsx)
                {
                    withNeedsCsx++;
                }

                if (!placeholder && !needsCsx)
                {
                    semanticsComplete++;
                }
            }

            totalEffects += result.Effects.Count;
            totalUnresolved += result.Unresolved.Count;

            // R8 观测面（E1-38）：既无效果、也无诊断＝**静默丢弃**（应为 0；非 0 即违规——含"整条仅词条行"的合法空文本，
            // 故此处只统计与列举、不设硬门槛，供人工复核）。
            if (result.Effects.Count == 0 && result.Unresolved.Count == 0)
            {
                silent++;
                if (silentSamples.Count < 20)
                {
                    silentSamples.Add(text.Replace("\n", "\\n", StringComparison.Ordinal));
                }
            }

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

        var report = BuildReport(
            corpusPath, texts.Count, withEffects, totalEffects, totalUnresolved, silent, silentSamples, reasons, samples,
            semanticsComplete, withPlaceholderCondition, withNeedsCsx);
        var directory = Path.Combine(AppContext.BaseDirectory, "EffectParsing", "report");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "corpus-coverage.md");
        File.WriteAllText(reportPath, report);

        _output.WriteLine(
            $"语料：{texts.Count} 条；产出效果：{withEffects} 条（语义完整 {semanticsComplete}／含占位条件 {withPlaceholderCondition}／"
            + $"含 needsCsx {withNeedsCsx}）；效果总数：{totalEffects}；"
            + $"未解析记录：{totalUnresolved}；既无效果也无诊断：{silent}");
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
        int silent,
        IReadOnlyList<string> silentSamples,
        IReadOnlyDictionary<string, int> reasons,
        IReadOnlyList<(string Text, string Reason)> samples,
        int semanticsComplete,
        int withPlaceholderCondition,
        int withNeedsCsx)
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
        builder.Append("| **既无效果、也无诊断（R8 违规面）** | ").Append(silent).AppendLine(" |");
        var rate = total == 0 ? 0 : withEffects * 100.0 / total;
        builder.Append("| 覆盖率（**结构**口径＝甲） | ").Append(rate.ToString("F1")).AppendLine("% |");
        var completeRate = total == 0 ? 0 : semanticsComplete * 100.0 / total;
        builder.Append("| 覆盖率（**语义完整**口径＝乙） | ").Append(completeRate.ToString("F1")).AppendLine("% |");
        builder.AppendLine();
        builder.AppendLine("### 分层披露（口径甲：结构可解析计入，但分层可见）").AppendLine();
        builder.AppendLine("| 分层 | 卡数 | 说明 |").AppendLine("|---|---|---|");
        builder.Append("| 语义完整（无占位条件、无 needsCsx/csx） | ").Append(semanticsComplete)
            .AppendLine(" | 直接可运行 |");
        builder.Append("| 含**占位条件**（`raw` ⇒ `if (false)`） | ").Append(withPlaceholderCondition)
            .AppendLine(" | 结构完成、条件求值面待接 |");
        builder.Append("| 含 `needsCsx`／`csx` 逃生舱 | ").Append(withNeedsCsx)
            .AppendLine(" | 已知语义、待 csx 实现 |");
        builder.AppendLine("| 产出 0 效果（未解析） | ").Append(total - withEffects).AppendLine(" | 显式失败 + 诊断 |");
        builder.AppendLine();

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

        builder.AppendLine().AppendLine("## 口径说明").AppendLine();
        builder.AppendLine("- **结构覆盖率**＝「能翻译成 DSL 并编译成真 csx」的卡面占比；**语义完整覆盖率**＝「没有占位条件、"
            + "也没有 `needsCsx`/`csx`（即**直接可跑**）」的卡面占比——两者**必须同时报**（只报前者会把死代码算成产出）。");
        builder.AppendLine("- 剩余缺口**绝大多数是游戏层机制缺失**（非「读不懂中文」）：能力边界、缺口归因、恢复条件与例子"
            + "见 `.ams/context/effect-parser/交接文档.md` **§6**。");

        builder.AppendLine().AppendLine("## 既无效果、也无诊断（前 20；应为空）").AppendLine();
        builder.AppendLine("| 卡面 |").AppendLine("|---|");
        foreach (var text in silentSamples)
        {
            builder.Append("| ").Append(text.Replace("|", "\\|", StringComparison.Ordinal)).AppendLine(" |");
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
