using System.Reflection;
using System.Text;
using System.Text.Json;
using Orc.Cards;
using Orc.Core;

namespace Orc.Output;

/// <summary>
/// 快照 JSON 序列化器（S5）：单实体（从引用/对象导出）与全量（引擎卡牌登记）快照。
/// 单实体结构＝{ entityId, name, alive, components }——entityId＝<see cref="Entity.Id"/>（稳定标识）；
/// components 键＝数据组件简单类型名（Type.Name；同名跨命名空间的冲突为已知限制），值＝公共可读实例属性（反射序列化，原型级）；
/// 组件属性值与事件流 data 共用 <see cref="JsonValueWriter"/> 的确定性降级口径（引用型值不递归导出目标——防环；getter 异常＝{"$error": 根因类型名} 降级，反射包装已解包）。
/// 失效目标（已销毁实体/失效引用）降级输出：alive=false 标记、不抛 <see cref="StaleReferenceException"/>；null 参数照常抛参数类异常。
/// 全量结构＝{ cards: [ 单实体结构… ] }（登记序；逐卡复用单实体结构）；多引擎实例各自独立；空快照＝合法空集合。
/// </summary>
public static class SnapshotJson
{
    /// <summary>单实体快照（对象形态；内部经其引用（<see cref="Entity.Ref"/>）解析）。</summary>
    /// <exception cref="ArgumentNullException">entity 为 null。</exception>
    public static string Serialize(Entity entity, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Serialize(entity.Ref, indented);
    }

    /// <summary>单实体快照（引用形态，含 <c>card.Ref</c>）：失效引用降级输出（不抛 StaleReferenceException）。</summary>
    /// <exception cref="ArgumentNullException">reference 为 null。</exception>
    public static string Serialize(Ref<Entity> reference, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(reference);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            WriteEntitySnapshot(writer, reference);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>全量快照（引擎卡牌登记；登记序；逐卡复用单实体结构）。</summary>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    public static string SerializeAll(LogicEngine engine, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(engine);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("cards");
            foreach (var card in engine.Cards)
            {
                WriteEntitySnapshot(writer, card.Ref);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>写单实体快照：entityId / name / alive / components（失效输入＝降级输出，不抛；标识与组件尽力给出）。</summary>
    private static void WriteEntitySnapshot(Utf8JsonWriter writer, Ref<Entity> reference)
    {
        var alive = reference.IsAlive;
        var target = alive ? reference.Value : reference.TargetForObservation;

        writer.WriteStartObject();
        writer.WriteString("entityId", target.Id.ToString("N"));
        writer.WriteString("name", target.Name);
        writer.WriteBoolean("alive", alive);

        writer.WriteStartObject("components");
        if (target is Card card)
        {
            WriteComponents(writer, card);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>写卡牌数据组件：键＝简单类型名；值＝公共可读实例属性（反射；getter 异常＝{"$error": 类型名} 降级）。</summary>
    private static void WriteComponents(Utf8JsonWriter writer, Card card)
    {
        foreach (var pair in card.DataComponents)
        {
            writer.WritePropertyName(pair.Key.Name);
            writer.WriteStartObject();
            foreach (var property in pair.Key.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);
                try
                {
                    JsonValueWriter.Write(writer, property.GetValue(pair.Value));
                }
                catch (Exception ex)
                {
                    // getter 异常降级（可诊断）：反射调用包装（TargetInvocationException）解包至根因。
                    var cause = (ex as TargetInvocationException)?.InnerException ?? ex;
                    writer.WriteStartObject();
                    writer.WriteString("$error", cause.GetType().Name);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
        }
    }
}
