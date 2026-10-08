using Orc.Cards;
using Orc.Core;
using Orc.Game.EffectParsing;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 效果离线编译驱动（批 5·N2a）——目录级批量验收：
/// ①落盘（两链写盘、读回、字节幂等）；②dry-run（零写、内容必携、预期路径）；③失败隔离（坏文件/读失败/落盘失败）；
/// ④批内重复（跨类型同 id——首见占位＋重复失败；dry-run 同判）；⑤混放/大小写/非约定文件；⑥边界（空目录/目录缺失/参数层）；
/// ⑦三层对账（汇总 ↔ 输入级 ↔ 效果级计数互核）；⑧两模式判定一致性。
/// 测试自含：临时目录/内嵌数据、不依赖 outputs、测后清理。
/// </summary>
public class EffectCompilationDriverBatchTests
{
    private const string JoinText = "友方单位加入时，抽一张牌。";
    private const string DeployText = "部署：造成1点伤害。";
    private const string DeathText = "亡计：抽一张牌。";

    // ────────────── ① 落盘（两链）＋ 读回 ＋ 幂等 ──────────────

    /// <summary>批量落盘·两链（卡面文本＋DSL）：写盘＜派生id＞.prefab.json → 读回结构等值 → 写→读→写字节幂等；与单条文本同款对拍。</summary>
    [Fact]
    public void 批量_落盘_两链_读回_字节幂等()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-batch-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-batch-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", JoinText);
            EffectCompilationDriverTestKit.WriteInputFile(
                tempIn, "b.dsl.json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText));

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);

            // 落盘模式：产物区默认不携带（内容以磁盘为准）。
            Assert.Empty(result.Artifacts);

            // 成功区：2 条（a、b）——产出文件＝实际写入文件（事实字段）。
            Assert.Equal(new[] { "a", "b" }, result.Report.Successes.Select(s => s.EffectId).ToArray());
            foreach (var success in result.Report.Successes)
            {
                Assert.Equal(Path.Combine(tempOut, success.EffectId + ".prefab.json"), success.OutputFile);
                Assert.Null(success.ExpectedTargetFile);
            }

            // 文件确实写出＋输入级产出文件列表。
            var fileA = Path.Combine(tempOut, "a.prefab.json");
            var fileB = Path.Combine(tempOut, "b.prefab.json");
            Assert.True(File.Exists(fileA));
            Assert.True(File.Exists(fileB));
            var inputA = Assert.Single(result.Report.Inputs, i => i.SourceFileName == "a.txt");
            Assert.Equal(new[] { fileA }, inputA.OutputFiles);

            // 读回结构等值（对象级）＋「单条文本＝落盘文件内容」对拍＋写→读→写字节幂等。
            var written = File.ReadAllText(fileA);
            var single = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText(JoinText, "a");
            Assert.Equal(Assert.Single(single.Artifacts).JsonText, written);

            var readBack = PrefabJson.Deserialize(written);
            EffectCompilationDriverTestKit.AssertSnapshotStructureEqual(
                Assert.Single(single.Artifacts).Snapshot, readBack);
            Assert.Equal(written, PrefabJson.SerializeForDisk(readBack));

            // 产物可经既有读面读入（目录读取面——PrefabManager.LoadDirectory）。
            var engine = new LogicEngine();
            var load = engine.Prefabs.LoadDirectory(tempOut);
            Assert.Equal(2, load.Loaded);
            Assert.Empty(load.Failures);
            Assert.True(engine.Prefabs.TryGetPrefab("a", out var loadedPrefab));
            EffectCompilationDriverTestKit.AssertSnapshotStructureEqual(
                Assert.Single(single.Artifacts).Snapshot, loadedPrefab);

            Assert.Equal(EffectCompilationMode.Disk, result.Report.Summary.Mode);
            Assert.Equal(tempIn, result.Report.Summary.InputDirectory);
            Assert.Equal(tempOut, result.Report.Summary.OutputDirectory);
            Assert.Equal(2, result.Report.Summary.InputCount);
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

    // ────────────── ② dry-run ──────────────

    /// <summary>dry-run：输出侧零写（目录不创建）＋产物内容必携（与落盘内容逐字符一致）＋预期路径必填。</summary>
    [Fact]
    public void 批量_dry_run_零写_内容必携_预期路径()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-dry-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-dry-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", JoinText);
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "b.dsl.json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText));

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut, dryRun: true);

            // 零写：输出目录未被创建、未被探测。
            Assert.False(Directory.Exists(tempOut));

            // 产物内容必携（预览即产出）——与落盘内容逐字符一致（单一真源）。
            Assert.Equal(2, result.Artifacts.Count);
            var single = EffectCompilationDriverTestKit.CreateDriver().CompileCardFaceText(JoinText, "a");
            Assert.Equal(Assert.Single(single.Artifacts).JsonText, result.Artifacts[0].JsonText);

            // 预期目标文件路径＝纯字符串推导（成功条目必填）。
            foreach (var success in result.Report.Successes)
            {
                Assert.Null(success.OutputFile); // 无产出（事实）。
                Assert.Equal(Path.Combine(tempOut, success.EffectId + ".prefab.json"), success.ExpectedTargetFile);
            }

            Assert.Equal(EffectCompilationMode.DryRun, result.Report.Summary.Mode);
            Assert.Equal(tempOut, result.Report.Summary.OutputDirectory);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    /// <summary>dry-run 未提供输出目录：允许（不落盘＝不需要目标目录）；预期路径＝null（不得虚构）。</summary>
    [Fact]
    public void 批量_dry_run_未提供输出目录_预期路径缺省()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-dry2-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, outputDirectory: null, dryRun: true);

            var success = Assert.Single(result.Report.Successes);
            Assert.Null(success.ExpectedTargetFile);
            Assert.Single(result.Artifacts);
            Assert.Null(result.Report.Summary.OutputDirectory);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    // ────────────── ③ 失败隔离 ──────────────

    /// <summary>单条失败隔离：坏 DSL 文件（InvalidData）不影响其余文件继续。</summary>
    [Fact]
    public void 批量_失败隔离_坏DSL_继续()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-iso-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-iso-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "bad.dsl.json", "{ 这不是 DSL");
            EffectCompilationDriverTestKit.WriteInputFile(
                tempIn, "c.dsl.json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText));

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);

            Assert.Equal(new[] { "a", "c" }, result.Report.Successes.Select(s => s.EffectId).ToArray());
            Assert.Empty(result.Report.Failures); // 文件级失败不进效果级失败区。

            var bad = Assert.Single(result.Report.Inputs, i => i.SourceFileName == "bad.dsl.json");
            Assert.Equal(EffectCompilationFailureCategory.InvalidData, bad.FileFailureCategory);
            Assert.Contains("JSON 非法", bad.FileFailureReason);
            Assert.Equal(1, bad.Counts.FailureInvalidDataCount);

            Assert.True(File.Exists(Path.Combine(tempOut, "a.prefab.json")));
            Assert.True(File.Exists(Path.Combine(tempOut, "c.prefab.json")));
            Assert.False(File.Exists(Path.Combine(tempOut, "bad.prefab.json")));
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

    /// <summary>读失败（IO）＝单条隔离：该文件进输入级 IoFailed（既有层消息原文），其余照常。</summary>
    [Fact]
    public void 批量_读失败_输入级隔离()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-readfail-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-readfail-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "ok.txt", DeployText);
            var lockedPath = Path.Combine(tempIn, "locked.txt");
            File.WriteAllText(lockedPath, DeployText);

            EffectCompilationResult result;
            using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);
            }

            Assert.Single(result.Report.Successes); // ok.txt 照常。
            Assert.Empty(result.Report.Failures);

            var locked = Assert.Single(result.Report.Inputs, i => i.SourceFileName == "locked.txt");
            Assert.Equal(EffectCompilationFailureCategory.IoFailed, locked.FileFailureCategory);
            Assert.Contains("读取失败", locked.FileFailureReason);
            Assert.Equal(1, locked.Counts.FailureIoFailedCount);
            Assert.Equal(1, result.Report.Summary.Counts.FailureIoFailedCount);
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

    /// <summary>落盘失败（输出写入）＝效果级 IoFailed 隔离：预期目标路径必填、其余文件照写、既有层消息原文保留。</summary>
    [Fact]
    public void 批量_落盘失败_IoFailed_隔离()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-iofail-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-iofail-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "z.txt", JoinText);

            // 首轮正常落盘（生成既有文件）。
            var first = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);
            Assert.Equal(2, first.Report.Successes.Count);

            // 锁定 a 的目标文件 → 再落盘：a 写失败（隔离）、z 照常覆盖。
            var lockedTarget = Path.Combine(tempOut, "a.prefab.json");
            EffectCompilationResult second;
            using (new FileStream(lockedTarget, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                second = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);
            }

            var failure = Assert.Single(second.Report.Failures);
            Assert.Equal(EffectCompilationFailureCategory.IoFailed, failure.Category);
            Assert.Equal("a", failure.EffectId);
            Assert.Equal(lockedTarget, failure.ExpectedTargetFile); // 落盘失败＝预期路径必填。
            Assert.Contains("写入失败", failure.Reason);

            Assert.Equal(new[] { "z" }, second.Report.Successes.Select(s => s.EffectId).ToArray());
            Assert.Empty(second.Artifacts);
            Assert.Equal(1, second.Report.Summary.Counts.FailureIoFailedCount);
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

    // ────────────── ④ 批内重复（跨类型同 id） ──────────────

    /// <summary>跨类型同 id 重复（a.txt 与 a.dsl.json 同名）：首见占位、重复者失败（dry-run 与落盘同判）。</summary>
    [Fact]
    public void 批量_跨类型同id_首见占位_重复失败()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-dup-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-dup-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(
                tempIn, "a.dsl.json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText));

            // dry-run 也必判（重复＝编译产物层事实，与落盘无关——不能因不落盘丢失）。
            var dry = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, null, dryRun: true);
            var dryFailure = Assert.Single(dry.Report.Failures);
            Assert.Equal(EffectCompilationFailureCategory.DuplicateIdentity, dryFailure.Category);

            // 落盘：首见写＋重复失败（沿批 2 口径）。
            var disk = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);

            // Ordinal 文件名序：'a.dsl.json' 先于 'a.txt'（'.d' < '.t'）⇒ 首见＝a.dsl.json。
            var success = Assert.Single(disk.Report.Successes);
            Assert.Equal("a", success.EffectId);
            Assert.Equal("a.dsl.json", success.SourceFileName);

            var failure = Assert.Single(disk.Report.Failures);
            Assert.Equal(EffectCompilationFailureCategory.DuplicateIdentity, failure.Category);
            Assert.Equal("a", failure.EffectId);
            Assert.Equal("a.txt", failure.SourceFileName);
            Assert.Null(failure.ExpectedTargetFile); // 重复＝不得虚构落点。
            Assert.Contains("a.dsl.json", failure.Reason); // 与谁重复（首见者源文件名）。
            Assert.Contains("派生身份 'a'", failure.Reason); // 与谁重复（首见者派生身份）。

            // 仅首见者写出（1 个文件）。
            Assert.Equal(new[] { Path.Combine(tempOut, "a.prefab.json") },
                Directory.EnumerateFiles(tempOut, "*.prefab.json").ToArray());
            Assert.Equal(1, disk.Report.Summary.Counts.FailureDuplicateIdentityCount);
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

    // ────────────── ⑤ 混放/大小写/非约定文件 ──────────────

    /// <summary>非约定文件跳过且不计入任何计数（含汇总「输入数」）。</summary>
    [Fact]
    public void 批量_非约定文件跳过_输入数对账()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-skip-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "notes.md", "无关文件");
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "x.prefab.json", "{}");

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, null, dryRun: true);

            Assert.Equal(1, result.Report.Summary.InputCount);
            Assert.Equal("a.txt", Assert.Single(result.Report.Inputs).SourceFileName);
            Assert.Single(result.Report.Successes);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    /// <summary>约定扩展名匹配大小写不敏感（A.TXT／b.Dsl.Json 均受理；基名保留原大小写）。</summary>
    [Fact]
    public void 批量_大小写不敏感匹配()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-case-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "A.TXT", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(
                tempIn, "b.Dsl.Json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText));

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, null, dryRun: true);

            Assert.Equal(new[] { "A", "b" }, result.Report.Successes.Select(s => s.EffectId).ToArray());
            Assert.Equal(2, result.Report.Summary.InputCount);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    // ────────────── ⑥ 边界 ──────────────

    /// <summary>空目录＝空成功（0 产出＋空报告）；目录缺失＝整体性抛出（两模式一致）。</summary>
    [Fact]
    public void 批量_空目录_空成功_与目录缺失抛出()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-empty-in");
        try
        {
            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, null, dryRun: true);
            Assert.Empty(result.Report.Inputs);
            Assert.Empty(result.Report.Successes);
            Assert.Empty(result.Artifacts);
            Assert.Equal(0, result.Report.Summary.InputCount);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }

        var missing = Path.Combine(Path.GetTempPath(), "orc-driver-missing-" + Guid.NewGuid().ToString("N"));
        var driver = EffectCompilationDriverTestKit.CreateDriver();
        Assert.Throws<DirectoryNotFoundException>(() => driver.CompileDirectory(missing));
        Assert.Throws<DirectoryNotFoundException>(() => driver.CompileDirectory(missing, null, dryRun: true));
    }

    /// <summary>落盘模式输出目录 null/空白＝参数层抛出；dry-run 允许缺省（不落盘＝不需要目标目录）。</summary>
    [Fact]
    public void 批量_落盘缺输出目录_参数层抛()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-arg-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            var driver = EffectCompilationDriverTestKit.CreateDriver();

            Assert.Throws<ArgumentException>(() => driver.CompileDirectory(tempIn));
            Assert.Throws<ArgumentException>(() => driver.CompileDirectory(tempIn, "   "));
            Assert.Throws<ArgumentException>(() => driver.CompileDirectory(tempIn, "  ", dryRun: false));
            Assert.Throws<ArgumentException>(() => driver.CompileDirectory("  ", "out"));

            // dry-run 无目录＝合法。
            var result = driver.CompileDirectory(tempIn, null, dryRun: true);
            Assert.Single(result.Report.Successes);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    /// <summary>输出目录自动创建（多级）；同目录就地编译——产出 *.prefab.json 不被同次/下次扫描当输入。</summary>
    [Fact]
    public void 批量_输出目录创建_就地编译_不回归扫描()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-inplace-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);

            // 就地编译：输出＝输入目录。
            var first = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempIn);
            Assert.Single(first.Report.Successes);
            Assert.True(File.Exists(Path.Combine(tempIn, "a.prefab.json")));

            // 第二轮：产出不会被扫描当输入（输入数仍 1）；且跨运行同 id＝内容覆盖。
            var second = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempIn);
            Assert.Equal(1, second.Report.Summary.InputCount);
            Assert.Equal("a.txt", Assert.Single(second.Report.Inputs).SourceFileName);
            Assert.Single(second.Report.Successes);
            Assert.Empty(second.Report.Failures);

            // 多级输出目录自动创建。
            var nested = Path.Combine(tempIn, "nested", "deep", "out");
            var third = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, nested);
            Assert.Single(third.Report.Successes);
            Assert.True(File.Exists(Path.Combine(nested, "a.prefab.json")));
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    /// <summary>输出目录准备失败（路径被文件占用）＝整体性抛出（沿批 2 口径——不依赖单条内容）。</summary>
    [Fact]
    public void 批量_输出目录准备失败_整体性抛出()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-dirfail-in");
        var tempRoot = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-dirfail-root");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            var fileAsDir = Path.Combine(tempRoot, "occupied");
            File.WriteAllText(fileAsDir, "占位文件（非目录）");

            Assert.ThrowsAny<IOException>(
                () => EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, fileAsDir));
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>批内序＝Ordinal 文件名序统一排队（报告条目序同源）。</summary>
    [Fact]
    public void 批量_文件处理序_Ordinal文件名序()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-order-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "z.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "m.txt", DeployText);

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, null, dryRun: true);

            Assert.Equal(new[] { "a.txt", "m.txt", "z.txt" },
                result.Report.Inputs.Select(i => i.SourceFileName).ToArray());
            Assert.Equal(new[] { "a", "m", "z" }, result.Report.Successes.Select(s => s.EffectId).ToArray());
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    /// <summary>资产目录不可用（显式覆盖）＝逐条 SourceMissing（不外抛——单条/批量同口径）。</summary>
    [Fact]
    public void 批量_资产目录不可用_逐条来源未命中()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-noasset-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(
                tempIn, "b.dsl.json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText));

            var missing = Path.Combine(Path.GetTempPath(), "orc-driver-noassets-" + Guid.NewGuid().ToString("N"));
            var driver = EffectCompilationDriverTestKit.CreateDriver();
            driver.UseEffectAssets(missing);

            var result = driver.CompileDirectory(tempIn, null, dryRun: true);

            Assert.Equal(2, result.Report.Failures.Count);
            Assert.All(result.Report.Failures,
                f => Assert.Equal(EffectCompilationFailureCategory.SourceMissing, f.Category));
            Assert.Empty(result.Report.Successes);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    /// <summary>批量身份非法（空白基名——两条输入路径同口径）：该文件所有效果全部失败（逐效果分条——派生序号不改变判定）。</summary>
    [Fact]
    public void 批量_空白基名_全效果InvalidIdentity()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-blank-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, ".txt", DeployText + "\n" + DeathText);
            EffectCompilationDriverTestKit.WriteInputFile(
                tempIn, ".dsl.json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText));

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, null, dryRun: true);

            Assert.Empty(result.Report.Successes);
            Assert.Equal(3, result.Report.Failures.Count); // 2（.txt）＋1（.dsl.json）——逐效果分条。
            Assert.All(result.Report.Failures,
                f => Assert.Equal(EffectCompilationFailureCategory.InvalidIdentity, f.Category));
            Assert.All(result.Report.Failures, f => Assert.Null(f.ExpectedTargetFile)); // 不得虚构落点。

            var txtFailures = result.Report.Failures.Where(f => f.SourceFileName == ".txt").ToArray();
            Assert.Equal(new[] { ".1", ".2" }, txtFailures.Select(f => f.EffectId).ToArray()); // 原始基名＋派生序号。
            var dslFailure = Assert.Single(result.Report.Failures, f => f.SourceFileName == ".dsl.json");
            Assert.Equal("", dslFailure.EffectId); // 空基名如实呈现。

            Assert.Equal(3, result.Report.Summary.Counts.FailureInvalidIdentityCount);
            Assert.Equal(3, result.Report.Summary.Counts.ParsedEffectCount);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    // ────────────── ⑦ 三层对账 ──────────────

    /// <summary>三层对账（汇总 ↔ 输入级 ↔ 效果级计数互核——逐层直读、逐维度一致）。</summary>
    [Fact]
    public void 批量_三层对账_计数不变量()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-recon-in");
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "ok.txt", DeployText + "\n" + DeathText);
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "warn.txt", "部署：如果手牌不少于2张，抽一张牌。");
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "bad.dsl.json", "{ 这不是 DSL");
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "decl.txt", "闪击\n" + DeployText + "\n" + DeathText);

            var result = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, null, dryRun: true);
            var report = result.Report;
            var summary = report.Summary.Counts;

            // 输入数＝输入级条目数。
            Assert.Equal(report.Inputs.Count, report.Summary.InputCount);
            Assert.Equal(4, report.Summary.InputCount);

            // Σ输入级 ≡ 汇总（逐维度直读——全部计数维度）。
            Assert.Equal(report.Inputs.Sum(i => i.Counts.ParsedEffectCount), summary.ParsedEffectCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.SuccessCount), summary.SuccessCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.SemanticallyCompleteCount), summary.SemanticallyCompleteCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.FailureInvalidDataCount), summary.FailureInvalidDataCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.FailureSourceMissingCount), summary.FailureSourceMissingCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.FailureCompileFailedCount), summary.FailureCompileFailedCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.FailureInvalidIdentityCount), summary.FailureInvalidIdentityCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.FailureDuplicateIdentityCount), summary.FailureDuplicateIdentityCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.FailureIoFailedCount), summary.FailureIoFailedCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.TraceUnresolvedCount), summary.TraceUnresolvedCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.TraceNeedsCsxCount), summary.TraceNeedsCsxCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.TracePlaceholderConditionCount), summary.TracePlaceholderConditionCount);
            Assert.Equal(report.Inputs.Sum(i => i.Counts.DeclarationCount), summary.DeclarationCount);

            // 汇总 ≡ 效果级清单（逐维度）。
            Assert.Equal(report.Successes.Count, summary.SuccessCount);
            Assert.Equal(report.UnresolvedTraces.Count, summary.TraceUnresolvedCount);
            Assert.Equal(report.NeedsCsxTraces.Count, summary.TraceNeedsCsxCount);
            Assert.Equal(report.PlaceholderConditionTraces.Count, summary.TracePlaceholderConditionCount);
            Assert.Equal(report.Declarations.Count, summary.DeclarationCount);
            Assert.Equal(
                report.Inputs.Count(i => i.FileFailureCategory == EffectCompilationFailureCategory.InvalidData)
                    + report.Failures.Count(f => f.Category == EffectCompilationFailureCategory.InvalidData),
                summary.FailureInvalidDataCount);

            // 不变量：语义完整 ≤ 成功；成功＋失败（效果级）＝解析效果数。
            Assert.True(summary.SemanticallyCompleteCount <= summary.SuccessCount);
            Assert.Equal(summary.ParsedEffectCount, report.Successes.Count + report.Failures.Count);

            // 场景事实：ok(2 成功) ＋ warn(1 成功带留痕) ＋ bad(文件级失败) ＋ decl(2 成功＋1 声明)。
            Assert.Equal(5, summary.ParsedEffectCount);
            Assert.Equal(5, summary.SuccessCount);
            Assert.Equal(4, summary.SemanticallyCompleteCount); // ok×2 ＋ decl×2。
            Assert.Equal(1, summary.TracePlaceholderConditionCount);
            Assert.Equal(1, summary.FailureInvalidDataCount);
            Assert.Equal(1, summary.DeclarationCount);
        }
        finally
        {
            Directory.Delete(tempIn, recursive: true);
        }
    }

    // ────────────── ⑧ 两模式判定一致性 ──────────────

    /// <summary>「模式只控制动作、不改变判定」：同输入两模式——成功集合/失败（类别+身份）集合一致（差异仅 IoFailed 不发生）。</summary>
    [Fact]
    public void 批量_dry_run与落盘_判定一致()
    {
        var tempIn = EffectCompilationDriverTestKit.CreateTempDirectory("orc-driver-parity-in");
        var tempOut = Path.Combine(Path.GetTempPath(), "orc-driver-parity-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "a.txt", DeployText);
            EffectCompilationDriverTestKit.WriteInputFile(
                tempIn, "a.dsl.json", EffectCompilationDriverTestKit.DslJsonOfText(JoinText)); // 与 a.txt 撞 id。
            EffectCompilationDriverTestKit.WriteInputFile(tempIn, "bad.dsl.json", "{ 坏");

            var dry = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut, dryRun: true);
            var disk = EffectCompilationDriverTestKit.CreateDriver().CompileDirectory(tempIn, tempOut);

            Assert.Equal(
                dry.Report.Successes.Select(s => (s.EffectId, s.SourceFileName, s.DeclarationOrdinal, s.SemanticallyComplete)),
                disk.Report.Successes.Select(s => (s.EffectId, s.SourceFileName, s.DeclarationOrdinal, s.SemanticallyComplete)));
            Assert.Equal(
                dry.Report.Failures.Select(f => (f.EffectId, f.SourceFileName, f.Category)),
                disk.Report.Failures.Select(f => (f.EffectId, f.SourceFileName, f.Category)));
            Assert.Equal(
                dry.Report.Inputs.Select(i => (i.SourceFileName, i.FileFailureCategory)),
                disk.Report.Inputs.Select(i => (i.SourceFileName, i.FileFailureCategory)));

            // dry-run：重复条目在报告（不因不落盘丢失）。
            Assert.Contains(
                dry.Report.Failures,
                f => f.Category == EffectCompilationFailureCategory.DuplicateIdentity);
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
}
