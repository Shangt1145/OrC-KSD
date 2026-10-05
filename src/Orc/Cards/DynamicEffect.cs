using System.Reflection;
using Orc.Core;

namespace Orc.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// S-C8/S-C10 动态效果：由效果快照（预制体）**从文本重建**出携带完整逻辑的效果。
//   · 被动（主触发器 Passive）→ DynamicPassiveEffect（继承 PassiveEffect：保留框架生命周期清理），
//     并把动态主触发器与其它被动触发器一并挂载（owner=效果实例，卸载整批移除）。
//   · 主动（主触发器 Active）→ DynamicActiveEffect（纯动作型，无 hook 段），施放经 CastAsync。
//   实例化＝**执行装载计划**（BuildLoadPlan 的步骤序）：挂主触发器 → 注册事件 → 施加 moding；
//   交由既有装载链（Effect.ExecuteMount/ExecuteUnmount）驱动生效与清理。
//   视图类型为泛型参数，故触发器经反射构造、注册/挂载/施放经泛型适配（调用侧无泛型耦合）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>动态效果实例化结果（S-C8；结构化——失败不抛）。</summary>
public sealed class DynamicEffectInstantiation
{
    private DynamicEffectInstantiation(bool success, Effect? effect, EffectLoadPlan? plan, string? errorCategory, string? error)
    {
        Success = success;
        Effect = effect;
        Plan = plan;
        ErrorCategory = errorCategory;
        Error = error;
    }

    /// <summary>是否成功。</summary>
    public bool Success { get; }

    /// <summary>实例化的动态效果（成功时非 null）。</summary>
    public Effect? Effect { get; }

    /// <summary>该效果对应的装载计划（成功时非 null）。</summary>
    public EffectLoadPlan? Plan { get; }

    /// <summary>失败分类。</summary>
    public string? ErrorCategory { get; }

    /// <summary>失败消息。</summary>
    public string? Error { get; }

    internal static DynamicEffectInstantiation Ok(Effect effect, EffectLoadPlan plan) => new(true, effect, plan, null, null);

    public static DynamicEffectInstantiation Fail(string category, string error) => new(false, null, null, category, error);
}

/// <summary>动态效果的构建状态（S-C8 内部）：主触发器 + 视图类型 + 其它触发器。</summary>
internal sealed class DynamicBuildState
{
    internal DynamicBuildState(Type mainViewType, object mainTrigger)
    {
        MainViewType = mainViewType;
        MainTrigger = mainTrigger;
    }

    internal Type MainViewType { get; }

    internal object MainTrigger { get; }

    internal List<(object Trigger, Type ViewType)> OtherTriggers { get; } = new();

    /// <summary>事件 id → handler（S5 注入用：把同一 handler 注册进宿主具名触发器）。</summary>
    internal Dictionary<string, Delegate> Handlers { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 动态效果构建器（S-C8 内部）：按装载计划序解析处理器、构造触发器、注册事件并施加 moding。
/// 一切失败结构化返回（不抛）——分类：view-type / handler-missing / evaluator-missing / signature /
/// compile / runtime / entry-missing / sandbox / timeout / moding-target。
/// </summary>
internal static class DynamicEffectBuilder
{
    internal static DynamicEffectInstantiation Build(
        LogicEngine engine, PrefabManager manager, EffectSnapshot snapshot, string name)
    {
        var prefab = snapshot.Root;

        var mainViewType = ResolveViewType(prefab.MainTrigger.ViewTypeName);
        if (mainViewType is null)
        {
            return DynamicEffectInstantiation.Fail(
                "view-type", $"主触发器视图类型 '{prefab.MainTrigger.ViewTypeName}' 无法解析。");
        }

        var isPassive = prefab.MainTrigger.Kind == TriggerKind.Passive;
        Effect effect = isPassive
            ? new DynamicPassiveEffect(name, snapshot, mainViewType)
            : new DynamicActiveEffect(name, snapshot, mainViewType);

        object mainTrigger;
        try
        {
            mainTrigger = TriggerReflection.Create(
                mainViewType, prefab.MainTrigger, effect);
        }
        catch (Exception ex)
        {
            return DynamicEffectInstantiation.Fail("view-type", $"主触发器构造失败：{ex.Message}");
        }

        var state = new DynamicBuildState(mainViewType, mainTrigger);
        var eventsById = new Dictionary<string, TriggerRegistration>(StringComparer.Ordinal);
        var failure = RegisterEvents(engine, manager, mainTrigger, mainViewType, prefab.MainTrigger, eventsById, state);
        if (failure is not null)
        {
            return failure;
        }

        foreach (var otherPrefab in prefab.OtherTriggers)
        {
            var otherViewType = ResolveViewType(otherPrefab.ViewTypeName);
            if (otherViewType is null)
            {
                return DynamicEffectInstantiation.Fail(
                    "view-type", $"触发器 '{otherPrefab.Id}' 视图类型 '{otherPrefab.ViewTypeName}' 无法解析。");
            }

            object otherTrigger;
            try
            {
                otherTrigger = TriggerReflection.Create(otherViewType, otherPrefab, effect);
            }
            catch (Exception ex)
            {
                return DynamicEffectInstantiation.Fail("view-type", $"触发器 '{otherPrefab.Id}' 构造失败：{ex.Message}");
            }

            var otherEvents = new Dictionary<string, TriggerRegistration>(StringComparer.Ordinal);
            failure = RegisterEvents(engine, manager, otherTrigger, otherViewType, otherPrefab, otherEvents, state);
            if (failure is not null)
            {
                return failure;
            }

            state.OtherTriggers.Add((otherTrigger, otherViewType));
        }

        foreach (var moding in prefab.Modings)
        {
            if (!eventsById.TryGetValue(moding.TargetEventId, out var target))
            {
                return DynamicEffectInstantiation.Fail(
                    "moding-target", $"moding 目标事件 '{moding.TargetEventId}' 不在主触发器中。");
            }

            var resolution = manager.ResolveHandler(moding.Replacement, mainViewType);
            if (!resolution.Success)
            {
                return DynamicEffectInstantiation.Fail(resolution.ErrorCategory!, resolution.Error!);
            }

            TriggerReflection.RegisterModing(mainTrigger, mainViewType, target, resolution.Handler!);
        }

        Attach(effect, state);
        return DynamicEffectInstantiation.Ok(effect, snapshot.BuildLoadPlan());
    }

    private static DynamicEffectInstantiation? RegisterEvents(
        LogicEngine engine,
        PrefabManager manager,
        object trigger,
        Type viewType,
        TriggerPrefab prefab,
        Dictionary<string, TriggerRegistration> eventsById,
        DynamicBuildState state)
    {
        foreach (var ev in prefab.Events)
        {
            var resolution = manager.ResolveHandler(ev, viewType);
            if (!resolution.Success)
            {
                return DynamicEffectInstantiation.Fail(resolution.ErrorCategory!, resolution.Error!);
            }

            var registration = TriggerReflection.Register(
                trigger, viewType, ev.EntryName == "HandleAsync" ? ev.Id : ev.Id, resolution.Handler!, ev.Downstream);
            eventsById[ev.Id] = registration;
            state.Handlers[ev.Id] = resolution.Handler!; // S5：注入用的 handler 来源
        }

        return null;
    }

    private static void Attach(Effect effect, DynamicBuildState state)
    {
        switch (effect)
        {
            case DynamicPassiveEffect passive:
                passive.AttachState(state);
                break;
            case DynamicActiveEffect active:
                active.AttachState(state);
                break;
        }
    }

    private static Type? ResolveViewType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is null || !type.IsClass)
        {
            return null;
        }

        return type;
    }
}

/// <summary>
/// 反射适配（S-C8 内部）：触发器为泛型 <c>Trigger&lt;TView&gt;</c>，动态装配需按运行期视图类型分派。
/// 每个方法体在泛型实例化后即为强类型调用（无 DynamicInvoke，无装箱调用开销）。
/// </summary>
internal static class TriggerReflection
{
    private static readonly MethodInfo RegisterMethod = Get(nameof(RegisterCore));
    private static readonly MethodInfo ModingMethod = Get(nameof(ModingCore));
    private static readonly MethodInfo MountMethod = Get(nameof(MountCore));
    private static readonly MethodInfo InvokeMethod = Get(nameof(InvokeCore));

    private static MethodInfo Get(string name) => typeof(TriggerReflection)
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(m => m.Name == name);

    internal static object Create(Type viewType, TriggerPrefab prefab, object owner)
    {
        var triggerType = typeof(Trigger<>).MakeGenericType(viewType);
        var ctor = triggerType.GetConstructors().Single();
        return ctor.Invoke(new object?[]
        {
            prefab.StableKey,
            prefab.Kind,
            null,
            null,
            prefab.Hooks.ToArray(),
            UpdatePriorities.Normal,
            owner,
            prefab.StableKey,
        });
    }

    internal static TriggerRegistration Register(
        object trigger, Type viewType, string name, Delegate handler, IReadOnlyList<string> downstream)
        => (TriggerRegistration)RegisterMethod.MakeGenericMethod(viewType)
            .Invoke(null, new object[] { trigger, name, handler, downstream })!;

    /// <summary>默认区段注册（S5 注入用：无 band、可带优先级）。</summary>
    internal static TriggerRegistration RegisterDefaultBand(
        object trigger, Type viewType, string name, Delegate handler, int priority)
        => (TriggerRegistration)RegisterBandMethod.MakeGenericMethod(viewType)
            .Invoke(null, new object[] { trigger, name, handler, priority })!;

    /// <summary>撤销注册（S5 注入撤销用；目标触发器 <c>Unregister</c> 面）。</summary>
    internal static void Unregister(object trigger, TriggerRegistration registration)
        => UnregisterMethod.Invoke(trigger, new object[] { registration });

    private static readonly MethodInfo RegisterBandMethod = Get(nameof(RegisterBandCore));

    private static readonly MethodInfo UnregisterMethod = typeof(Trigger<>)
        .GetMethod(nameof(Trigger<object>.Unregister), new[] { typeof(TriggerRegistration) })!;

    private static TriggerRegistration RegisterBandCore<TView>(object trigger, string name, Delegate handler, int priority)
        where TView : class
        => ((Trigger<TView>)trigger).Register(name, (Func<TView, Context, CancellationToken, Task>)handler, priority);

    internal static void RegisterModing(object trigger, Type viewType, TriggerRegistration target, Delegate moding)
        => ModingMethod.MakeGenericMethod(viewType).Invoke(null, new object[] { trigger, target, moding });

    internal static void Mount(Bus bus, Type viewType, object trigger)
        => MountMethod.MakeGenericMethod(viewType).Invoke(null, new object[] { bus, trigger });

    internal static Task<EventStream> Invoke(
        LogicEngine engine, Type viewType, object trigger, IDictionary<string, object?>? data, CancellationToken ct)
        => (Task<EventStream>)InvokeMethod.MakeGenericMethod(viewType)
            .Invoke(null, new object?[] { engine, trigger, data, ct })!;

    private static TriggerRegistration RegisterCore<TView>(
        object trigger, string name, Delegate handler, IReadOnlyList<string> downstream) where TView : class
        => ((Trigger<TView>)trigger).Register(name, (Func<TView, Context, CancellationToken, Task>)handler, 0, downstream);

    private static void ModingCore<TView>(object trigger, TriggerRegistration target, Delegate moding) where TView : class
        => ((Trigger<TView>)trigger).RegisterModing(target, (Func<TView, Context, CancellationToken, Task>)moding);

    private static void MountCore<TView>(Bus bus, object trigger) where TView : class
        => bus.Mount((Trigger<TView>)trigger);

    private static Task<EventStream> InvokeCore<TView>(
        LogicEngine engine, object trigger, IDictionary<string, object?>? data, CancellationToken ct) where TView : class
        => ((Trigger<TView>)trigger).InvokeAsync(engine, data, ct);
}

/// <summary>
/// 动态被动效果（S-C8）：由效果快照重建；继承 <see cref="PassiveEffect"/> 以保留框架生命周期清理
/// （effect.removed / card.destroyed → 卸载），并把动态触发器（主 + 其它）一并挂载（owner=效果实例）。
/// </summary>
public sealed class DynamicPassiveEffect : PassiveEffect, ISerializableEffect
{
    private readonly EffectSnapshot _snapshot;
    private readonly Type _viewType;
    private DynamicBuildState? _state;

    internal DynamicPassiveEffect(string name, EffectSnapshot snapshot, Type viewType)
        : base(name)
    {
        _snapshot = snapshot;
        _viewType = viewType;
    }

    /// <summary>本动态效果对应的效果快照（可再次序列化——往返）。</summary>
    public EffectSnapshot Snapshot => _snapshot;

    internal void AttachState(DynamicBuildState state) => _state = state;

    /// <summary>取本效果内某事件的 handler（S5 注入用；未命中＝false）。</summary>
    public bool TryGetEventHandler(string eventId, out Delegate? handler)
    {
        handler = null;
        return _state is not null
            && !string.IsNullOrWhiteSpace(eventId)
            && _state.Handlers.TryGetValue(eventId, out handler);
    }

    internal override void MountMainTrigger(Bus bus)
    {
        base.MountMainTrigger(bus); // 生命周期触发器（owner=this）

        if (_state is null)
        {
            return;
        }

        TriggerReflection.Mount(bus, _state.MainViewType, _state.MainTrigger);
        foreach (var (trigger, viewType) in _state.OtherTriggers)
        {
            TriggerReflection.Mount(bus, viewType, trigger);
        }
    }

    internal override void UnmountMainTrigger()
    {
        // owner＝本效果：UnmountOwner(this) 一次性移除生命周期触发器与全部动态触发器。
        base.UnmountMainTrigger();
    }
}

/// <summary>
/// 动态主动效果（S-C10；纯动作型）：无 hook 段、不参与装载/卸载；施放经 <see cref="CastAsync"/>
/// 调用动态主触发器。其"下游边"可由其它被动触发器 declare 指向（Q5 补充语义）。
/// </summary>
public sealed class DynamicActiveEffect : Effect, ISerializableEffect
{
    private readonly EffectSnapshot _snapshot;
    private readonly Type _viewType;
    private DynamicBuildState? _state;

    internal DynamicActiveEffect(string name, EffectSnapshot snapshot, Type viewType)
        : base(name, TriggerKind.Active)
    {
        _snapshot = snapshot;
        _viewType = viewType;
    }

    /// <summary>本动态效果对应的效果快照。</summary>
    public EffectSnapshot Snapshot => _snapshot;

    internal void AttachState(DynamicBuildState state) => _state = state;

    /// <summary>施放入口（调用动态主触发器；施放事件链按注册序执行）。</summary>
    /// <exception cref="InvalidOperationException">尚未构建（未实例化）。</exception>
    public Task<EventStream> CastAsync(
        LogicEngine engine, IDictionary<string, object?>? data = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (_state is null)
        {
            throw new InvalidOperationException($"动态效果 '{Name}' 尚未实例化（无构建状态）。");
        }

        return TriggerReflection.Invoke(engine, _state.MainViewType, _state.MainTrigger, data, ct);
    }

    internal override void MountMainTrigger(Bus bus)
    {
        // 主动效果不装载（S3 约束：主动触发器不可挂载）。
    }

    internal override void UnmountMainTrigger()
    {
    }
}

/// <summary>动态效果工厂（S-C8；入口）：由效果快照在引擎上实例化动态效果。</summary>
public static class DynamicEffectFactory
{
    /// <summary>
    /// 实例化动态效果（解析视图类型 → 构造触发器 → 解析处理器 → 注册事件 → 施加 moding）。
    /// 失败结构化返回（不抛，除参数为 null）。
    /// </summary>
    /// <exception cref="ArgumentNullException">engine 或 snapshot 为 null。</exception>
    public static DynamicEffectInstantiation Instantiate(
        LogicEngine engine, EffectSnapshot snapshot, string? name = null, PrefabManager? manager = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(snapshot);

        var effectiveManager = manager ?? engine.Prefabs;
        var effectiveName = string.IsNullOrWhiteSpace(name) ? snapshot.Root.Id : name!;
        return DynamicEffectBuilder.Build(engine, effectiveManager, snapshot, effectiveName);
    }

    /// <summary>按预制体 id 从管理器实例化（未注册＝结构化失败）。</summary>
    /// <exception cref="ArgumentNullException">engine 或 prefabId 为 null。</exception>
    public static DynamicEffectInstantiation InstantiateRegistered(
        LogicEngine engine, string prefabId, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(prefabId);

        return engine.Prefabs.TryGetPrefab(prefabId, out var snapshot)
            ? Instantiate(engine, snapshot, name)
            : DynamicEffectInstantiation.Fail("prefab-missing", $"未注册效果预制体 '{prefabId}'。");
    }
}
