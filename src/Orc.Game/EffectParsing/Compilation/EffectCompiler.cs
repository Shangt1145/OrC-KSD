using System.Text;
using Orc.Cards;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Templates;

namespace Orc.Game.EffectParsing.Compilation;

/// <summary>
/// 效果转换器（转换段 S4）：DSL 实例 + 模板效果 → **带 csx 的 <see cref="EffectSnapshot"/>**。
/// <para>规则：主触发器/其它触发器/modings/injects 来自模板；被槽位指认的事件 handler＝
/// 由 op 语句模板渲染的 csx 填入；其余事件沿用模板固定 csx。实例 <c>id</c>/<c>version</c> 由调用方给出，<c>stableKey</c> 沿用模板。</para>
/// </summary>
public sealed class EffectCompiler
{
    private readonly IReadOnlyDictionary<string, EffectTemplate> _templates;
    private readonly OpTemplateCatalog _ops;

    /// <summary>创建转换器。</summary>
    /// <exception cref="ArgumentNullException">templates 或 ops 为 null。</exception>
    /// <exception cref="InvalidOperationException">模板 id 重复。</exception>
    public EffectCompiler(IEnumerable<EffectTemplate> templates, OpTemplateCatalog ops)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(ops);

        _templates = EffectTemplateLoader.Index(templates);
        _ops = ops;
    }

    /// <summary>编译 DSL 实例为效果快照。</summary>
    /// <exception cref="ArgumentNullException">dsl 为 null。</exception>
    /// <exception cref="ArgumentException">effectId 空白。</exception>
    /// <exception cref="InvalidOperationException">模板缺失、槽位覆盖不全、op/参数非法或事件无 handler 来源。</exception>
    public EffectSnapshot Compile(DslEffectInstance dsl, string effectId, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(dsl);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);

        if (!_templates.TryGetValue(dsl.Template, out var template))
        {
            throw new InvalidOperationException($"找不到模板效果：'{dsl.Template}'。");
        }

        var handlers = ResolveHandlers(template, dsl);

        var mainTrigger = BuildTrigger(template.Root.MainTrigger, template, handlers);
        var others = template.Root.OtherTriggers.Select(trigger => BuildTrigger(trigger, template, handlers)).ToList();

        var modings = template.Root.Modings.Select(m =>
        {
            if (string.IsNullOrWhiteSpace(m.Replacement.Csx))
            {
                throw new InvalidOperationException($"模板 '{template.Id}' 的 moding 替换事件 '{m.Replacement.Id}' 缺少 csx。");
            }

            return new ModingPrefab(m.TargetEventId, new EventPrefab(m.Replacement.Id, m.Replacement.Entry, csxSource: m.Replacement.Csx));
        }).ToList();

        var injects = template.Root.Injects
            .Select(i => new InjectPrefab(i.TargetTriggerName, i.BandName, i.EventId, i.Priority))
            .ToList();

        var prefab = new EffectPrefab(effectId, mainTrigger, others, modings, version, injects);
        return new EffectSnapshot(prefab);
    }

    /// <summary>编译并序列化为效果快照 JSON 文本。</summary>
    /// <exception cref="ArgumentNullException">dsl 为 null。</exception>
    public string CompileToJson(DslEffectInstance dsl, string effectId, bool indented = true, int version = 1) =>
        PrefabJson.Serialize(Compile(dsl, effectId, version), indented);

    private Dictionary<(EffectTemplateTrigger, int), string> ResolveHandlers(EffectTemplate template, DslEffectInstance dsl)
    {
        var declared = new HashSet<string>(template.Slots.Select(slot => slot.Name), StringComparer.Ordinal);

        foreach (var key in dsl.Fills.Keys)
        {
            if (!declared.Contains(key))
            {
                throw new InvalidOperationException($"DSL 槽位 '{key}' 不在模板 '{template.Id}' 的槽位声明内。");
            }
        }

        var handlers = new Dictionary<(EffectTemplateTrigger, int), string>();
        foreach (var slot in template.Slots)
        {
            if (!dsl.Fills.TryGetValue(slot.Name, out var fill))
            {
                throw new InvalidOperationException($"模板 '{template.Id}' 的槽位 '{slot.Name}' 未被 DSL 填写。");
            }

            if (!EffectTemplateSlots.TryResolve(template, slot, out var reference, out var error))
            {
                throw new InvalidOperationException(error);
            }

            handlers[(reference.Trigger, reference.EventIndex)] = RenderBody(template, slot.Name, reference.Trigger, fill);
        }

        return handlers;
    }

    private string RenderBody(EffectTemplate template, string slotName, EffectTemplateTrigger trigger, DslSlotFill fill)
    {
        var builder = new StringBuilder();
        foreach (var op in fill.Ops)
        {
            var errors = DslOpRegistry.Validate(op);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"模板 '{template.Id}' 的槽位 '{slotName}' 含非法 op：{string.Join(" ", errors)}");
            }

            if (!_ops.Has(op.Op))
            {
                throw new InvalidOperationException($"模板 '{template.Id}' 的槽位 '{slotName}' 的 op '{op.Op}' 无 csx 语句模板。");
            }

            var rendered = _ops.Render(op);
            if (op.Condition is null)
            {
                builder.AppendLine(rendered);
                continue;
            }

            // 条件（A）：owner.*＝真实 csx 守卫；raw＝占位（if (false /* TODO */)）。
            builder.Append("if (").Append(ConditionExpression(op.Condition)).AppendLine(")");
            builder.AppendLine("{");
            foreach (var line in rendered.Split('\n'))
            {
                builder.Append("    ").AppendLine(line.TrimEnd('\r'));
            }

            builder.AppendLine("}");
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static string ConditionExpression(DslCondition condition) => condition.Kind switch
    {
        DslCondition.OwnerSame =>
            $"self is {CardBaseType} actorSelf && view.Card is {CardBaseType} actorEvent"
            + " && actorSelf.Owner is not null && object.ReferenceEquals(actorSelf.Owner, actorEvent.Owner)",
        DslCondition.OwnerDifferent =>
            $"!(self is {CardBaseType} actorSelf && view.Card is {CardBaseType} actorEvent"
            + " && actorSelf.Owner is not null && object.ReferenceEquals(actorSelf.Owner, actorEvent.Owner))",
        _ => $"false /* TODO 条件占位：{EscapeComment(condition.Raw)} */",
    };

    // csx 默认导入不含 Orc.Game.*，故游戏层类型一律**全限定**（否则编译失败——E1 编译校验会拦下）。
    private const string CardBaseType = "Orc.Game.Cards.CardBase";

    private static string EscapeComment(string? text) =>
        text?.Replace("*/", "* /", StringComparison.Ordinal) ?? string.Empty;

    private static TriggerPrefab BuildTrigger(
        EffectTemplateTrigger trigger,
        EffectTemplate template,
        IReadOnlyDictionary<(EffectTemplateTrigger, int), string> handlers)
    {
        var events = new List<EventPrefab>(trigger.Events.Count);
        for (var i = 0; i < trigger.Events.Count; i++)
        {
            var ev = trigger.Events[i];
            string? csx;
            if (handlers.TryGetValue((trigger, i), out var body))
            {
                csx = WrapHandler(trigger.ViewTypeName, ev.Entry, body, template.ActorFrom);
            }
            else
            {
                csx = ev.Csx;
            }

            if (string.IsNullOrWhiteSpace(csx))
            {
                throw new InvalidOperationException(
                    $"模板 '{template.Id}' 的事件 '{trigger.Id}.{ev.Id}' 无 handler 来源（既非槽位、又无固定 csx）。");
            }

            events.Add(new EventPrefab(ev.Id, ev.Entry, csxSource: csx));
        }

        return new TriggerPrefab(
            trigger.Id,
            trigger.StableKey,
            trigger.Kind,
            trigger.Hooks,
            events,
            viewTypeName: trigger.ViewTypeName);
    }

    /// <summary>
    /// 把渲染出的 body 包裹成规定入口变量（csx 入口必须是顶层具名变量），并按模板声明的
    /// <see cref="ActorFrom"/> 生成**施动卡变量 <c>self</c>**（op 模板统一使用 <c>self</c>）。
    /// </summary>
    private static string WrapHandler(string viewTypeName, string entryName, string body, ActorFrom actorFrom)
    {
        var builder = new StringBuilder();
        // 视图类型一律**全限定**（csx 默认导入不含游戏层命名空间，如 Orc.Game.Commanding）。
        builder.Append("Func<").Append(viewTypeName)
            .Append(", Context, CancellationToken, Task> ").Append(entryName)
            .AppendLine(" = async (view, ctx, ct) =>");
        builder.AppendLine("{");
        builder.Append("    var self = ").Append(
            actorFrom == ActorFrom.EffectHost
                ? "view.Host as Card;"
                : "view.Card as Card;").AppendLine();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            builder.Append("    ").AppendLine(trimmed);
        }

        builder.Append("};");
        return builder.ToString();
    }
}
