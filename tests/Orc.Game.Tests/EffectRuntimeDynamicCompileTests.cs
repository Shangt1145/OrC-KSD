using Orc.Cards;
using Orc.Game;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Parsing;
using Orc.Game.EffectParsing.Templates;
using Orc.Game.Effects;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// csx 动态效果能力（批 4）——运行时编译挂载（N2b）验收：
/// ①「DSL → 编译 → 快照 → 挂载 → 行为」全链（含指定目录资产注入与内存资产注入）；
/// ②资产变更必反映（内容指纹使缓存分键/失效——含目录内容实时变化）；
/// ③同 DSL 二次编译行为等价；④失败分类（数据无效/来源未命中/编译失败）。
/// 测试自含：临时目录/内嵌数据、不依赖 outputs、测后清理。
/// </summary>
public class EffectRuntimeDynamicCompileTests
{
    /// <summary>编译挂载全链（指定目录资产注入路径）：自含资产目录 → UseEffectAssets → 编译 → 挂载 → 行为。</summary>
    [Fact]
    public async Task 编译挂载_指定目录资产_全链行为生效()
    {
        var tempDir = CreateTempAssetDirectory();
        try
        {
            var match = await CreateMatchAsync();
            var playerA = match.Players[0];
            var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
            var runtime = EffectRuntime.ResolveFor(host)!;
            runtime.UseEffectAssets(tempDir); // 显式覆盖：指定目录（测试自含，不依赖程序集目录）

            var attach = await runtime.CompileAttachAsync(host, DslJsonOfText("友方单位加入时，抽一张牌。"), "effect.compiled.full");
            Assert.True(attach.Success, attach.FailureReason);
            Assert.Equal("effect.compiled.full", attach.EffectName); // 指定身份＝效果名/快照 root.id
            Assert.NotNull(attach.Credential);
            Assert.Single(host.Effects);

            var before = playerA.Hand.Count;
            await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
            Assert.Equal(before + 1, playerA.Hand.Count); // 全链通：csx 真执行

            var detach = await runtime.DetachEffectAsync(host, attach.Credential!);
            Assert.True(detach.Success, detach.FailureReason);
            Assert.Empty(host.Effects);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 资产变更必反映（指纹分键/失效）：目录内容变化后，**同 DSL、同身份**再编译必须反映新资产
    /// （不重设资产源——内容指纹实时重算）。
    /// </summary>
    [Fact]
    public async Task 编译挂载_资产变更必反映_指纹分键()
    {
        var tempDir = CreateTempAssetDirectory();
        try
        {
            var match = await CreateMatchAsync();
            var playerA = match.Players[0];
            var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
            var runtime = EffectRuntime.ResolveFor(host)!;
            runtime.UseEffectAssets(tempDir);

            var dslJson = DslJsonOfText("友方单位加入时，抽一张牌。");

            // v1：draw 抽 1 张。
            var first = await runtime.CompileAttachAsync(host, dslJson, "effect.fingerprint.probe");
            Assert.True(first.Success, first.FailureReason);
            var before1 = playerA.Hand.Count;
            await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
            Assert.Equal(before1 + 1, playerA.Hand.Count);
            Assert.True((await runtime.DetachEffectAsync(host, first.Credential!)).Success);

            // 变更资产内容：draw op 模板抽 count + 1 张。
            var drawFile = Path.Combine(tempDir, "ops", "draw.csx.tpl");
            var original = File.ReadAllText(drawFile);
            File.WriteAllText(drawFile, original.Replace("{{count}}", "{{count}} + 1", StringComparison.Ordinal));

            // 同 DSL、同身份再编译 → 必须分键并反映新资产（抽 2 张）。
            var second = await runtime.CompileAttachAsync(host, dslJson, "effect.fingerprint.probe");
            Assert.True(second.Success, second.FailureReason);
            var before2 = playerA.Hand.Count;
            await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
            Assert.Equal(before2 + 2, playerA.Hand.Count);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>同 DSL 二次编译行为等价：两次编译均成功、各自产出可挂载且行为一致的效果；缺省身份＝自动生成。</summary>
    [Fact]
    public async Task 编译挂载_同DSL二次编译_行为等价()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        var dslJson = DslJsonOfText("友方单位加入时，抽一张牌。");
        var first = await runtime.CompileAttachAsync(host, dslJson, "effect.twice.probe");
        var second = await runtime.CompileAttachAsync(host, dslJson, "effect.twice.probe");
        Assert.True(first.Success, first.FailureReason);
        Assert.True(second.Success, second.FailureReason);
        Assert.NotSame(first.Credential, second.Credential);
        Assert.Equal(2, host.Effects.Count);

        var before = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(before + 2, playerA.Hand.Count); // 两次编译产出等值（行为一致、各自独立实例）

        Assert.True((await runtime.DetachEffectAsync(host, first.Credential!)).Success);
        Assert.True((await runtime.DetachEffectAsync(host, second.Credential!)).Success);
        Assert.Empty(host.Effects);
    }

    /// <summary>内存资产注入路径：直接注入模板集合与 op 目录对象；缺省身份＝自动生成（确定性派生）。</summary>
    [Fact]
    public async Task 编译挂载_内存资产注入_行为生效()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        Assert.Empty(templates.Failures);
        var ops = OpTemplateCatalog.LoadDirectory(OpTemplateCatalog.DefaultDirectory, out var opFailures);
        Assert.Empty(opFailures);
        runtime.UseEffectAssets(templates.Templates, ops);

        var attach = await runtime.CompileAttachAsync(host, DslJsonOfText("友方单位加入时，抽一张牌。"));
        Assert.True(attach.Success, attach.FailureReason);
        Assert.StartsWith("effect.runtime.", attach.EffectName, StringComparison.Ordinal); // 缺省身份＝自动生成

        var before = playerA.Hand.Count;
        await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        Assert.Equal(before + 1, playerA.Hand.Count);

        Assert.True((await runtime.DetachEffectAsync(host, attach.Credential!)).Success);
    }

    /// <summary>失败分类：DSL 解析失败＝数据无效；未知模板＝来源未命中；槽位覆盖不全＝编译失败；零残留。</summary>
    [Fact]
    public async Task 编译挂载_失败分类_无效数据_未知模板_编译层失败()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;

        var invalid = await runtime.CompileAttachAsync(host, "{ this is not a dsl");
        Assert.Equal(EffectAttachStatus.InvalidData, invalid.Status);

        var unknownTemplate = DslJson.Serialize(new DslEffectInstance(
            "no_such_template",
            new Dictionary<string, DslSlotFill>
            {
                ["on_event"] = new DslSlotFill(new[] { new DslOp("draw", count: 1) }),
            }));
        var missing = await runtime.CompileAttachAsync(host, unknownTemplate);
        Assert.Equal(EffectAttachStatus.SourceMissing, missing.Status);

        var incomplete = DslJson.Serialize(new DslEffectInstance("join_basic", new Dictionary<string, DslSlotFill>()));
        var unbuildable = await runtime.CompileAttachAsync(host, incomplete);
        Assert.Equal(EffectAttachStatus.CompileFailed, unbuildable.Status);
        Assert.False(string.IsNullOrWhiteSpace(unbuildable.FailureReason));

        Assert.Empty(host.Effects); // 三类失败均零残留
    }

    /// <summary>失败分类：op 资产缺失（DSL 引用的 op 无语句模板）＝来源未命中。</summary>
    [Fact]
    public async Task 编译挂载_失败分类_op资产缺失()
    {
        var tempDir = CreateTempAssetDirectory();
        try
        {
            File.Delete(Path.Combine(tempDir, "ops", "draw.csx.tpl")); // 单文件缺失（可用子集继续）

            var match = await CreateMatchAsync();
            var playerA = match.Players[0];
            var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
            var runtime = EffectRuntime.ResolveFor(host)!;
            runtime.UseEffectAssets(tempDir);

            var result = await runtime.CompileAttachAsync(host, DslJsonOfText("友方单位加入时，抽一张牌。"));
            Assert.Equal(EffectAttachStatus.SourceMissing, result.Status);
            Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
            Assert.Empty(host.Effects);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>边界：资产目录不可用（不存在/路径指向文件）＝相关请求按来源未命中失败（structural、不外抛）。</summary>
    [Fact]
    public async Task 编译挂载_资产目录不可用_来源未命中()
    {
        var match = await CreateMatchAsync();
        var playerA = match.Players[0];
        var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
        var runtime = EffectRuntime.ResolveFor(host)!;
        var dslJson = DslJsonOfText("友方单位加入时，抽一张牌。");

        // ① 目录不存在。
        var missingDirectory = Path.Combine(Path.GetTempPath(), "orc-missing-assets-" + Guid.NewGuid().ToString("N"));
        runtime.UseEffectAssets(missingDirectory);
        var missing = await runtime.CompileAttachAsync(host, dslJson);
        Assert.Equal(EffectAttachStatus.SourceMissing, missing.Status);

        // ② 路径指向文件（非目录）＝目录不可用。
        var filePath = Path.GetTempFileName();
        try
        {
            runtime.UseEffectAssets(filePath);
            var asFile = await runtime.CompileAttachAsync(host, dslJson);
            Assert.Equal(EffectAttachStatus.SourceMissing, asFile.Status);
        }
        finally
        {
            File.Delete(filePath);
        }

        Assert.Empty(host.Effects);
    }

    /// <summary>
    /// 边界：**资产文件不可读**（读取失败隔离）——单文件被独占锁定 ⇒ 指纹哨兵＋加载留痕隔离，
    /// 相关请求按来源未命中失败（不外抛）；可用子集继续（模板照常加载）。
    /// </summary>
    [Fact]
    public async Task 编译挂载_资产文件不可读_隔离_相关请求来源未命中()
    {
        var tempDir = CreateTempAssetDirectory();
        try
        {
            var match = await CreateMatchAsync();
            var playerA = match.Players[0];
            var host = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 0);
            var runtime = EffectRuntime.ResolveFor(host)!;

            var drawFile = Path.Combine(tempDir, "ops", "draw.csx.tpl");
            using (new FileStream(drawFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                runtime.UseEffectAssets(tempDir);
                var result = await runtime.CompileAttachAsync(host, DslJsonOfText("友方单位加入时，抽一张牌。"));
                Assert.Equal(EffectAttachStatus.SourceMissing, result.Status); // 不可读 op 资产＝来源未命中（不抛）
                Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
                Assert.Empty(host.Effects);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------- 辅助 ----------

    private static async Task<Match> CreateMatchAsync()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();
        return match;
    }

    private static string DslJsonOfText(string text)
    {
        var parser = EffectParser.CreateDefault(out var lexFailures);
        Assert.Empty(lexFailures);
        var parse = parser.Parse(text);
        Assert.Empty(parse.Unresolved);
        return DslJson.Serialize(Assert.Single(parse.Effects));
    }

    /// <summary>创建自含资产目录（复制默认资产：模板 + ops；测试自含、测后清理）。</summary>
    private static string CreateTempAssetDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orc-dynamic-assets-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(EffectTemplateLoader.DefaultDirectory, directory);
        return directory;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
