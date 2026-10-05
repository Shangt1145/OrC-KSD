using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Orc.Game.Output;

/// <summary>
/// 输入入口全集清单导出器（02-输入接口 S6；<c>invariants.json</c> 式映射）。
/// 风格对齐 <see cref="GameHooksJson"/>（静态类 + <c>Serialize(...)</c> → string、不写文件；
/// 自持 <see cref="Utf8JsonWriter"/> 手写写出、定深结构、无递归）。
/// </summary>
public static class GameEntryPointsJson
{
    /// <summary>序列化为 JSON 文本（确定性；覆盖全部入口与计数汇总）。</summary>
    public static string Serialize(bool indented = false)
    {
        var all = GameEntryPoints.All;
        var available = 0;
        var pending = 0;
        foreach (var entry in all)
        {
            if (entry.IsAvailable)
            {
                available++;
            }
            else
            {
                pending++;
            }
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("entryPoints");
            foreach (var entry in all)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteString("category", entry.Category);
                writer.WriteString("owner", entry.Owner);
                writer.WriteString("signature", entry.Signature);
                writer.WriteString("gate", entry.Gate);
                writer.WriteString("outcome", entry.Outcome);
                writer.WriteString("status", StatusText(entry.Status));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartObject("counts");
            writer.WriteNumber("total", all.Count);
            writer.WriteNumber("available", available);
            writer.WriteNumber("pendingDelivery", pending);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>交付状态文本（与文档口径一致）。</summary>
    private static string StatusText(GameEntryPointStatus status) => status switch
    {
        GameEntryPointStatus.Available => "已具备",
        GameEntryPointStatus.PendingDelivery => "01待交付",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未定义的入口交付状态。"),
    };
}
