#pragma warning disable CS8618 // 视图属性值由框架在绑定时提供；声明期不做初始化（视图类不应写属性初始化器）。

using Orc.Core;

namespace Orc.Tests;

/// <summary>S3 总线测试共享视图：部署载荷场景（Card 必填；Amount/Marker 可选）。</summary>
[ContextView]
public class BusPayloadView
{
    [Read]
    public virtual Ref<Entity> Card { get; set; }

    [Optional]
    [Mutate]
    public virtual int Amount { get; set; }

    [Optional]
    [Read]
    public virtual string? Marker { get; set; }
}

/// <summary>S3 总线测试共享工具。</summary>
internal static class BusTestHelpers
{
    /// <summary>构建被动触发器：单"记录"事件（执行时把 name 追加进 trace；视图用全可选视图，任意载荷可绑定）。</summary>
    internal static Trigger<CounterView> RecordingPassive(
        string name, string[] hooks, List<string> trace, int priority = UpdatePriorities.Normal, object? owner = null)
    {
        return new Trigger<CounterView>(
            name,
            TriggerKind.Passive,
            events: new[]
            {
                new TriggerEvent<CounterView>("记录", (v, c, ct) =>
                {
                    trace.Add(name);
                    return Task.CompletedTask;
                }),
            },
            hooks: hooks,
            priority: priority,
            owner: owner);
    }
}
