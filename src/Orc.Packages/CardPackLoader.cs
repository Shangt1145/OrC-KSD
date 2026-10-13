using System.Text.Json;
using Orc.Cards;
using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Cards.Data;
using Orc.Game.EffectParsing;

namespace Orc.Packages;

/// <summary>
/// 卡包目录装载管道（前端资产迁移 S2）：按**目录约定**读包内 <c>cards/</c>（可多目录 <c>cards-*</c>）、
/// <c>effects/</c>、<c>texts.json</c>、<c>keywords.json</c>，产出纯数据内容（定义集 ＋ 内联预制体 ＋ 效果声明 ＋ 效果库目录 ＋ 词条）。
/// 装载前跑分发预检**双零门**（Errors＝0 且 Warnings＝0），不达＝明确失败（调用方按整包隔离处理）。
/// 说明：卡面文本 → 效果的转换经 <see cref="CardTextEffectCompiler"/>（引擎侧能力，复用既有编译驱动）。
/// </summary>
public static class CardPackLoader
{
    /// <summary>卡目录名前缀（<c>cards</c> / <c>cards-support</c> …）。</summary>
    public const string CardsDirectoryPrefix = "cards";

    /// <summary>效果库目录名。</summary>
    public const string EffectsDirectoryName = "effects";

    /// <summary>卡面文本 sidecar 文件名。</summary>
    public const string TextsFileName = "texts.json";

    /// <summary>词条声明文件名。</summary>
    public const string KeywordsFileName = "keywords.json";

    /// <summary>
    /// 装载一个卡包目录。失败＝抛（调用方按**整包隔离**处理，不留半装载态）。
    /// </summary>
    /// <param name="packageRoot">包根目录。</param>
    /// <param name="manifest">已解析清单（须为 <see cref="PackageKind.CardPack"/>）。</param>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    /// <exception cref="InvalidOperationException">清单类型不符。</exception>
    /// <exception cref="InvalidDataException">目录约定/预检/文本转换失败。</exception>
    public static CardPackContent Load(string packageRoot, PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(packageRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Kind != PackageKind.CardPack)
        {
            throw new InvalidOperationException(
                $"卡包管道只接受 kind=cardpack 的清单（实际 {manifest.Kind}）。");
        }

        var cardDirectories = DiscoverCardDirectories(packageRoot);
        if (cardDirectories.Count == 0)
        {
            throw new InvalidDataException("缺少 cards/ 目录（卡包须以目录约定提供卡数据）。");
        }

        var effectsDirectory = Path.Combine(packageRoot, EffectsDirectoryName);
        var effects = Directory.Exists(effectsDirectory) ? effectsDirectory : null;

        var report = CardDistributionValidator.Validate(
            cardDirectories, effects is null ? null : new[] { effects });
        if (report.ErrorCount > 0 || report.WarningCount > 0)
        {
            throw new InvalidDataException("分发预检未达双零："
                + string.Join("；", report.Errors.Select(e => e.Message)
                    .Concat(report.Warnings.Select(w => w.Message)).Take(6)));
        }

        var prefix = $"pack/{manifest.Id}/";
        var definitions = new List<CardDefinitionEntry>();
        var inlinePrefabs = new List<EffectSnapshot>();
        var declarations = new List<(string CardId, IReadOnlyList<string> PrefabIds)>();
        foreach (var directory in cardDirectories)
        {
            var load = CardDataLoader.LoadDirectory(directory);
            if (load.Failures.Count > 0)
            {
                throw new InvalidDataException("卡数据装载失败："
                    + string.Join("；", load.Failures.Select(f => $"{Path.GetFileName(f.File)}：{f.Error}").Take(6)));
            }

            foreach (var entry in load.Definitions)
            {
                definitions.Add(new CardDefinitionEntry(prefix + entry.Id, entry.Definition));
            }

            inlinePrefabs.AddRange(load.Prefabs);
            foreach (var (cardId, prefabIds) in load.EffectDeclarations)
            {
                declarations.Add((prefix + cardId, prefabIds));
            }
        }

        var texts = ReadTexts(packageRoot);
        AppendTextEffects(manifest, definitions, prefix, texts, inlinePrefabs, declarations);

        var keywords = ReadKeywords(packageRoot);
        return new CardPackContent(
            manifest, packageRoot, definitions, inlinePrefabs, declarations, effects, keywords, texts);
    }

    private static IReadOnlyList<string> DiscoverCardDirectories(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(packageRoot)
            .Where(d => IsCardDirectory(Path.GetFileName(d)))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCardDirectory(string name) =>
        string.Equals(name, CardsDirectoryPrefix, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(CardsDirectoryPrefix + "-", StringComparison.OrdinalIgnoreCase);

    private static void AppendTextEffects(
        PackageManifest manifest,
        IReadOnlyList<CardDefinitionEntry> definitions,
        string prefix,
        IReadOnlyDictionary<string, string> texts,
        List<EffectSnapshot> inlinePrefabs,
        List<(string CardId, IReadOnlyList<string> PrefabIds)> declarations)
    {
        foreach (var entry in definitions)
        {
            // entry.Id 已前缀化；文本键为包内原卡 id。
            var rawId = entry.Id.StartsWith(prefix, StringComparison.Ordinal) ? entry.Id[prefix.Length..] : entry.Id;
            if (!texts.TryGetValue(rawId, out var text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var cardType = entry.Definition.Category == CardCategory.Unit ? "unit" : "order";
            var drafts = CardTextEffectCompiler.Parse(text, cardType)
                ?? throw new InvalidDataException(
                    $"卡包 '{manifest.Id}' 卡牌 '{rawId}' 的卡面文本无法解析（生成器与解析器已漂移）：{text}");
            if (drafts.Count == 0)
            {
                continue;
            }

            var snapshot = CardTextEffectCompiler.Compile(cardType, drafts, "pack." + manifest.Id + "." + rawId);
            inlinePrefabs.Add(snapshot);
            var existing = declarations.FindIndex(d => string.Equals(d.CardId, entry.Id, StringComparison.Ordinal));
            if (existing >= 0)
            {
                declarations[existing] = (entry.Id,
                    [.. declarations[existing].PrefabIds, snapshot.Root.Id]);
            }
            else
            {
                declarations.Add((entry.Id, new[] { snapshot.Root.Id }));
            }
        }
    }

    private static IReadOnlyDictionary<string, string> ReadTexts(string packageRoot)
    {
        var path = Path.Combine(packageRoot, TextsFileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in document.RootElement.EnumerateObject())
        {
            texts[pair.Name] = pair.Value.GetString() ?? "";
        }

        return texts;
    }

    /// <summary>读取包内 <c>keywords.json</c>（不存在＝空；非法＝明确失败）。供词条加载插件使用。</summary>
    /// <exception cref="ArgumentNullException">packageRoot 为 null。</exception>
    /// <exception cref="InvalidDataException">结构非法。</exception>
    public static IReadOnlyList<CardPackKeyword> ReadKeywords(string packageRoot)
    {
        var path = Path.Combine(packageRoot, KeywordsFileName);
        if (!File.Exists(path))
        {
            return Array.Empty<CardPackKeyword>();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("keywords.json 须为数组。");
            }

            var result = new List<CardPackKeyword>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = Text(item, "id") ?? throw new InvalidDataException("词条缺少 id。");
                if (!seen.Add(id))
                {
                    throw new InvalidDataException($"词条 id 重复：{id}");
                }

                var label = Text(item, "label") ?? id;
                var battle = item.TryGetProperty("battle", out var b) && b.ValueKind == JsonValueKind.True;
                var bindings = new List<KeywordEffectBinding>();
                if (item.TryGetProperty("bindings", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var binding in list.EnumerateArray())
                    {
                        var prefabId = Text(binding, "prefabId")
                            ?? throw new InvalidDataException($"词条 '{id}' 的绑定缺少 prefabId。");
                        var effectName = Text(binding, "effectName")
                            ?? throw new InvalidDataException($"词条 '{id}' 的绑定缺少 effectName。");
                        bindings.Add(new KeywordEffectBinding(prefabId, effectName));
                    }
                }

                if (bindings.Count == 0)
                {
                    throw new InvalidDataException($"词条 '{id}' 没有任何效果绑定（无行为词条不允许发布）。");
                }

                result.Add(new CardPackKeyword(id, label, battle, bindings));
            }

            return result;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"keywords.json 无法解析：{e.Message}");
        }
    }

    private static string? Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// 卡包内容 → 对局注册面（装配序：先把效果声明/预制体/效果库装进对局注册面，再由 <c>Match.Initialize</c> 装载卡）。
/// **须在 <c>Match.Initialize</c> 之前调用**（注册先于卡加载）。
/// </summary>
public static class CardPackAssembler
{
    /// <summary>把若干卡包内容按给定序装配进对局的注册面。</summary>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    /// <exception cref="InvalidDataException">效果库装载失败。</exception>
    public static void Attach(
        CardEffectRegistry registry, LogicEngine engine, IReadOnlyList<CardPackContent> packs)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(packs);

        foreach (var pack in packs)
        {
            foreach (var (cardId, prefabIds) in pack.Declarations)
            {
                registry.DeclarePrefab(cardId, prefabIds);
            }

            foreach (var snapshot in pack.InlinePrefabs)
            {
                engine.Prefabs.RegisterPrefab(snapshot);
            }

            if (pack.EffectsDirectory is { } directory)
            {
                var report = engine.Prefabs.LoadDirectory(directory);
                if (report.Failures.Count > 0)
                {
                    throw new InvalidDataException($"卡包 '{pack.Manifest.Id}' 效果库装载失败："
                        + string.Join("；", report.Failures));
                }
            }
        }
    }
}
