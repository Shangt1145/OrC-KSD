using System.Text;
using Orc.Game.Cards;

namespace Orc.Game.Cards.Data;

/// <summary>单卡写出成功条目（卡 id ＋ 目标文件）。</summary>
/// <param name="CardId">卡 id。</param>
/// <param name="File">目标文件路径（＝<c>&lt;目录&gt;/&lt;id&gt;.card.json</c>）。</param>
public sealed record CardDataSaveSuccess(string CardId, string File);

/// <summary>单卡写出失败（隔离留痕；与警告分列——失败＝文件系统/输入层，警告＝序列化语义面）。</summary>
/// <param name="CardId">卡 id（可定位）。</param>
/// <param name="File">目标文件路径（预期的落点）。</param>
/// <param name="Error">失败原因。</param>
public sealed record CardDataSaveFailure(string CardId, string File, string Error);

/// <summary>
/// 卡牌数据体落盘结果（X1 加性·写方向）：成功／失败／警告**分列**且可定位——
/// 成功判定＝「全部写成功、零失败/零警告」可由结构化清单直接断言（对齐 <c>CardDataLoadResult</c> 的结构风格）。
/// </summary>
/// <param name="Succeeded">成功条目（卡 id＋目标文件）。</param>
/// <param name="Failures">失败条目（卡 id＋目标文件＋原因；单卡级失败＝隔离、其余照常）。</param>
/// <param name="Warnings">警告条目（来自逐卡序列化的语义面警告——已含卡 id，与 <c>CardDataJson.Serialize</c> 同源）。</param>
public sealed record CardDataSaveResult(
    IReadOnlyList<CardDataSaveSuccess> Succeeded,
    IReadOnlyList<CardDataSaveFailure> Failures,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 卡牌数据体落盘器（X1 加性·写方向）：把定义集条目写成单卡一文件（<c>&lt;id&gt;.card.json</c>，顶层）。
/// 命名/结构口径与 <see cref="CardDataLoader.LoadDirectory"/> 成对（<c>*.card.json</c>、单卡一文件、顶层）。
/// 失败契约（与读侧「单文件隔离」对偶）：
/// ①单卡级失败＝隔离（记失败条目、跳过该卡、其余继续）——含非法 id、批内重复、IO 失败；
/// ②目录准备失败（无法创建/准备目标目录）＝整体性失败（抛出，不依赖单卡内容）；
/// ③空输入列表＝空成功（零文件）；④非法 id＝拒绝（不转义、不清理、不照写——含路径穿越防护）；
/// ⑤批内重复 id＝首见写出、重复者进失败集（对称读侧「后者隔离」口径）；跨运行同 id＝覆盖（导出语义）。
/// 文本契约（写文件）：UTF-8 无 BOM；换行/缩进/尾随换行由 <see cref="CardDataJson.Serialize"/> 固定。
/// </summary>
public static class CardDataWriter
{
    /// <summary>单卡文件名后缀（与读侧扫描模式 <c>*.card.json</c> 成对）。</summary>
    public const string FileSuffix = ".card.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 目录级落盘：逐卡归约到单文件写（<see cref="SaveFile"/> 为原语）；目录不存在＝自动创建（含多级）。
    /// </summary>
    /// <param name="directory">目标目录（不存在＝自动创建；无法创建＝抛出）。</param>
    /// <param name="entries">定义集条目（声明序；空＝空成功、零文件、不做目录准备）。</param>
    /// <returns>成功／失败／警告分列的落盘结果。</returns>
    /// <exception cref="ArgumentNullException">directory 或 entries 为 null；entries 含 null 元素。</exception>
    /// <exception cref="ArgumentException">directory 为 null/空白。</exception>
    /// <exception cref="IOException">目录准备失败（整体性——无法创建/准备目标目录）。</exception>
    /// <exception cref="UnauthorizedAccessException">目录准备失败（权限）。</exception>
    public static CardDataSaveResult SaveDirectory(string directory, IEnumerable<CardDefinitionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(entries);

        var entryList = new List<CardDefinitionEntry>();
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entryList.Add(entry);
        }

        var succeeded = new List<CardDataSaveSuccess>();
        var failures = new List<CardDataSaveFailure>();
        var warnings = new List<string>();

        if (entryList.Count == 0)
        {
            // 空输入列表＝空成功（零文件——无单卡内容、不做目录准备：副作用最小）。
            return new CardDataSaveResult(succeeded, failures, warnings);
        }

        Directory.CreateDirectory(directory); // 目录准备失败＝整体性失败（抛出）。

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entryList)
        {
            WriteSingle(directory, entry, seenIds, succeeded, failures, warnings);
        }

        return new CardDataSaveResult(succeeded, failures, warnings);
    }

    /// <summary>
    /// 单文件写（原语：目录＋卡条目——复用命名规则 <c>&lt;id&gt;.card.json</c> 与安全校验；
    /// 不提供任意目标路径形态）。目录不存在＝自动创建。
    /// </summary>
    /// <param name="directory">目标目录（不存在＝自动创建；无法创建＝抛出）。</param>
    /// <param name="entry">卡条目（id 由外部携带）。</param>
    /// <returns>成功／失败／警告分列的落盘结果（单卡时成功/失败条目各 ≤1）。</returns>
    /// <exception cref="ArgumentNullException">directory 或 entry 为 null。</exception>
    /// <exception cref="ArgumentException">directory 为 null/空白。</exception>
    /// <exception cref="IOException">目录准备失败（整体性——无法创建/准备目标目录）。</exception>
    /// <exception cref="UnauthorizedAccessException">目录准备失败（权限）。</exception>
    public static CardDataSaveResult SaveFile(string directory, CardDefinitionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(entry);

        Directory.CreateDirectory(directory); // 目录准备失败＝整体性失败（抛出）。

        var succeeded = new List<CardDataSaveSuccess>();
        var failures = new List<CardDataSaveFailure>();
        var warnings = new List<string>();
        WriteSingle(directory, entry, new HashSet<string>(StringComparer.Ordinal), succeeded, failures, warnings);
        return new CardDataSaveResult(succeeded, failures, warnings);
    }

    private static void WriteSingle(
        string directory,
        CardDefinitionEntry entry,
        HashSet<string> seenIds,
        List<CardDataSaveSuccess> succeeded,
        List<CardDataSaveFailure> failures,
        List<string> warnings)
    {
        var cardId = entry.Id ?? string.Empty;
        var targetFile = Path.Combine(directory, cardId + FileSuffix);

        // 非法 id＝拒绝（不转义、不清理、不照写）：文件系统层不可表达/不安全（含路径穿越防护——如 ..、分隔符）。
        if (IsIllegalCardId(cardId) || !IsWithinDirectory(directory, targetFile))
        {
            failures.Add(new CardDataSaveFailure(
                cardId, targetFile, "卡 id 非法（含文件系统非法字符/路径分隔符/穿越序列——拒绝写出）。"));
            return;
        }

        // 同批重复＝首见写出、重复者失败（对称读侧「后者隔离」口径）。
        if (!seenIds.Add(cardId))
        {
            failures.Add(new CardDataSaveFailure(cardId, targetFile, "卡 id 重复（同批内已写出首见者——跳过）。"));
            return;
        }

        string json;
        try
        {
            json = CardDataJson.Serialize(entry, out var serializeWarnings);
            foreach (var warning in serializeWarnings)
            {
                warnings.Add(warning);
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException
            or ArgumentOutOfRangeException or InvalidOperationException)
        {
            failures.Add(new CardDataSaveFailure(cardId, targetFile, $"序列化失败：{ex.Message}"));
            return;
        }

        try
        {
            File.WriteAllText(targetFile, json, Utf8NoBom); // UTF-8 无 BOM（文本契约）。
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new CardDataSaveFailure(cardId, targetFile, $"写入失败：{ex.Message}"));
            return;
        }

        succeeded.Add(new CardDataSaveSuccess(cardId, targetFile));
    }

    private static bool IsIllegalCardId(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return true;
        }

        if (cardId.Contains('/') || cardId.Contains('\\'))
        {
            return true;
        }

        if (cardId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return true;
        }

        // 防御性："." / ".." 相对路径段（前缀拼接后仍属穿越风险面——本批以「拒绝＋报告」为最稳口径）。
        return cardId is "." or "..";
    }

    private static bool IsWithinDirectory(string directory, string file)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var prefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;

        return Path.GetFullPath(file).StartsWith(prefix, StringComparison.Ordinal);
    }
}
