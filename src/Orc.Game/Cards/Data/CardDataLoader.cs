using Orc.Cards;

namespace Orc.Game.Cards.Data;

/// <summary>单卡载入失败（隔离留痕）。</summary>
/// <param name="File">文件路径。</param>
/// <param name="Error">失败原因。</param>
public sealed record CardDataLoadFailure(string File, string Error);

/// <summary>
/// 卡牌数据体载入结果（S8；Q14/Q15/Q17）：**纯数据产出**——注册由宿主完成
/// （定义集传给 <c>Match</c>；预制体经 <c>PrefabManager.RegisterPrefab</c>；效果声明经 <c>CardEffectRegistry.DeclarePrefab</c>）。
/// </summary>
/// <param name="Definitions">卡定义集（成功载入的卡）。</param>
/// <param name="Prefabs">内联效果预制体（含 id——载入期注册用；显式 id 缺失＝该条隔离）。</param>
/// <param name="EffectDeclarations">卡 → 效果预制体 id 清单（引用 ＋ 内联并入；注册用）。</param>
/// <param name="Failures">失败/隔离留痕（坏文件、坏单卡）。</param>
/// <param name="Warnings">软隔离留痕（未知组件、组件反序列化失败等）。</param>
public sealed record CardDataLoadResult(
    IReadOnlyList<CardDefinitionEntry> Definitions,
    IReadOnlyList<EffectSnapshot> Prefabs,
    IReadOnlyList<(string CardId, IReadOnlyList<string> PrefabIds)> EffectDeclarations,
    IReadOnlyList<CardDataLoadFailure> Failures,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 卡牌数据体载入器（S8）：扫描目录下的单卡数据体文件（<c>*.card.json</c>，Q14）、逐文件解析
/// （**单文件失败隔离**、不阻断整批——对齐 <c>PrefabManager.LoadDirectory</c> 口径），
/// 产出"卡定义集 ＋ 预制体注册 ＋ 效果声明"三样数据（assembly 不动注册面——由宿主接线）。
/// </summary>
public static class CardDataLoader
{
    /// <summary>数据体文件模式（单卡一文件）。</summary>
    public const string FilePattern = "*.card.json";

    /// <summary>载入目录（不存在＝空结果）。</summary>
    /// <exception cref="ArgumentNullException">directory 为 null。</exception>
    public static CardDataLoadResult LoadDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!Directory.Exists(directory))
        {
            return new CardDataLoadResult(
                Array.Empty<CardDefinitionEntry>(),
                Array.Empty<EffectSnapshot>(),
                Array.Empty<(string, IReadOnlyList<string>)>(),
                Array.Empty<CardDataLoadFailure>(),
                Array.Empty<string>());
        }

        return LoadFiles(Directory.EnumerateFiles(directory, FilePattern, SearchOption.TopDirectoryOnly));
    }

    /// <summary>载入指定文件集（逐文件隔离）。</summary>
    /// <exception cref="ArgumentNullException">files 为 null。</exception>
    public static CardDataLoadResult LoadFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var definitions = new List<CardDefinitionEntry>();
        var prefabs = new List<EffectSnapshot>();
        var declarations = new List<(string CardId, IReadOnlyList<string> PrefabIds)>();
        var failures = new List<CardDataLoadFailure>();
        var warnings = new List<string>();
        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        var prefabIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new CardDataLoadFailure(file, $"读取失败：{ex.Message}"));
                continue;
            }

            if (!CardDataJson.TryDeserialize(json, out var body, out var fileWarnings, out var error))
            {
                failures.Add(new CardDataLoadFailure(file, error ?? "载入失败。"));
                continue;
            }

            foreach (var warning in fileWarnings)
            {
                warnings.Add($"{Path.GetFileName(file)}：{warning}");
            }

            if (!cardIds.Add(body.Id))
            {
                failures.Add(new CardDataLoadFailure(file, $"卡 id '{body.Id}' 重复（跳过——卡库键唯一）。"));
                continue;
            }

            CardDefinition definition;
            try
            {
                definition = new CardDefinition(body.Name, body.Components);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                failures.Add(new CardDataLoadFailure(file, $"卡定义非法：{ex.Message}"));
                continue;
            }

            definitions.Add(new CardDefinitionEntry(body.Id, definition));

            // 效果声明：引用 id ＋ 内联预制体（内联先注册——以 id 并入声明，装载链因此只有"引用"一条）。
            var ids = new List<string>();
            if (body.TryGetComponent<Components.EffectsDefinition>(out var effects))
            {
                foreach (var prefabId in effects.Prefabs)
                {
                    if (!prefabIds.Add(prefabId))
                    {
                        warnings.Add($"{body.Id}：效果预制体 id '{prefabId}' 重复注册（隔离该条）。");
                        continue;
                    }

                    ids.Add(prefabId);
                }

                var index = 0;
                foreach (var snapshot in effects.Inline)
                {
                    index++;
                    var inlineId = snapshot.Root.Id;
                    if (string.IsNullOrWhiteSpace(inlineId))
                    {
                        warnings.Add($"{body.Id}：内联预制体缺少 root.id（隔离该条——请显式给出 id）。");
                        continue;
                    }

                    if (!prefabIds.Add(inlineId))
                    {
                        warnings.Add($"{body.Id}：内联预制体 id '{inlineId}' 与已注册项冲突（隔离该条）。");
                        continue;
                    }

                    prefabs.Add(snapshot);
                    ids.Add(inlineId);
                    _ = index;
                }
            }

            if (ids.Count > 0)
            {
                declarations.Add((body.Id, ids));
            }
        }

        return new CardDataLoadResult(definitions, prefabs, declarations, failures, warnings);
    }
}
