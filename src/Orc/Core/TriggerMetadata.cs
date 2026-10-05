namespace Orc.Core;

/// <summary>
/// 触发器元数据只读面（S-C3；编排管理器与审查链用）：以非泛型视角暴露触发器的定义级身份与结构信息。
/// 由 <see cref="Trigger{TView}"/> 实现（显式实现内部面：展示名/hooks/所有者/挂载优先级）。
/// 不含 handler 委托本体；事件以 <see cref="TriggerEventInfo"/>（含注册序句柄）标识。
/// </summary>
public interface ITriggerMetadata
{
    /// <summary>触发器种类的稳定标识（定义级；跨对局/跨机器一致）。</summary>
    TriggerId Id { get; }

    /// <summary>稳定键（定义级身份来源；声明值或回退派生值）。</summary>
    string StableKey { get; }

    /// <summary>是否由作者显式声明稳定键；false＝弱身份。</summary>
    bool HasDeclaredStableKey { get; }

    /// <summary>展示名（名称非空白用名称，否则视图类型名退化）。</summary>
    string DisplayName { get; }

    /// <summary>触发器种类（主动/被动）。</summary>
    TriggerKind Kind { get; }

    /// <summary>hook 名（声明顺序；空＝未声明）。</summary>
    IReadOnlyList<string> HookNames { get; }

    /// <summary>事件信息（执行序快照）。</summary>
    IReadOnlyList<TriggerEventInfo> Events { get; }

    /// <summary>所有者（构造期声明；可为 null）。</summary>
    object? Owner { get; }

    /// <summary>挂载优先级（构造期声明）。</summary>
    int MountPriority { get; }

    /// <summary>是否处于挂载状态（主动触发器恒 false）。</summary>
    bool IsMounted { get; }
}
