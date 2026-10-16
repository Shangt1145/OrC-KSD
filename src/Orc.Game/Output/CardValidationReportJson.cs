using System.Text;
using System.Text.Json;
using Orc.Game.Cards.Data;

namespace Orc.Game.Output;

/// <summary>
/// 分发预检报告 JSON 导出器（补齐"校验报告可 JSON 化"缺口；报告本体是结构化内存记录，
/// 序列化形态由此处固定——与 <see cref="GameHooksJson"/> 同风格：手写 <see cref="Utf8JsonWriter"/>、不引反射序列化）。
/// <para>
/// 输出形态：<c>{ cardDirectories, prefabLibraryDirectories, counts{...}, hasNoErrors, errors[], warnings[] }</c>；
/// 条目＝<c>{ category, message, file, cardId, prefabId }</c>（不可得字段输出 null，不省略）。
/// </para>
/// </summary>
public static class CardValidationReportJson
{
    /// <summary>导出报告为 JSON 字符串。</summary>
    /// <exception cref="ArgumentNullException">report 为 null。</exception>
    public static string Serialize(CardDistributionValidationReport report, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("cardDirectories");
            foreach (var directory in report.CardDirectories)
            {
                writer.WriteStringValue(directory);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("prefabLibraryDirectories");
            foreach (var directory in report.PrefabLibraryDirectories)
            {
                writer.WriteStringValue(directory);
            }

            writer.WriteEndArray();

            writer.WriteStartObject("counts");
            writer.WriteNumber("cardDirectoryCount", report.CardDirectoryCount);
            writer.WriteNumber("prefabLibraryDirectoryCount", report.PrefabLibraryDirectoryCount);
            writer.WriteNumber("successfulCardCount", report.SuccessfulCardCount);
            writer.WriteNumber("successfulLibrarySnapshotCount", report.SuccessfulLibrarySnapshotCount);
            writer.WriteNumber("successfulInlineSnapshotCount", report.SuccessfulInlineSnapshotCount);
            writer.WriteNumber("successfulSnapshotCount", report.SuccessfulSnapshotCount);
            writer.WriteNumber("unresolvedReferenceCount", report.UnresolvedReferenceCount);
            writer.WriteNumber("errorCount", report.ErrorCount);
            writer.WriteNumber("warningCount", report.WarningCount);
            writer.WriteEndObject();

            writer.WriteBoolean("hasNoErrors", report.HasNoErrors);

            WriteEntries(writer, "errors", report.Errors);
            WriteEntries(writer, "warnings", report.Warnings);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteEntries(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<CardDistributionValidationEntry> entries)
    {
        writer.WriteStartArray(propertyName);

        foreach (var entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteString("category", entry.Category.ToString());
            writer.WriteString("message", entry.Message);
            WriteNullableString(writer, "file", entry.File);
            WriteNullableString(writer, "cardId", entry.CardId);
            WriteNullableString(writer, "prefabId", entry.PrefabId);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }
}
