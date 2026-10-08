using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Orc.Cards;
using Orc.Core;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Templates;

namespace Orc.Game.Effects;

/// <summary>
/// 动态效果「编译挂载」的**资产源**（批 4）：模板效果（<c>*.tpl.json</c>）＋ op 语句模板（<c>ops/*.csx.tpl</c>）。
/// 形态两种——①目录（默认：程序集输出目录 <c>EffectParsing/Templates</c> 约定；或显式指定）；
/// ②内存注入（已加载的模板集合与 op 目录对象——测试/工具链自含用）。
/// </summary>
internal sealed class EffectAssetSource
{
    private EffectAssetSource(string? directory, IReadOnlyList<EffectTemplate>? templates, OpTemplateCatalog? ops)
    {
        Directory = directory;
        MemoryTemplates = templates;
        MemoryOps = ops;
    }

    /// <summary>目录形态的资产根（约定：根含 <c>*.tpl.json</c>、<c>ops</c> 子目录含 <c>*.csx.tpl</c>）。</summary>
    internal string? Directory { get; }

    /// <summary>内存形态的模板集合。</summary>
    internal IReadOnlyList<EffectTemplate>? MemoryTemplates { get; }

    /// <summary>内存形态的 op 目录。</summary>
    internal OpTemplateCatalog? MemoryOps { get; }

    internal static EffectAssetSource ForDirectory(string directory) => new(directory, null, null);

    internal static EffectAssetSource ForMemory(IReadOnlyList<EffectTemplate> templates, OpTemplateCatalog ops)
        => new(null, templates, ops);
}

/// <summary>解析后的资产包（模板集合＋op 目录＋内容指纹——指纹参与编译缓存分键）。</summary>
internal sealed record EffectAssetBundle(
    IReadOnlyList<EffectTemplate> Templates,
    OpTemplateCatalog Ops,
    string Fingerprint);

/// <summary>
/// 资产解析器（批 4；进程级缓存、惰性加载、单文件失败隔离）：
/// ①目录形态——**每次请求重算内容指纹**（文件路径＋内容哈希）：指纹一致＝复用已解析资产（不重复解析）；
///   指纹变化＝重新加载（「资产变更必反映」——指纹使缓存分键/失效）；
/// ②内存形态——模板以序列化内容参与指纹、op 目录以实例身份标记参与指纹（op 目录无内容读面）。
/// </summary>
internal static class EffectAssetResolver
{
    private static readonly ConcurrentDictionary<string, (string Fingerprint, EffectAssetBundle Bundle)> DirectoryCache =
        new(StringComparer.Ordinal);

    // 内存源 op 目录实例身份标记（首次访问生成唯一值；同一对象多次＝同标记——缓存命中，异对象＝异标记——分键）。
    private static readonly ConditionalWeakTable<OpTemplateCatalog, string> MemoryOpTags = new();

    /// <summary>解析资产源（null＝默认目录约定）。</summary>
    internal static EffectAssetBundle Resolve(EffectAssetSource? source)
    {
        if (source is null || source.Directory is not null)
        {
            return ResolveDirectory(source?.Directory ?? EffectTemplateLoader.DefaultDirectory);
        }

        return ResolveMemory(source.MemoryTemplates!, source.MemoryOps!);
    }

    private static EffectAssetBundle ResolveDirectory(string directory)
    {
        var fingerprint = ComputeDirectoryFingerprint(directory);
        if (DirectoryCache.TryGetValue(directory, out var cached)
            && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return cached.Bundle;
        }

        // 单文件失败隔离（可用子集继续——加载失败/坏文件不阻断整批）；目录级读取失败（IO/权限类）
        // ＝隔离为空资产（相关请求按「来源未命中」失败——不外抛）。
        EffectTemplateLoadResult templates;
        OpTemplateCatalog ops;
        try
        {
            templates = EffectTemplateLoader.LoadDirectory(directory);
            ops = OpTemplateCatalog.LoadDirectory(Path.Combine(directory, "ops"), out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            templates = new EffectTemplateLoadResult(
                Array.Empty<EffectTemplate>(), Array.Empty<EffectTemplateLoadFailure>());
            ops = OpTemplateCatalog.LoadFiles(Array.Empty<string>(), out _);
        }

        var bundle = new EffectAssetBundle(templates.Templates, ops, fingerprint);
        DirectoryCache[directory] = (fingerprint, bundle);
        return bundle;
    }

    private static EffectAssetBundle ResolveMemory(IReadOnlyList<EffectTemplate> templates, OpTemplateCatalog ops)
        => new(templates, ops, ComputeMemoryFingerprint(templates, ops));

    /// <summary>
    /// 目录内容指纹：逐文件「路径＋内容」哈希（含根 <c>*.tpl.json</c> 与 <c>ops/*.csx.tpl</c>；内容变更即分键）。
    /// 读取失败（IO/权限类）＝哨兵参与指纹（不外抛——相关请求按「来源未命中」分类；安全方向：宁多编译不错误命中）。
    /// </summary>
    private static string ComputeDirectoryFingerprint(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return "dir-missing:" + StableHash.Fnv1a64(directory).ToString("x16");
        }

        var builder = new StringBuilder();
        try
        {
            foreach (var file in EnumerateAssetFiles(directory))
            {
                builder.Append(file).Append('\u0001');
                try
                {
                    builder.Append(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    builder.Append("<read-failed:").Append(ex.GetType().Name).Append('>');
                }

                builder.Append('\u0002');
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            builder.Append("<enumerate-failed:").Append(ex.GetType().Name).Append('>');
        }

        return "dir:" + StableHash.Fnv1a64(builder.ToString()).ToString("x16");
    }

    private static IEnumerable<string> EnumerateAssetFiles(string directory)
    {
        foreach (var file in Directory
            .EnumerateFiles(directory, EffectTemplateLoader.FilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return file;
        }

        var opsDirectory = Path.Combine(directory, "ops");
        if (!Directory.Exists(opsDirectory))
        {
            yield break;
        }

        foreach (var file in Directory
            .EnumerateFiles(opsDirectory, OpTemplateCatalog.FilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return file;
        }
    }

    /// <summary>内存源指纹：模板（id＋序列化内容）＋ op 目录实例身份。</summary>
    private static string ComputeMemoryFingerprint(IReadOnlyList<EffectTemplate> templates, OpTemplateCatalog ops)
    {
        var builder = new StringBuilder();
        foreach (var template in templates.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            builder.Append(template.Id).Append('\u0001').Append(EffectTemplateJson.Serialize(template)).Append('\u0002');
        }

        builder.Append("ops:").Append(MemoryOpTags.GetValue(ops, _ => Guid.NewGuid().ToString("N")));
        return "mem:" + StableHash.Fnv1a64(builder.ToString()).ToString("x16");
    }
}

/// <summary>
/// 动态效果**编译缓存**（批 4；进程级共享——跨对局复用、成本摊薄）：
/// 键＝资产指纹＋effectId＋DSL 文本哈希（同输入产出等值快照——缓存命中与否可观察行为无差别）；
/// 编译产物为纯数据（无对局态——不同资产集/不同 DSL＝不同键，天然隔离）。
/// 容量/失效＝最小（进程内、不淘汰；不做跨进程持久化、不暴露统计面）。
/// </summary>
internal static class EffectCompilationCache
{
    private static readonly ConcurrentDictionary<string, EffectSnapshot> Cache = new(StringComparer.Ordinal);

    /// <summary>取缓存或执行编译（缓存命中＝不重复全量编译）。</summary>
    internal static EffectSnapshot GetOrCompile(string key, Func<EffectSnapshot> compile) => Cache.GetOrAdd(key, _ => compile());
}
