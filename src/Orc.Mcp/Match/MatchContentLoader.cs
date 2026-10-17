using Orc.Cards;
using Orc.Game.Cards;
using Orc.Game.Cards.Data;
using Orc.Packages;

namespace Orc.Mcp.Match;

/// <summary>
/// 真实对局的卡内容装载结果（纯数据：定义集 ＋ 内联预制体 ＋ 效果声明 ＋ 效果库目录）。
/// </summary>
public sealed class MatchContentLoadResult
{
    private readonly HashSet<string> _definitionIds;

    internal MatchContentLoadResult(
        IReadOnlyList<CardDefinitionEntry> definitions,
        IReadOnlyList<EffectSnapshot> inlinePrefabs,
        IReadOnlyList<(string CardId, IReadOnlyList<string> PrefabIds)> declarations,
        IReadOnlyList<string> effectDirectories,
        IReadOnlyList<string> packPrefixes)
    {
        Definitions = definitions;
        InlinePrefabs = inlinePrefabs;
        Declarations = declarations;
        EffectDirectories = effectDirectories;
        PackPrefixes = packPrefixes;
        _definitionIds = new HashSet<string>(definitions.Select(d => d.Id), StringComparer.Ordinal);
    }

    /// <summary>卡定义集（包链的 id 已前缀化）。</summary>
    public IReadOnlyList<CardDefinitionEntry> Definitions { get; }

    /// <summary>内联效果预制体。</summary>
    public IReadOnlyList<EffectSnapshot> InlinePrefabs { get; }

    /// <summary>卡 → 效果预制体声明。</summary>
    public IReadOnlyList<(string CardId, IReadOnlyList<string> PrefabIds)> Declarations { get; }

    /// <summary>效果库目录（按装载序）。</summary>
    public IReadOnlyList<string> EffectDirectories { get; }

    /// <summary>包链的卡 id 前缀（目录链不贡献前缀）。</summary>
    public IReadOnlyList<string> PackPrefixes { get; }

    /// <summary>
    /// 把调用方给的卡 id 解析为装载后的全局 id：先按全名直配；否则按包前缀唯一匹配。
    /// </summary>
    /// <exception cref="InvalidOperationException">未找到或匹配歧义。</exception>
    public string ResolveCardId(string cardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);

        if (_definitionIds.Contains(cardId))
        {
            return cardId;
        }

        var matches = PackPrefixes
            .Select(prefix => prefix + cardId)
            .Where(_definitionIds.Contains)
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"卡 id '{cardId}' 不在已装载的卡定义中。"),
            _ => throw new InvalidOperationException(
                $"卡 id '{cardId}' 在多个卡包中重名（{string.Join("、", matches)}），请给出全名。"),
        };
    }
}

/// <summary>
/// 真实对局的卡内容装载器：目录列表 → 卡定义/效果（两条链）。
/// <para>
/// 有 <c>manifest.json</c> 的目录走**包链**（<see cref="CardPackLoader.Load"/>，含分发预检双零门）；
/// 其余走**目录链**（按 <c>cards</c>/<c>cards-*</c> 发现，或目录自身即卡目录）。
/// </para>
/// </summary>
internal static class MatchContentLoader
{
    private const string ManifestFileName = "manifest.json";
    private const string CardsDirectoryPrefix = "cards";
    private const string EffectsDirectoryName = "effects";

    public static MatchContentLoadResult Load(IReadOnlyList<string> packDirectories)
    {
        ArgumentNullException.ThrowIfNull(packDirectories);

        if (packDirectories.Count == 0)
        {
            throw new ArgumentException("至少需要一个卡包目录。", nameof(packDirectories));
        }

        var definitions = new List<CardDefinitionEntry>();
        var inlinePrefabs = new List<EffectSnapshot>();
        var declarations = new List<(string CardId, IReadOnlyList<string> PrefabIds)>();
        var effectDirectories = new List<string>();
        var packPrefixes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawDirectory in packDirectories)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rawDirectory);
            var directory = Path.GetFullPath(rawDirectory);
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"卡包目录不存在：{directory}");
            }

            if (File.Exists(Path.Combine(directory, ManifestFileName)))
            {
                AppendPack(directory, definitions, inlinePrefabs, declarations, effectDirectories, packPrefixes, seen);
            }
            else
            {
                AppendDirectoryChain(directory, definitions, inlinePrefabs, declarations, effectDirectories, seen);
            }
        }

        if (definitions.Count == 0)
        {
            throw new InvalidDataException("卡包目录未装载到任何卡定义。");
        }

        return new MatchContentLoadResult(
            definitions, inlinePrefabs, declarations, effectDirectories, packPrefixes);
    }

    private static void AppendPack(
        string directory,
        List<CardDefinitionEntry> definitions,
        List<EffectSnapshot> inlinePrefabs,
        List<(string CardId, IReadOnlyList<string> PrefabIds)> declarations,
        List<string> effectDirectories,
        List<string> packPrefixes,
        HashSet<string> seen)
    {
        var manifest = PackageManifest.Parse(File.ReadAllText(Path.Combine(directory, ManifestFileName)));
        var content = CardPackLoader.Load(directory, manifest);

        foreach (var entry in content.Definitions)
        {
            if (!seen.Add(entry.Id))
            {
                throw new InvalidDataException($"卡 id '{entry.Id}' 在多个装载源中重名（拒绝半装载）。");
            }

            definitions.Add(entry);
        }

        inlinePrefabs.AddRange(content.InlinePrefabs);
        declarations.AddRange(content.Declarations);
        if (content.EffectsDirectory is { } effects)
        {
            effectDirectories.Add(effects);
        }

        packPrefixes.Add(content.Prefix);
    }

    private static void AppendDirectoryChain(
        string directory,
        List<CardDefinitionEntry> definitions,
        List<EffectSnapshot> inlinePrefabs,
        List<(string CardId, IReadOnlyList<string> PrefabIds)> declarations,
        List<string> effectDirectories,
        HashSet<string> seen)
    {
        var cardDirectories = DiscoverCardDirectories(directory);
        if (cardDirectories.Count == 0)
        {
            throw new InvalidDataException(
                $"目录 '{directory}' 既无 manifest.json，也不是卡目录（未发现 cards/ 或 *.card.json）。");
        }

        foreach (var cardDirectory in cardDirectories)
        {
            var load = CardDataLoader.LoadDirectory(cardDirectory);
            if (load.Failures.Count > 0)
            {
                throw new InvalidDataException("卡数据装载失败："
                    + string.Join("；", load.Failures.Select(f => $"{Path.GetFileName(f.File)}：{f.Error}").Take(6)));
            }

            foreach (var entry in load.Definitions)
            {
                if (!seen.Add(entry.Id))
                {
                    throw new InvalidDataException($"卡 id '{entry.Id}' 在多个装载源中重名（拒绝半装载）。");
                }

                definitions.Add(entry);
            }

            inlinePrefabs.AddRange(load.Prefabs);
            declarations.AddRange(load.EffectDeclarations);
        }

        var effects = Path.Combine(directory, EffectsDirectoryName);
        if (Directory.Exists(effects))
        {
            effectDirectories.Add(effects);
        }
    }

    private static IReadOnlyList<string> DiscoverCardDirectories(string directory)
    {
        var children = Directory.EnumerateDirectories(directory)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return string.Equals(name, CardsDirectoryPrefix, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(CardsDirectoryPrefix + "-", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();

        if (children.Length > 0)
        {
            return children;
        }

        // 便利形态：直接给卡目录本身（目录下就是 *.card.json）。
        return Directory.EnumerateFiles(directory, "*.card.json", SearchOption.TopDirectoryOnly).Any()
            ? new[] { directory }
            : Array.Empty<string>();
    }
}
