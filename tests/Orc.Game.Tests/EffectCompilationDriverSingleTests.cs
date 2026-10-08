using Orc.Cards;
using Orc.Game.EffectParsing;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Templates;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 效果离线编译驱动（批 5·N2a）——单条入口验收：
/// ①卡面文本链／DSL 链端到端（快照＋JSON 文本；「单条文本＝落盘文件内容」对拍＋读回结构等值＋字节幂等）；
/// ②身份派生（基名＋声明序序号）；③失败分层（InvalidData／SourceMissing／CompileFailed／InvalidIdentity）；
/// ④留痕（Unresolved／needsCsx／占位条件）；⑤参数层 fail-fast；⑥资产三形态；⑦确定性。
/// 测试自含：临时目录/内嵌数据、不依赖 outputs、测后清理。
/// </summary>
public class EffectCompilationDriverSingleTests
{
    // ────────────── ① 全链（卡面文本）＋ 文本对拍/读回/幂等 ──────────────

    /// <summary>卡面文本全链：单效果 → 快照＋JSON 文本；「单条文本＝落盘文件内容」逐字符对拍＋读回结构等值＋写→读→写幂等。</summary>
    [Fact]
    public void 单条_卡面文本_全链_文本与落盘一致_读回结构等值()
    {
        var driver = EffectCompilationDriverTestKit.CreateDriver();
        var result = driver.CompileCardFaceText("友方单位加入时，抽一张牌。", "effect.single.full", contextLabel: "probe");

        // 产物区：1 快照＋1 JSON 文本。
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("effect.single.full", artifact.EffectId);
        Assert.Equal("effect.single.full", artifact.Snapshot.Root.Id);

        // 报告区：输入级恰好 1 条（单条＝虚拟输入）；成功 1；语义完整 1；模式 Single；文件/目录字段缺省。
        var report = result.Report;
        var input = Assert.Single(report.Inputs);
        Assert.Null(input.SourceFileName);
        Assert.Equal("probe", input.ContextLabel);
        Assert.Equal(EffectCompilationInputKind.CardFaceText, input.InputKind);
        Assert.Equal(1, input.Counts.ParsedEffectCount);
        Assert.Equal(1, input.Counts.SuccessCount);
        Assert.Equal(1, input.Counts.SemanticallyCompleteCount);
        Assert.Empty(input.OutputFiles);
        Assert.Null(input.FileFailureCategory);
        Assert.Null(input.FileFailureReason);

        var success = Assert.Single(report.Successes);
        Assert.Equal("effect.single.full", success.EffectId);
        Assert.Equal(1, success.DeclarationOrdinal);
        Assert.True(success.SemanticallyComplete);
        Assert.Null(success.OutputFile);
        Assert.Null(success.ExpectedTargetFile);

        Assert.Empty(report.Failures);
        Assert.Empty(report.UnresolvedTraces);
        Assert.Empty(report.NeedsCsxTraces);
        Assert.Empty(report.PlaceholderConditionTraces);

        Assert.Equal(EffectCompilationMode.Single, report.Summary.Mode);
        Assert.Equal(1, report.Summary.InputCount);
        Assert.Null(report.Summary.InputDirectory);
        Assert.Null(report.Summary.OutputDirectory);
        Assert.StartsWith("default:", report.Summary.AssetSource, StringComparison.Ordinal);
        Assert.Equal(1, report.Summary.Counts.SuccessCount);

        // 「单条文本＝落盘文件内容」：经批 2 落盘出口写出 → 逐字符对拍。
        var dir = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-single");
        try
        {
            PrefabWriter.SaveFile(dir, artifact.Snapshot);
            var file = Path.Combine(dir, "effect.single.full.prefab.json");
            var written = File.ReadAllText(file);
            Assert.Equal(artifact.JsonText, written);

            // 读回结构等值（对象级逐字段）＋ 再序列化逐字符（写→读→写幂等）。
            var readBack = PrefabJson.Deserialize(written);
            EffectCompilationDriverTestKit.AssertSnapshotStructureEqual(artifact.Snapshot, readBack);
            Assert.Equal(written, PrefabJson.SerializeForDisk(readBack));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>多效果：身份派生＝基名＋声明序序号（.1/.2）；同输入两次调用确定性（文本与报告序相同）。</summary>
    [Fact]
    public void 单条_多效果_身份派生与确定性()
    {
        const string text = "部署：造成1点伤害。\n亡计：抽一张牌。";
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText(text, "card.probe");

        Assert.Equal(2, result.Artifacts.Count);
        Assert.Equal(new[] { "card.probe.1", "card.probe.2" }, result.Artifacts.Select(a => a.EffectId).ToArray());
        Assert.Equal(new[] { 1, 2 }, result.Report.Successes.Select(s => s.DeclarationOrdinal).ToArray());
        Assert.Equal(new[] { "card.probe.1", "card.probe.2" },
            result.Report.Successes.Select(s => s.EffectId).ToArray());
        Assert.Equal(2, result.Report.Summary.Counts.ParsedEffectCount);
        Assert.Equal(2, result.Report.Summary.Counts.SemanticallyCompleteCount);

        // 确定性（硬断言）：同输入两次调用 ⇒ 产物文本逐字符相同＋报告条目序/字面相同。
        var again = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText(text, "card.probe");
        Assert.Equal(
            result.Artifacts.Select(a => a.JsonText),
            again.Artifacts.Select(a => a.JsonText));
        Assert.Equal(
            result.Report.Successes.Select(s => (s.EffectId, s.DeclarationOrdinal, s.SemanticallyComplete)),
            again.Report.Successes.Select(s => (s.EffectId, s.DeclarationOrdinal, s.SemanticallyComplete)));
    }

    // ────────────── ② DSL 链 ──────────────

    /// <summary>DSL 链全链：DSL JSON → 编译 → 快照＋JSON 文本（身份＝基名直接用）。</summary>
    [Fact]
    public void 单条_DSL链_全链()
    {
        var dslJson = EffectCompilationDriverTestKit.DslJsonOfText("友方单位加入时，抽一张牌。");
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileDsl(dslJson, "effect.dsl.probe");

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("effect.dsl.probe", artifact.EffectId);
        var input = Assert.Single(result.Report.Inputs);
        Assert.Equal(EffectCompilationInputKind.Dsl, input.InputKind);
        Assert.Equal(1, input.Counts.SuccessCount);

        // 与落盘文本同一真源＋读回结构等值（对象级）。
        var dir = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-dsl");
        try
        {
            PrefabWriter.SaveFile(dir, artifact.Snapshot);
            var file = Path.Combine(dir, "effect.dsl.probe.prefab.json");
            var written = File.ReadAllText(file);
            Assert.Equal(artifact.JsonText, written);

            var readBack = PrefabJson.Deserialize(written);
            EffectCompilationDriverTestKit.AssertSnapshotStructureEqual(artifact.Snapshot, readBack);
            Assert.Equal(written, PrefabJson.SerializeForDisk(readBack));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>DSL 无效＝InvalidData（文件级失败——不产出；与卡面文本侧宽容解析有意不对称）。</summary>
    [Fact]
    public void 单条_DSL链_无效数据_InvalidData()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileDsl("{ 这不是 DSL", "bad.dsl");

        Assert.Empty(result.Artifacts);
        Assert.Empty(result.Report.Successes);
        Assert.Empty(result.Report.Failures); // 文件级失败记入输入级条目（与效果级分列）

        var input = Assert.Single(result.Report.Inputs);
        Assert.Equal(EffectCompilationFailureCategory.InvalidData, input.FileFailureCategory);
        Assert.Contains("JSON 非法", input.FileFailureReason);
        Assert.Equal(1, input.Counts.FailureInvalidDataCount);
        Assert.Equal(0, input.Counts.ParsedEffectCount);
        Assert.Equal(1, result.Report.Summary.Counts.FailureInvalidDataCount);
    }

    /// <summary>模板缺失＝SourceMissing（来源未命中——不外抛）。</summary>
    [Fact]
    public void 单条_DSL链_模板缺失_来源未命中()
    {
        var dslJson = DslJson.Serialize(new DslEffectInstance(
            "no_such_template",
            new Dictionary<string, DslSlotFill>
            {
                ["on_event"] = new DslSlotFill(new[] { new DslOp("draw", count: 1) }),
            }));

        var result = EffectCompilationDriverTestKit.CreateDriver().CompileDsl(dslJson, "missing.probe");

        var failure = Assert.Single(result.Report.Failures);
        Assert.Equal(EffectCompilationFailureCategory.SourceMissing, failure.Category);
        Assert.Contains("no_such_template", failure.Reason);
        Assert.Contains("来源未命中", failure.Reason);
        Assert.Null(failure.ExpectedTargetFile);
        Assert.Empty(result.Artifacts);
    }

    /// <summary>槽位未填＝CompileFailed（原因完整包含既有层消息原文——不吞不降级）。</summary>
    [Fact]
    public void 单条_DSL链_槽位未填_编译失败_原因保留原文()
    {
        var dslJson = DslJson.Serialize(new DslEffectInstance("join_basic", new Dictionary<string, DslSlotFill>()));
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileDsl(dslJson, "incomplete.probe");

        var failure = Assert.Single(result.Report.Failures);
        Assert.Equal(EffectCompilationFailureCategory.CompileFailed, failure.Category);
        Assert.Contains("未被 DSL 填写", failure.Reason); // 既有层消息原文。
        Assert.Contains("join_basic", failure.Reason);
        Assert.Null(failure.ExpectedTargetFile);
    }

    // ────────────── ③ 身份（结构化失败 / 参数层抛出） ──────────────

    /// <summary>身份非法（含落盘非法字符）＝语义层结构化 InvalidIdentity（不产出、不抛）。</summary>
    [Fact]
    public void 单条_身份非法_结构化失败()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver()
            .CompileCardFaceText("部署：造成1点伤害。", "bad/name");

        var failure = Assert.Single(result.Report.Failures);
        Assert.Equal(EffectCompilationFailureCategory.InvalidIdentity, failure.Category);
        Assert.Equal("bad/name", failure.EffectId); // 意图身份如实呈现。
        Assert.Contains("bad/name", failure.Reason);
        Assert.Null(failure.ExpectedTargetFile); // 不得虚构落点。
        Assert.Empty(result.Artifacts);
        Assert.Equal(1, result.Report.Summary.Counts.FailureInvalidIdentityCount);
    }

    /// <summary>参数层 fail-fast：文本 null／基名 null/空白／version 非法＝抛出（与语义层失败分列）。</summary>
    [Fact]
    public void 单条_参数层_抛出()
    {
        var driver = EffectCompilationDriverTestKit.CreateDriver();

        Assert.Throws<ArgumentNullException>(() => driver.CompileCardFaceText(null!, "id"));
        Assert.Throws<ArgumentNullException>(() => driver.CompileDsl(null!, "id"));
        Assert.Throws<ArgumentException>(() => driver.CompileCardFaceText("部署：造成1点伤害。", "  "));
        Assert.Throws<ArgumentOutOfRangeException>(() => driver.CompileCardFaceText("部署：造成1点伤害。", "id", null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => driver.CompileDsl("{}", "id", -1));
    }

    /// <summary>空文本＝空成功（0 产出；不得视为失败）；输入级条目仍恰 1 条。</summary>
    [Fact]
    public void 单条_空文本_空成功()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText("", "empty.probe");

        Assert.Empty(result.Artifacts);
        Assert.Empty(result.Report.Successes);
        Assert.Empty(result.Report.Failures);
        var input = Assert.Single(result.Report.Inputs);
        Assert.Equal(0, input.Counts.ParsedEffectCount);
        Assert.Equal(1, result.Report.Summary.InputCount);
    }

    // ────────────── ④ 留痕 ──────────────

    /// <summary>未解析留痕：文本级、不失败；「文本级留痕＋0 产出」属成功档。</summary>
    [Fact]
    public void 单条_未解析_留痕_空产出成功档()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText("部署：变成晴天。", "u.probe");

        Assert.Empty(result.Artifacts);
        Assert.Empty(result.Report.Successes);
        Assert.Empty(result.Report.Failures); // 留痕≠失败。

        var trace = Assert.Single(result.Report.UnresolvedTraces);
        Assert.Equal(EffectCompilationTraceCategory.Unresolved, trace.Category);
        Assert.Null(trace.SourceFileName);
        Assert.Contains("变成晴天", trace.RawText);
        Assert.Contains("无法识别动作短语", trace.Reason);
        Assert.Equal("部署：变成晴天。".IndexOf("变成晴天", StringComparison.Ordinal), trace.Start);
        Assert.Equal("变成晴天".Length, trace.Length);

        var input = Assert.Single(result.Report.Inputs);
        Assert.Equal(1, input.Counts.TraceUnresolvedCount);
        Assert.Equal(1, result.Report.Summary.Counts.TraceUnresolvedCount);
    }

    /// <summary>needsCsx 留痕：照常产出（可编译产物）＋档位标注（语义不完整）；产出文件关联（单条＝null）。</summary>
    [Fact]
    public void 单条_needsCsx_留痕与档位()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText("部署：攻击一个敌方单位。", "n.probe");

        Assert.Single(result.Artifacts); // 照常产出。
        var trace = Assert.Single(result.Report.NeedsCsxTraces);
        Assert.Equal(EffectCompilationTraceCategory.NeedsCsx, trace.Category);
        Assert.Equal("n.probe", trace.EffectId);
        Assert.Equal(1, trace.DeclarationOrdinal);
        Assert.Equal("on_deploy[0]", trace.Location);
        Assert.Contains("攻击一个敌方单位", trace.Script);
        Assert.Null(trace.OutputFile);

        var success = Assert.Single(result.Report.Successes);
        Assert.False(success.SemanticallyComplete);
        Assert.Equal(0, result.Report.Summary.Counts.SemanticallyCompleteCount);
        Assert.Equal(1, result.Report.Summary.Counts.TraceNeedsCsxCount);
    }

    /// <summary>占位条件留痕（raw 条件）＋档位标注。</summary>
    [Fact]
    public void 单条_占位条件_留痕()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver()
            .CompileCardFaceText("部署：如果手牌不少于2张，抽一张牌。", "p.probe");

        Assert.Single(result.Artifacts);
        var trace = Assert.Single(result.Report.PlaceholderConditionTraces);
        Assert.Equal(EffectCompilationTraceCategory.PlaceholderCondition, trace.Category);
        Assert.Equal("p.probe", trace.EffectId);
        Assert.Equal("on_deploy[0].condition", trace.Location);
        Assert.Contains("不少于", trace.RawText);
        Assert.False(Assert.Single(result.Report.Successes).SemanticallyComplete);
        Assert.Equal(1, result.Report.Summary.Counts.TracePlaceholderConditionCount);
    }

    /// <summary>未解析与效果并存：部分解析＝产出成功部分，Unresolved 单列（不静默）。</summary>
    [Fact]
    public void 单条_未解析与效果并存()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver()
            .CompileCardFaceText("部署、移动、攻击时：造成2点伤害。", "mixed.probe");

        // 效果＝解析成功部分（部署）；移动/攻击＝未解析记录。
        Assert.Single(result.Report.Successes);
        Assert.Equal(2, result.Report.UnresolvedTraces.Count);
        Assert.Empty(result.Report.Failures);
        var input = Assert.Single(result.Report.Inputs);
        Assert.Equal(1, input.Counts.ParsedEffectCount);
        Assert.Equal(1, input.Counts.SuccessCount);
        Assert.Equal(2, input.Counts.TraceUnresolvedCount);
    }

    // ────────────── ⑤ 资产三形态 ──────────────

    /// <summary>内存注入（测试自含通道）：模板集合＋op 目录对象直注；资产来源标识＝memory。</summary>
    [Fact]
    public void 单条_资产_内存注入()
    {
        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        Assert.Empty(templates.Failures);
        var ops = OpTemplateCatalog.LoadDirectory(OpTemplateCatalog.DefaultDirectory, out var opFailures);
        Assert.Empty(opFailures);

        var driver = EffectCompilationDriverTestKit.CreateDriver();
        driver.UseEffectAssets(templates.Templates, ops);
        var result = driver.CompileCardFaceText("友方单位加入时，抽一张牌。", "mem.probe");

        Assert.Single(result.Artifacts);
        Assert.Equal("memory", result.Report.Summary.AssetSource);
    }

    /// <summary>显式目录覆盖＋单文件缺失（op 资产缺失＝SourceMissing；可用子集继续）。</summary>
    [Fact]
    public void 单条_资产_显式目录_缺op_来源未命中()
    {
        var tempDir = EffectCompilationDriverTestKit.CreateTempAssetDirectory();
        try
        {
            File.Delete(Path.Combine(tempDir, "ops", "draw.csx.tpl"));

            var driver = EffectCompilationDriverTestKit.CreateDriver();
            driver.UseEffectAssets(tempDir);
            var result = driver.CompileCardFaceText("友方单位加入时，抽一张牌。", "dir.probe");

            var failure = Assert.Single(result.Report.Failures);
            Assert.Equal(EffectCompilationFailureCategory.SourceMissing, failure.Category);
            Assert.Contains("draw", failure.Reason);
            Assert.Contains("op 资产缺失", failure.Reason);
            Assert.Equal("directory:" + tempDir, result.Report.Summary.AssetSource);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>资产目录不可用（不存在）＝逐条 SourceMissing（不外抛——批 4 先例口径）。</summary>
    [Fact]
    public void 单条_资产_目录不存在_来源未命中()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), "orc-missing-assets-" + Guid.NewGuid().ToString("N"));

        var driver = EffectCompilationDriverTestKit.CreateDriver();
        driver.UseEffectAssets(missingDirectory);
        var result = driver.CompileCardFaceText("友方单位加入时，抽一张牌。", "miss.probe");

        var failure = Assert.Single(result.Report.Failures);
        Assert.Equal(EffectCompilationFailureCategory.SourceMissing, failure.Category);
        Assert.Empty(result.Artifacts);
    }

    /// <summary>资产变更必反映（内容指纹语义）：op 模板内容变化 → 同输入再编译反映新资产；资产稳定＝产物稳定。</summary>
    [Fact]
    public void 单条_资产变更必反映()
    {
        var tempDir = EffectCompilationDriverTestKit.CreateTempAssetDirectory();
        try
        {
            var driver = EffectCompilationDriverTestKit.CreateDriver();
            driver.UseEffectAssets(tempDir);

            var first = driver.CompileCardFaceText("友方单位加入时，抽一张牌。", "fp.probe");
            var firstCsx = Assert.Single(Assert.Single(first.Artifacts).Snapshot.Root.MainTrigger.Events).CsxSource;

            // 变更资产内容：draw op 模板抽 count + 1 张。
            var drawFile = Path.Combine(tempDir, "ops", "draw.csx.tpl");
            var original = File.ReadAllText(drawFile);
            File.WriteAllText(drawFile, original.Replace("{{count}}", "{{count}} + 1", StringComparison.Ordinal));

            var second = driver.CompileCardFaceText("友方单位加入时，抽一张牌。", "fp.probe");
            var secondCsx = Assert.Single(Assert.Single(second.Artifacts).Snapshot.Root.MainTrigger.Events).CsxSource;
            Assert.NotEqual(firstCsx, secondCsx); // 资产变更必反映。

            // 资产不再变化：再编译与上一次一致（确定性）。
            var third = driver.CompileCardFaceText("友方单位加入时，抽一张牌。", "fp.probe");
            var thirdCsx = Assert.Single(Assert.Single(third.Artifacts).Snapshot.Root.MainTrigger.Events).CsxSource;
            Assert.Equal(secondCsx, thirdCsx);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
