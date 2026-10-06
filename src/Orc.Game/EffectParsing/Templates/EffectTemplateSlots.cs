using System.Text.RegularExpressions;

namespace Orc.Game.EffectParsing.Templates;

/// <summary>槽位解析结果：槽位指向的（模板触发器，事件下标）。</summary>
/// <param name="Trigger">被指认的模板触发器。</param>
/// <param name="EventIndex">该触发器内的事件下标。</param>
public readonly record struct TemplateSlotRef(EffectTemplateTrigger Trigger, int EventIndex);

/// <summary>
/// 槽位定位与模板校验（转换段 S2）：
/// 定位语法＝<c>mainTrigger.events[e1]</c> 或 <c>otherTriggers[&lt;触发器 id&gt;].events[e2]</c>（**事件 id 引用**）。
/// </summary>
public static class EffectTemplateSlots
{
    private static readonly Regex MainPattern = new(
        @"^mainTrigger\.events\[(?<event>[^\]]+)\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OtherPattern = new(
        @"^otherTriggers\[(?<trigger>[^\]]+)\]\.events\[(?<event>[^\]]+)\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>解析定位表达式。</summary>
    /// <param name="target">定位表达式。</param>
    /// <param name="isMain">是否指向主触发器。</param>
    /// <param name="triggerId">其它触发器 id（主触发器时为 null）。</param>
    /// <param name="eventId">事件 id。</param>
    /// <returns>语法是否合法。</returns>
    public static bool TryParseTarget(string target, out bool isMain, out string? triggerId, out string eventId)
    {
        isMain = false;
        triggerId = null;
        eventId = string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var main = MainPattern.Match(target);
        if (main.Success)
        {
            isMain = true;
            eventId = main.Groups["event"].Value;
            return true;
        }

        var other = OtherPattern.Match(target);
        if (other.Success)
        {
            triggerId = other.Groups["trigger"].Value;
            eventId = other.Groups["event"].Value;
            return true;
        }

        return false;
    }

    /// <summary>把槽位解析为模板内的触发器 + 事件下标。</summary>
    /// <param name="template">模板。</param>
    /// <param name="slot">槽位声明。</param>
    /// <param name="reference">解析结果。</param>
    /// <param name="error">失败原因。</param>
    public static bool TryResolve(
        EffectTemplate template,
        EffectTemplateSlot slot,
        out TemplateSlotRef reference,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(slot);

        reference = default;
        if (!TryParseTarget(slot.Target, out var isMain, out var triggerId, out var eventId))
        {
            error = $"槽位 '{slot.Name}' 的定位 '{slot.Target}' 不是合法表达式（应为 mainTrigger.events[e1] 或 otherTriggers[<id>].events[e2]）。";
            return false;
        }

        EffectTemplateTrigger? trigger;
        if (isMain)
        {
            trigger = template.Root.MainTrigger;
        }
        else
        {
            trigger = template.Root.OtherTriggers.FirstOrDefault(item => string.Equals(item.Id, triggerId, StringComparison.Ordinal));
            if (trigger is null)
            {
                error = $"槽位 '{slot.Name}' 指向的触发器 '{triggerId}' 不存在。";
                return false;
            }
        }

        var index = -1;
        for (var i = 0; i < trigger.Events.Count; i++)
        {
            if (string.Equals(trigger.Events[i].Id, eventId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            error = $"槽位 '{slot.Name}' 指向的事件 '{eventId}' 不存在。";
            return false;
        }

        error = null;
        reference = new TemplateSlotRef(trigger, index);
        return true;
    }

    /// <summary>校验模板（结构 + 槽位语义）。返回错误清单（空＝通过）。</summary>
    /// <exception cref="ArgumentNullException">template 为 null。</exception>
    public static IReadOnlyList<string> Validate(EffectTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var errors = new List<string>();

        if (template.SchemaVersion != EffectTemplate.CurrentSchemaVersion)
        {
            errors.Add($"模板效果 schema 版本不符：期望 {EffectTemplate.CurrentSchemaVersion}，实际 {template.SchemaVersion}（不静默降级）。");
        }

        var slotNames = new HashSet<string>(StringComparer.Ordinal);
        var slotTargets = new HashSet<(EffectTemplateTrigger, int)>();

        foreach (var slot in template.Slots)
        {
            if (!slotNames.Add(slot.Name))
            {
                errors.Add($"模板 '{template.Id}' 的槽位名重复：'{slot.Name}'。");
            }

            if (!TryResolve(template, slot, out var reference, out var error))
            {
                errors.Add(error!);
                continue;
            }

            // 同一事件不得被两个槽位重复声明
            if (!slotTargets.Add((reference.Trigger, reference.EventIndex)))
            {
                errors.Add($"模板 '{template.Id}' 的槽位 '{slot.Name}' 与其他槽位指向同一事件。");
            }

            var target = reference.Trigger.Events[reference.EventIndex];
            if (!string.IsNullOrWhiteSpace(target.Csx))
            {
                errors.Add($"模板 '{template.Id}' 的槽位 '{slot.Name}' 指向的事件 '{target.Id}' 已有固定 csx（不得作为可填写槽位）。");
            }
        }

        // 非槽位事件必须自带固定 csx
        foreach (var trigger in EnumerateTriggers(template.Root))
        {
            for (var i = 0; i < trigger.Events.Count; i++)
            {
                var ev = trigger.Events[i];
                if (slotTargets.Contains((trigger, i)))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ev.Csx))
                {
                    errors.Add($"模板 '{template.Id}' 的事件 '{trigger.Id}.{ev.Id}' 既非槽位、又无固定 csx。");
                }
            }
        }

        return errors;
    }

    /// <summary>枚举模板内全部触发器（主 + 其它）。</summary>
    public static IEnumerable<EffectTemplateTrigger> EnumerateTriggers(EffectTemplateRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        yield return root.MainTrigger;
        foreach (var trigger in root.OtherTriggers)
        {
            yield return trigger;
        }
    }
}
