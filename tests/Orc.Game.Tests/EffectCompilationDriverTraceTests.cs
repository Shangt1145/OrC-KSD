using Orc.Cards;
using Orc.Game.EffectParsing;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Parsing;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 效果离线编译驱动（批 5·N2a）——留痕与声明区验收：
/// ①留痕三类分列（独立清单、类别字面稳定）；②内嵌递归（嵌套 needCsx/占位条件计入顶层效果档位）；
/// ③模板固定 csx 不计入留痕（反例）；④csx 逃生舱留痕；⑤词条行声明（独立信息区、行号、不计入效果计）。
/// 测试自含：临时目录/内嵌数据、不依赖 outputs、测后清理。
/// </summary>
public class EffectCompilationDriverTraceTests
{
    /// <summary>内嵌 effects 递归：嵌套里的 needsCsx 计入顶层效果档位与留痕清单（锚点＝内嵌路径）。</summary>
    [Fact]
    public void 内嵌_needsCsx_递归计入顶层()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver()
            .CompileCardFaceText("部署：获得：“亡计：攻击一个敌方单位。”", "nest.probe");

        var artifact = Assert.Single(result.Artifacts); // 照常产出。
        var trace = Assert.Single(result.Report.NeedsCsxTraces);
        Assert.Equal(EffectCompilationTraceCategory.NeedsCsx, trace.Category);
        Assert.Equal("nest.probe", trace.EffectId); // 锚点＝顶层效果身份。
        Assert.Equal(1, trace.DeclarationOrdinal);
        Assert.StartsWith("on_deploy[0].nested[0]", trace.Location, StringComparison.Ordinal); // 内嵌路径。
        Assert.Contains("攻击一个敌方单位", trace.Script);

        var success = Assert.Single(result.Report.Successes);
        Assert.False(success.SemanticallyComplete); // 内嵌留痕计入顶层档位。
        Assert.Equal("nest.probe", artifact.EffectId);
    }

    /// <summary>内嵌 effects 递归：嵌套里的占位条件计入顶层效果档位与留痕清单。</summary>
    [Fact]
    public void 内嵌_占位条件_递归计入顶层()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver()
            .CompileCardFaceText("部署：获得：“亡计：如果手牌不少于2张，抽一张牌。”", "nestp.probe");

        Assert.Single(result.Artifacts);
        var trace = Assert.Single(result.Report.PlaceholderConditionTraces);
        Assert.Equal(EffectCompilationTraceCategory.PlaceholderCondition, trace.Category);
        Assert.Equal("nestp.probe", trace.EffectId);
        Assert.StartsWith("on_deploy[0].nested[0]", trace.Location, StringComparison.Ordinal);
        Assert.Contains("不少于", trace.RawText);
        Assert.False(Assert.Single(result.Report.Successes).SemanticallyComplete);
    }

    /// <summary>模板固定 csx 不计入留痕（反例）：编译产物含 csx（模板/渲染），但无留痕、判语义完整。</summary>
    [Fact]
    public void 模板固定csx_不计入留痕()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText("友方单位加入时，抽一张牌。", "csx.neg");

        var artifact = Assert.Single(result.Artifacts);
        Assert.NotEmpty(artifact.Snapshot.Root.MainTrigger.Events);
        Assert.All(artifact.Snapshot.Root.MainTrigger.Events, e => Assert.False(string.IsNullOrWhiteSpace(e.CsxSource)));

        Assert.Empty(result.Report.NeedsCsxTraces);
        Assert.Empty(result.Report.PlaceholderConditionTraces);
        Assert.True(Assert.Single(result.Report.Successes).SemanticallyComplete);
        Assert.Equal(1, result.Report.Summary.Counts.SemanticallyCompleteCount);
    }

    /// <summary>同一效果带 needsCsx 与占位条件：两类留痕各自独立记录（不互斥——同一效果既成功又带多类留痕）。</summary>
    [Fact]
    public void 三类留痕_独立并存()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver()
            .CompileCardFaceText("部署：如果手牌不少于2张，攻击一个敌方单位。", "both.probe");

        Assert.Single(result.Artifacts);
        var needsCsx = Assert.Single(result.Report.NeedsCsxTraces);
        var placeholder = Assert.Single(result.Report.PlaceholderConditionTraces);
        Assert.Equal(EffectCompilationTraceCategory.NeedsCsx, needsCsx.Category);
        Assert.Equal("both.probe", needsCsx.EffectId);
        Assert.Equal("both.probe", placeholder.EffectId);
        Assert.Contains("攻击一个敌方单位", needsCsx.Script);
        Assert.Contains("不少于", placeholder.RawText);

        var input = Assert.Single(result.Report.Inputs);
        Assert.Equal(1, input.Counts.TraceNeedsCsxCount);
        Assert.Equal(1, input.Counts.TracePlaceholderConditionCount);
        Assert.False(Assert.Single(result.Report.Successes).SemanticallyComplete);
    }

    /// <summary>csx 逃生舱 op（DSL 链）：照常产出＋留痕（类别字面 csx、原文＝脚本）。</summary>
    [Fact]
    public void csx逃生舱_留痕()
    {
        var dslJson = DslJson.Serialize(new DslEffectInstance(
            "deploy_basic",
            new Dictionary<string, DslSlotFill>
            {
                ["on_deploy"] = new DslSlotFill(new[] { new DslOp("csx", script: "// custom handler") }),
            }));

        var result = EffectCompilationDriverTestKit.CreateDriver().CompileDsl(dslJson, "csx.probe");

        Assert.Single(result.Artifacts);
        var trace = Assert.Single(result.Report.NeedsCsxTraces);
        Assert.Equal(EffectCompilationTraceCategory.Csx, trace.Category);
        Assert.Equal("csx.probe", trace.EffectId);
        Assert.Equal("on_deploy[0]", trace.Location);
        Assert.Equal("// custom handler", trace.Script);
        Assert.False(Assert.Single(result.Report.Successes).SemanticallyComplete);
    }

    /// <summary>词条行声明：独立信息区（维度/标识/参值/注册状态/行序）；不计入效果计数。</summary>
    [Fact]
    public void 声明区_词条行()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver()
            .CompileCardFaceText("部署：造成1点伤害。\n闪击\n亡计：抽一张牌。", "decl.probe");

        Assert.Equal(2, result.Report.Successes.Count); // 效果面不受声明影响。
        var declaration = Assert.Single(result.Report.Declarations);
        Assert.Equal(DeclarationDimension.Keyword, declaration.Dimension);
        Assert.Equal("闪击", declaration.Id);
        Assert.Null(declaration.Value);
        Assert.Equal(DeclarationRegistration.Registered, declaration.Registration);
        Assert.Equal(2, declaration.Line); // 行序（1 起——第 2 行）。
        Assert.Equal("部署：造成1点伤害。\n闪击\n亡计：抽一张牌。".IndexOf("闪击", StringComparison.Ordinal), declaration.Start);
        Assert.Equal("闪击".Length, declaration.Length);

        var input = Assert.Single(result.Report.Inputs);
        Assert.Equal(1, input.Counts.DeclarationCount);
        Assert.Equal(2, input.Counts.ParsedEffectCount); // 声明不计入效果数。
        Assert.Equal(1, result.Report.Summary.Counts.DeclarationCount);
    }

    /// <summary>只有词条行、0 效果：空成功＋声明区仍可见（不静默丢弃）。</summary>
    [Fact]
    public void 声明区_只有词条行_空成功()
    {
        var result = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText("闪击", "declonly.probe");

        Assert.Empty(result.Artifacts);
        Assert.Empty(result.Report.Successes);
        Assert.Empty(result.Report.Failures);

        var declaration = Assert.Single(result.Report.Declarations);
        Assert.Equal("闪击", declaration.Id);
        Assert.Equal(1, declaration.Line);
        Assert.Equal(1, result.Report.Summary.Counts.DeclarationCount);
        Assert.Equal(0, result.Report.Summary.Counts.ParsedEffectCount);
    }

    /// <summary>留痕条目的落点承载：事实字段（落盘成功回填／dry-run null）＋预期字段（dry-run 且提供输出目录＝必填、与成功条目一致——同第 6 轮规则、无例外）。</summary>
    [Fact]
    public void 留痕_落点承载_事实与预期字段()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-trace-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-trace-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "warn.txt", "部署：攻击一个敌方单位。");

            // 落盘：事实字段＝产出文件（确有产出）；预期字段＝可缺省（null——不与事实字段混淆）。
            var disk = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);
            var trace = Assert.Single(disk.Report.NeedsCsxTraces);
            Assert.Equal(Path.Combine(tempOut, "warn.prefab.json"), trace.OutputFile);
            Assert.Null(trace.ExpectedTargetFile);

            // dry-run＋提供输出目录：无产出＝null（事实语义——不得虚构）；预期字段＝必填且与成功条目一致。
            var dry = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut, dryRun: true);
            var dryTrace = Assert.Single(dry.Report.NeedsCsxTraces);
            Assert.Null(dryTrace.OutputFile);
            var drySuccess = Assert.Single(dry.Report.Successes);
            Assert.Equal(Path.Combine(tempOut, "warn.prefab.json"), dryTrace.ExpectedTargetFile);
            Assert.Equal(drySuccess.ExpectedTargetFile, dryTrace.ExpectedTargetFile);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
            if (Directory.Exists(tempOut))
            {
                Directory.Delete(tempOut, recursive: true);
            }
        }
    }

    /// <summary>留痕落点承载·落盘失败（IoFailed）：预期字段必填（同失败条目路径）；事实字段＝null（无产出）。</summary>
    [Fact]
    public void 留痕_落盘失败_预期路径必填()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-traceio-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-traceio-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "warn.txt", "部署：攻击一个敌方单位。");

            var first = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);
            Assert.Single(first.Report.Successes);

            var lockedTarget = Path.Combine(tempOut, "warn.prefab.json");
            EffectCompilationResult second;
            using (new FileStream(lockedTarget, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                second = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);
            }

            var failure = Assert.Single(second.Report.Failures);
            Assert.Equal(EffectCompilationFailureCategory.IoFailed, failure.Category);
            var trace = Assert.Single(second.Report.NeedsCsxTraces);
            Assert.Null(trace.OutputFile);
            Assert.Equal(lockedTarget, trace.ExpectedTargetFile); // 必填（同失败条目路径）。
            Assert.Equal(failure.ExpectedTargetFile, trace.ExpectedTargetFile);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
            if (Directory.Exists(tempOut))
            {
                Directory.Delete(tempOut, recursive: true);
            }
        }
    }

    /// <summary>失败与留痕并存（三区互不吞并）：失败效果可在失败区一条＋留痕区若干条；无产出＝留痕产出文件 null。</summary>
    [Fact]
    public void 失败与留痕并存_互不吞并()
    {
        var tempAssets = EffectCompilationDriverTestKit.CreateTempAssetDirectory();
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-failtrace-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-failtrace-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.Delete(Path.Combine(tempAssets, "deploy_basic.tpl.json")); // 模板缺失 → SourceMissing。
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "warn.txt", "部署：攻击一个敌方单位。");

            var driver = EffectCompilationDriverTestKit.CreateDriver();
            driver.UseEffectAssets(tempAssets);
            var result = driver.CompileDirectory(tempIn, tempOut);

            var failure = Assert.Single(result.Report.Failures);
            Assert.Equal(EffectCompilationFailureCategory.SourceMissing, failure.Category);
            Assert.Equal("warn", failure.EffectId);

            var trace = Assert.Single(result.Report.NeedsCsxTraces); // 失败不影响留痕照收（不吞）。
            Assert.Equal(EffectCompilationTraceCategory.NeedsCsx, trace.Category);
            Assert.Equal("warn", trace.EffectId); // 与失败条目同一关联键（源文件＋身份＋序号）。
            Assert.Equal(1, trace.DeclarationOrdinal);
            Assert.Null(trace.OutputFile); // 无产出（失败）＝null。
            Assert.Null(trace.ExpectedTargetFile); // 编译失败（SourceMissing）＝其余可缺省（不得虚构）。

            Assert.Empty(result.Report.Successes);
            Assert.Equal(1, result.Report.Summary.Counts.FailureSourceMissingCount);
            Assert.Equal(1, result.Report.Summary.Counts.TraceNeedsCsxCount);
        }
        finally
        {
            Directory.Delete(tempAssets, recursive: true);
            Directory.Delete(tempIn, recursive: true);
            if (Directory.Exists(tempOut))
            {
                Directory.Delete(tempOut, recursive: true);
            }
        }
    }
}
