using Orc.Core;

namespace Orc.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// S-C5 预制体模型（三层）与效果快照：
//   事件预制体（handler 声明；来源＝csx 内联文本 / 程序集注册键）→
//   触发器预制体（触发器定义：稳定键 / Kind / hooks / 事件集合）→
//   效果预制体（主触发器 + 其它触发器预制体 + moding 声明）。
// 效果快照（EffectSnapshot）= schemaVersion + 效果预制体（嵌套三层，自包含）；
// 反序列化产出**有序装载计划**（EffectLoadPlan）——数据（快照）与动作（计划）分离，均可独立审查。
// 来源承载（S-C11）：内置预制体走程序集键；动态/社区预制体走 csx 内联文本。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 事件预制体（S-C5；三层之最内层）：具名、可参数化的 handler 逻辑声明。
/// 来源**二选一**（恰一种）：<see cref="CsxSource"/>（内联 csx 文本，动态/社区）或
/// <see cref="AssemblyKey"/>（程序集显式注册键，内置）。入口名默认 <c>HandleAsync</c>。
/// </summary>
public sealed class EventPrefab
{
    /// <summary>创建事件预制体（来源二选一，构造期 fail-fast）。</summary>
    /// <exception cref="ArgumentException">id/entryName 空白；来源不是"恰一种"；来源值为空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">version ≤ 0。</exception>
    public EventPrefab(
        string id,
        string entryName = "HandleAsync",
        string? csxSource = null,
        string? assemblyKey = null,
        IEnumerable<string>? downstream = null,
        int version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        if (csxSource is null && assemblyKey is null)
        {
            throw new ArgumentException("事件预制体必须提供一种来源（csxSource 或 assemblyKey）。", nameof(csxSource));
        }

        if (csxSource is not null && assemblyKey is not null)
        {
            throw new ArgumentException("事件预制体来源必须恰一种（csxSource 与 assemblyKey 不可同给）。", nameof(csxSource));
        }

        if (csxSource is not null && string.IsNullOrWhiteSpace(csxSource))
        {
            throw new ArgumentException("csxSource 不能为空白。", nameof(csxSource));
        }

        if (assemblyKey is not null && string.IsNullOrWhiteSpace(assemblyKey))
        {
            throw new ArgumentException("assemblyKey 不能为空白。", nameof(assemblyKey));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "预制体版本须为正整数。");
        }

        Id = id;
        EntryName = entryName;
        CsxSource = csxSource;
        AssemblyKey = assemblyKey;
        Downstream = ValidateDownstream(downstream);
        Version = version;
    }

    /// <summary>预制体标识（注册/引用键）。</summary>
    public string Id { get; }

    /// <summary>入口名（csx 侧要被解析的函数名；默认 <c>HandleAsync</c>）。</summary>
    public string EntryName { get; }

    /// <summary>csx 内联源码（动态/社区来源；null＝程序集来源）。</summary>
    public string? CsxSource { get; }

    /// <summary>程序集显式注册键（内置来源；null＝csx 来源）。</summary>
    public string? AssemblyKey { get; }

    /// <summary>声明的下游触发器稳定键（审查链取边；空＝未声明）。</summary>
    public IReadOnlyList<string> Downstream { get; }

    /// <summary>版本（引用形态 <c>{id, version}</c> 的组成；不匹配＝结构化失败）。</summary>
    public int Version { get; }

    /// <summary>是否为内联（csx）来源。</summary>
    public bool IsInline => CsxSource is not null;

    private static string[] ValidateDownstream(IEnumerable<string>? downstream)
    {
        if (downstream is null)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var key in downstream)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("downstream 不能含 null/空白项。", nameof(downstream));
            }

            if (list.Contains(key))
            {
                throw new ArgumentException($"downstream 含重复项 '{key}'。", nameof(downstream));
            }

            list.Add(key);
        }

        return list.ToArray();
    }
}

/// <summary>
/// 触发器预制体（S-C5；三层之中层）：触发器定义声明（稳定键 / Kind / hooks / band 方案名 / 事件集合）。
/// <see cref="StableKey"/> 即定义级身份来源（与 <see cref="Trigger{TView}.StableKey"/> 对齐）。
/// </summary>
public sealed class TriggerPrefab
{
    /// <summary>创建触发器预制体（构造期 fail-fast；主动触发器不得声明 hooks）。</summary>
    /// <exception cref="ArgumentException">id/stableKey 空白；主动 + 非空 hooks。</exception>
    /// <exception cref="ArgumentNullException">events 含 null 元素。</exception>
    /// <exception cref="ArgumentOutOfRangeException">version ≤ 0。</exception>
    public TriggerPrefab(
        string id,
        string stableKey,
        TriggerKind kind = TriggerKind.Active,
        IEnumerable<string>? hooks = null,
        IEnumerable<EventPrefab>? events = null,
        int version = 1,
        string viewTypeName = "Orc.Cards.CardEventView")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewTypeName);

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "预制体版本须为正整数。");
        }

        var hookList = new List<string>();
        if (hooks is not null)
        {
            foreach (var hook in hooks)
            {
                if (string.IsNullOrWhiteSpace(hook))
                {
                    throw new ArgumentException("hooks 不能含 null/空白项。", nameof(hooks));
                }

                if (hookList.Contains(hook))
                {
                    throw new ArgumentException($"hooks 含重复项 '{hook}'。", nameof(hooks));
                }

                hookList.Add(hook);
            }
        }

        if (hookList.Count > 0 && kind == TriggerKind.Active)
        {
            throw new ArgumentException(
                "主动触发器预制体不能声明 hooks（矛盾声明：主动触发器不参与挂载）。", nameof(hooks));
        }

        var eventList = new List<EventPrefab>();
        if (events is not null)
        {
            foreach (var item in events)
            {
                if (item is null)
                {
                    throw new ArgumentNullException(nameof(events), "事件集合不能包含 null 元素。");
                }

                eventList.Add(item);
            }
        }

        Id = id;
        StableKey = stableKey;
        Kind = kind;
        Hooks = hookList;
        Events = eventList;
        Version = version;
        ViewTypeName = viewTypeName;
    }

    /// <summary>预制体标识。</summary>
    public string Id { get; }

    /// <summary>触发器稳定键（定义级身份）。</summary>
    public string StableKey { get; }

    /// <summary>种类（主动/被动）。</summary>
    public TriggerKind Kind { get; }

    /// <summary>hook 名（声明顺序；空＝未声明）。</summary>
    public IReadOnlyList<string> Hooks { get; }

    /// <summary>事件预制体集合（声明序＝注册序）。</summary>
    public IReadOnlyList<EventPrefab> Events { get; }

    /// <summary>版本。</summary>
    public int Version { get; }

    /// <summary>视图类型名（程序集限定名或全名；动态实例化时解析为 <see cref="Type"/>；默认 CardEventView）。</summary>
    public string ViewTypeName { get; }
}

/// <summary>
/// moding 声明（S-C5；随效果快照往返）：以目标事件预制体 id 锚定替换对象，
/// 实例化时按声明序重建 moding 栈（保留运行期注销、回退基础逻辑的语义）。
/// </summary>
/// <param name="TargetEventId">目标事件预制体 id（须在本效果预制体的触发器内）。</param>
/// <param name="Replacement">替换逻辑预制体。</param>
public sealed record ModingPrefab(string TargetEventId, EventPrefab Replacement);

/// <summary>
/// 效果预制体（S-C5；三层之外层）：效果定义声明＝主触发器 + 其它触发器预制体 + moding 声明。
/// <see cref="MountedHooks"/> 为计算属性：主触发器与其它被动触发器的 hooks 并集（＝"需挂载的 HookID"）。
/// </summary>
public sealed class EffectPrefab
{
    /// <summary>创建效果预制体（构造期 fail-fast）。</summary>
    /// <exception cref="ArgumentNullException">mainTrigger 为 null；集合含 null 元素。</exception>
    /// <exception cref="ArgumentException">id 空白；moding 目标 id 空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">version ≤ 0。</exception>
    public EffectPrefab(
        string id,
        TriggerPrefab mainTrigger,
        IEnumerable<TriggerPrefab>? otherTriggers = null,
        IEnumerable<ModingPrefab>? modings = null,
        int version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(mainTrigger);

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "预制体版本须为正整数。");
        }

        var others = new List<TriggerPrefab>();
        if (otherTriggers is not null)
        {
            foreach (var item in otherTriggers)
            {
                if (item is null)
                {
                    throw new ArgumentNullException(nameof(otherTriggers), "其它触发器集合不能包含 null 元素。");
                }

                others.Add(item);
            }
        }

        var modingList = new List<ModingPrefab>();
        if (modings is not null)
        {
            foreach (var item in modings)
            {
                if (item is null)
                {
                    throw new ArgumentNullException(nameof(modings), "moding 集合不能包含 null 元素。");
                }

                if (string.IsNullOrWhiteSpace(item.TargetEventId))
                {
                    throw new ArgumentException("moding 目标事件 id 不能为空白。", nameof(modings));
                }

                modingList.Add(item);
            }
        }

        Id = id;
        MainTrigger = mainTrigger;
        OtherTriggers = others;
        Modings = modingList;
        Version = version;

        var hooks = new List<string>(mainTrigger.Hooks);
        foreach (var other in others)
        {
            foreach (var hook in other.Hooks)
            {
                if (!hooks.Contains(hook))
                {
                    hooks.Add(hook);
                }
            }
        }

        MountedHooks = hooks;
    }

    /// <summary>预制体标识。</summary>
    public string Id { get; }

    /// <summary>主触发器预制体。</summary>
    public TriggerPrefab MainTrigger { get; }

    /// <summary>其它触发器预制体（声明序）。</summary>
    public IReadOnlyList<TriggerPrefab> OtherTriggers { get; }

    /// <summary>moding 声明（声明序＝栈序）。</summary>
    public IReadOnlyList<ModingPrefab> Modings { get; }

    /// <summary>"需挂载的 HookID"（计算属性：主触发器与其它被动触发器 hooks 的并集，去重保序）。</summary>
    public IReadOnlyList<string> MountedHooks { get; }

    /// <summary>版本。</summary>
    public int Version { get; }
}

/// <summary>
/// 效果快照（S-C5/S-C10）：schemaVersion + 效果预制体（嵌套三层、自包含）。
/// 反序列化产出**有序装载计划**（<see cref="BuildLoadPlan"/>）——计划可独立审查/重放。
/// </summary>
public sealed class EffectSnapshot
{
    /// <summary>效果快照 schema 版本（本版＝1）。</summary>
    public const int SchemaVersion = 1;

    /// <summary>创建效果快照。</summary>
    /// <exception cref="ArgumentNullException">root 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">schemaVersion ≤ 0。</exception>
    public EffectSnapshot(EffectPrefab root, int schemaVersion = SchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "schema 版本须为正整数。");
        }

        Root = root;
        Version = schemaVersion;
    }

    /// <summary>schema 版本。</summary>
    public int Version { get; }

    /// <summary>根效果预制体。</summary>
    public EffectPrefab Root { get; }

    /// <summary>
    /// 生成有序装载计划：被动＝【挂主触发器（含 hooks）】→【注册事件】（主触发器→其它触发器）
    /// →【施加 moding】；主动＝无挂载步（纯动作型，无 hook 段），仅【注册事件】→【施加 moding】。
    /// </summary>
    public EffectLoadPlan BuildLoadPlan()
    {
        var steps = new List<EffectLoadStep>();
        var main = Root.MainTrigger;

        var mainMounted = main.Kind == TriggerKind.Passive && main.Hooks.Count > 0;
        if (mainMounted)
        {
            steps.Add(new EffectLoadStep(
                EffectLoadStepKind.MountMainTrigger, main.StableKey, $"hooks: {string.Join(", ", main.Hooks)}"));
        }

        AddRegisterSteps(steps, main);
        foreach (var other in Root.OtherTriggers)
        {
            if (other.Kind == TriggerKind.Passive && other.Hooks.Count > 0)
            {
                steps.Add(new EffectLoadStep(
                    EffectLoadStepKind.MountTrigger, other.StableKey, $"hooks: {string.Join(", ", other.Hooks)}"));
            }

            AddRegisterSteps(steps, other);
        }

        foreach (var moding in Root.Modings)
        {
            steps.Add(new EffectLoadStep(
                EffectLoadStepKind.ApplyModing, main.StableKey, $"{moding.TargetEventId} <- {moding.Replacement.Id}"));
        }

        return new EffectLoadPlan(steps);
    }

    private static void AddRegisterSteps(List<EffectLoadStep> steps, TriggerPrefab trigger)
    {
        foreach (var ev in trigger.Events)
        {
            var source = ev.IsInline ? $"csx:{ev.EntryName}" : $"asm:{ev.AssemblyKey}";
            steps.Add(new EffectLoadStep(EffectLoadStepKind.RegisterEvent, trigger.StableKey, $"{ev.Id} ({source})"));
        }
    }
}

/// <summary>装载计划（S-C5；由 <see cref="EffectSnapshot.BuildLoadPlan"/> 产出）。</summary>
public sealed class EffectLoadPlan
{
    internal EffectLoadPlan(IReadOnlyList<EffectLoadStep> steps) => Steps = steps;

    /// <summary>有序装载步骤。</summary>
    public IReadOnlyList<EffectLoadStep> Steps { get; }
}

/// <summary>装载步骤种类（S-C5）。</summary>
public enum EffectLoadStepKind
{
    /// <summary>挂载主触发器。</summary>
    MountMainTrigger,

    /// <summary>挂载其它（非主）触发器。</summary>
    MountTrigger,

    /// <summary>注册事件（handler 绑定）。</summary>
    RegisterEvent,

    /// <summary>施加 moding（逻辑替换）。</summary>
    ApplyModing,
}

/// <summary>装载步骤（S-C5）：种类 + 目标（触发器稳定键）+ 明细（确定性文本）。</summary>
/// <param name="Kind">步骤种类。</param>
/// <param name="Target">目标触发器稳定键。</param>
/// <param name="Detail">明细文本（可读、确定性）。</param>
public sealed record EffectLoadStep(EffectLoadStepKind Kind, string Target, string Detail);
