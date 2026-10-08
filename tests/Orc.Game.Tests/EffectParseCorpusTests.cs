using System.Text;
using System.Text.Json;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Parsing;
using Orc.Game.EffectParsing.Templates;
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
        var withDeclarations = 0;
        var totalDeclarations = 0;
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
            totalDeclarations += result.Declarations.Count;
            if (result.Declarations.Count > 0)
            {
                withDeclarations++;
            }

            // R8 观测面（E1-38→序列③）：既无效果、也无诊断、也无声明产出＝**静默丢弃**（应为 0；非 0 即违规——含"整条仅词条行"的
            // 合法空文本，故此处只统计与列举、不设硬门槛，供人工复核）。序列③口径更新：判定成立的词条行产出**声明**（新通道）
            // ⇒ 词条行整体移出静默类（口径变化对照见报告「词条行联动对照」节；声明不得计入效果/覆盖率计——防口径污染）。
            if (result.Effects.Count == 0 && result.Unresolved.Count == 0 && result.Declarations.Count == 0)
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
            corpusPath, texts.Count, withEffects, totalEffects, totalUnresolved, withDeclarations, totalDeclarations,
            silent, silentSamples, reasons, samples,
            semanticsComplete, withPlaceholderCondition, withNeedsCsx);

        // S3：覆盖率报告**新增「本机制样本（老兵／隐蔽）」独立节**（逐例：文本／来源／结果类别／完整性／产物要点；
        // 全量对拍语料与总体数据口径**保持不变**——样本性质不同〔机制专项 vs 全量对拍〕，不并入对拍循环）。
        report += Environment.NewLine + BuildMechanismSamplesSection();

        // 序列③：覆盖率报告**新增「词条行（声明产出）」独立节**（与专项测试同源、逐例断言随行；
        // 独立于全量对拍语料——总体数据口径保持不变）。
        report += Environment.NewLine + BuildKeywordLineSamplesSection();

        var directory = Path.Combine(AppContext.BaseDirectory, "EffectParsing", "report");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "corpus-coverage.md");
        File.WriteAllText(reportPath, report);

        _output.WriteLine(
            $"语料：{texts.Count} 条；产出效果：{withEffects} 条（语义完整 {semanticsComplete}／含占位条件 {withPlaceholderCondition}／"
            + $"含 needsCsx {withNeedsCsx}）；效果总数：{totalEffects}；"
            + $"含词条行声明：{withDeclarations} 条（声明总数 {totalDeclarations}）；"
            + $"未解析记录：{totalUnresolved}；既无效果、无诊断、无声明：{silent}");
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
        int withDeclarations,
        int totalDeclarations,
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
        builder.Append("| 含词条行声明产出 | ").Append(withDeclarations).AppendLine(" |");
        builder.Append("| 词条行声明总数 | ").Append(totalDeclarations).AppendLine(" |");
        builder.Append("| **既无效果、无诊断、无声明（R8 违规面）** | ").Append(silent).AppendLine(" |");
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

        // 序列③：**词条行联动对照**（批 4·序列③基线→现——"三项对照"：声明／未解析变化／静默下降；
        // 声明不得计入效果/覆盖率计——防口径污染）。基线＝改造前实测（对照材料：_work/corpus-dump-true-old-ops.tsv）。
        builder.AppendLine("## 词条行联动对照（批 4·序列③）").AppendLine();
        builder.AppendLine("| 指标 | 基线（改造前） | 现 | 差异说明 |").AppendLine("|---|---|---|---|");
        builder.Append("| 含词条行声明产出 | 0（无通道） | ").Append(withDeclarations)
            .AppendLine(" | 新增维度（判定成立的词条行从「静默丢弃」改为产出声明） |");
        builder.Append("| 词条行声明总数 | 0（无通道） | ").Append(totalDeclarations).AppendLine(" | 新增维度 |");
        builder.Append("| 未解析记录总数 | 532 | ").Append(totalUnresolved)
            .AppendLine(" | +3＝「压制/抑制」复合词误读修正 +6／词条行「未解析转声明」-3（b/c 异常子类 3 条为原因变更、计数不变）；逐条明细见交付说明对照表 |");
        builder.Append("| 静默（无效果、无未解析、无声明） | 0 | ").Append(silent)
            .AppendLine(" | 语料无「整条仅被丢弃词条行」案例；口径更新后仍 0 |");
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

    // ---------- S3：本机制样本（老兵／隐蔽）——独立手工样本集＋覆盖率报告独立节 ----------

    /// <summary>
    /// S3 机制样本（老兵／隐蔽）：**卡池原文逐字**（含弯引号/空格）＋来源标注（分卷/行号——供后续漂移核对）
    /// ＋完整性档位（序列③：V4 经解析层回补〔『获得冲击』真映射〕归完整档——**留痕豁免档位机制保留、当前档为空**；其余完整档）。
    /// <para>样本纪律（Q&A-6）：逐例＝卡池对应场景的连续句组；不并入全量对拍语料（总体数据口径保持不变）。</para>
    /// </summary>
    private static readonly (string Id, string Text, string Source, string Completeness)[] MechanismSamples =
    {
        ("V1", "在场上的第三回合开始时，升为老兵。", "part-13.txt:29", "完整档（无占位、无 needsCsx）"),
        ("V2", "本单位对敌方总部造成伤害时，升为老兵。升为老兵时，将 1 张“一号坦克 B 型”加入手牌。", "part-03.txt:61-62", "完整档（无占位、无 needsCsx）"),
        ("V3", "友方单位升为老兵时，本单位获得 +2+2。", "part-07.txt:35", "完整档（无占位、无 needsCsx）"),
        ("V4", "使 1 个老兵单位获得奋战和冲击。", "part-02.txt:23", "完整档（无占位、无 needsCsx）"),
        ("C5", "揭示：若是友方回合，获得 +2 攻击力。", "part-11.txt:48", "完整档（无占位、无 needsCsx）"),
        ("C6", "部署：揭示 1 个隐蔽单位。", "part-11.txt:60", "完整档（无占位、无 needsCsx）"),
        ("C7", "友方隐蔽单位被揭示时，使所有友方单位获得 +1+1。", "part-05.txt:10", "完整档（无占位、无 needsCsx）"),
    };

    /// <summary>
    /// 生成「本机制样本（老兵／隐蔽）」节（逐例：文本／来源／结果类别／完整性／产物要点）——
    /// 逐例断言随行（解析无未解析记录、编译产物非空、完整性档位与产物一致——不静默降级）。
    /// </summary>
    private static string BuildMechanismSamplesSection()
    {
        var parser = EffectParser.CreateDefault(out var lexiconFailures);
        Assert.Empty(lexiconFailures);
        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        Assert.Empty(templates.Failures);
        var ops = OpTemplateCatalog.LoadDirectory(OpTemplateCatalog.DefaultDirectory, out var opFailures);
        Assert.Empty(opFailures);
        var compiler = new EffectCompiler(templates.Templates, ops);

        var builder = new StringBuilder();
        builder.AppendLine("## 本机制样本（老兵／隐蔽）").AppendLine();
        builder.AppendLine("> S3 机制专项样本（**独立于**全量对拍语料——总体数据口径不变）；逐字入断言，来源供漂移核对；");
        builder.AppendLine("> 完整性档位分层规则：完整档＝无 needsCsx/csx；留痕豁免档＝含显式留痕（批 4·序列③后 V4 归完整档、豁免档为空——档位机制保留）。").AppendLine();
        builder.AppendLine("| 样本 | 原文 | 来源 | 结果类别 | 完整性档位 | 产物要点 |").AppendLine("|---|---|---|---|---|---|");

        var index = 0;
        foreach (var (id, text, source, completeness) in MechanismSamples)
        {
            var result = parser.Parse(text);
            Assert.Empty(result.Unresolved);
            Assert.NotEmpty(result.Effects);

            var templatesUsed = new List<string>();
            var details = new List<string>();
            var hasTrace = false;
            foreach (var effect in result.Effects)
            {
                templatesUsed.Add(effect.Template);
                foreach (var fill in effect.Fills.Values)
                {
                    foreach (var op in fill.Ops)
                    {
                        if (op.Op is "needsCsx" or "csx")
                        {
                            hasTrace = true;
                        }

                        var detail = op.Op;
                        if (op.Condition is { } condition)
                        {
                            detail += " [条件:" + DescribeCondition(condition) + "]";
                        }

                        if (!string.IsNullOrWhiteSpace(op.Keyword))
                        {
                            detail += " [keyword:" + op.Keyword + "]";
                        }

                        details.Add(detail);
                    }
                }

                var snapshot = compiler.Compile(effect, $"effect.mechanism.sample.{index}");
                foreach (var ev in snapshot.Root.MainTrigger.Events)
                {
                    Assert.False(string.IsNullOrWhiteSpace(ev.CsxSource), $"样本 {id} 的编译产物为空。");
                }
            }

            // 完整性档位与产物一致（不静默降级）：完整档＝无 needsCsx/csx；留痕豁免＝含显式留痕
            // （序列③起豁免档为空——机制保留以备后续）。
            if (completeness.StartsWith("完整档", StringComparison.Ordinal))
            {
                Assert.False(hasTrace, $"样本 {id} 标注完整档，但含 needsCsx/csx 产痕。");
            }
            else
            {
                Assert.True(hasTrace, $"样本 {id} 标注留痕豁免，但无 needsCsx/csx 产痕。");
            }

            index++;
            builder.Append("| ").Append(id)
                .Append(" | ").Append(text.Replace("|", "\\|", StringComparison.Ordinal))
                .Append(" | ").Append(source)
                .Append(" | ").Append(string.Join(" + ", templatesUsed))
                .Append(" | ").Append(completeness)
                .Append(" | ").Append(string.Join("；", details).Replace("|", "\\|", StringComparison.Ordinal))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    /// <summary>条件摘要（合取展开为 `a+b`；其余＝`Kind` 或 `Kind=Raw`）——供报告"产物要点"列。</summary>
    private static string DescribeCondition(DslCondition condition) =>
        condition.Kind == DslCondition.AllKind && condition.All is { Count: > 0 }
            ? string.Join("+", condition.All.Select(DescribeCondition))
            : condition.Kind + (condition.Raw is null ? string.Empty : "=" + condition.Raw);

    [Fact]
    public void Mechanism_Samples_Report()
    {
        // S3：独立手工样本集（不依赖全量语料文件）——生成独立报告（独立节口径与语料报告并入节同源）。
        var section = BuildMechanismSamplesSection();
        var directory = Path.Combine(AppContext.BaseDirectory, "EffectParsing", "report");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "mechanism-samples.md");
        File.WriteAllText(reportPath, "# 效果解析器·本机制样本报告（老兵／隐蔽）" + Environment.NewLine + Environment.NewLine + section);

        Assert.True(File.Exists(reportPath));
        var content = File.ReadAllText(reportPath);
        Assert.Contains("本机制样本（老兵／隐蔽）", content, StringComparison.Ordinal);
        Assert.Contains("V1", content, StringComparison.Ordinal);
        Assert.Contains("V4", content, StringComparison.Ordinal);
        Assert.Contains("C7", content, StringComparison.Ordinal);
    }

    // ---------- 序列③：词条行（声明产出）——独立手工样本节（与专项测试同源） ----------

    /// <summary>
    /// 生成「词条行（声明产出）」节（逐例：类别／文本／声明产出／未解析数／效果数／备注）——
    /// 样本与专项测试（<see cref="KeywordLineDeclarationTests.Samples"/>）**同源**；逐例断言随行（声明四元串、
    /// 未解析数、效果数——不静默、不误跑、不产效果单元）。
    /// </summary>
    private static string BuildKeywordLineSamplesSection()
    {
        var parser = EffectParser.CreateDefault(out var lexiconFailures);
        Assert.Empty(lexiconFailures);

        var builder = new StringBuilder();
        builder.AppendLine("## 词条行（声明产出）").AppendLine();
        builder.AppendLine("> 批 4·序列③专项样本（**独立于**全量对拍语料——总体数据口径不变；与专项测试同源）；");
        builder.AppendLine("> 逐例断言随行（声明四元／未解析数／效果数——词条行不产效果单元）。").AppendLine();
        builder.AppendLine("| 样本 | 类别 | 原文 | 声明产出 | 未解析数 | 效果数 | 备注 |").AppendLine("|---|---|---|---|---|---|---|");

        foreach (var (id, category, text, declarations, unresolved, effects, note) in KeywordLineDeclarationTests.Samples)
        {
            var result = parser.Parse(text);
            Assert.Equal(declarations, string.Join("; ", result.Declarations.Select(KeywordLineDeclarationTests.Describe)));
            Assert.Equal(unresolved, result.Unresolved.Count);
            Assert.Equal(effects, result.Effects.Count);

            builder.Append("| ").Append(id)
                .Append(" | ").Append(category)
                .Append(" | ").Append(text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal))
                .Append(" | ").Append(declarations.Length == 0 ? "（无）" : declarations.Replace("|", "\\|", StringComparison.Ordinal))
                .Append(" | ").Append(unresolved)
                .Append(" | ").Append(effects)
                .Append(" | ").Append(note.Replace("|", "\\|", StringComparison.Ordinal))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    [Fact]
    public void KeywordLine_Samples_Report()
    {
        // 序列③：词条行专项样本——独立报告文件（与语料报告并入节同源）。
        var section = BuildKeywordLineSamplesSection();
        var directory = Path.Combine(AppContext.BaseDirectory, "EffectParsing", "report");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "keyword-line-samples.md");
        File.WriteAllText(reportPath, "# 效果解析器·词条行样本报告（声明产出）" + Environment.NewLine + Environment.NewLine + section);

        Assert.True(File.Exists(reportPath));
        var content = File.ReadAllText(reportPath);
        Assert.Contains("词条行（声明产出）", content, StringComparison.Ordinal);
        Assert.Contains("K1", content, StringComparison.Ordinal);
        Assert.Contains("K13", content, StringComparison.Ordinal);
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
