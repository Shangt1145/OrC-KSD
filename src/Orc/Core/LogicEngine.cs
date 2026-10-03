namespace Orc.Core;

/// <summary>
/// 逻辑引擎（S3 形态）：持总线（一对一绑定、公开只读）＋总事件流（读访问）＋支撑顶层挂载（框架内部自动机制）。
/// 允许并主张多实例（每实例独立总线与总流）；无全局单例；链上执行恒随「调用时显式传入的引擎引用」。
/// engine.Emit 为 <see cref="Bus.Emit"/> 的便捷转发（完全一致、纯转发：含入流目标流判定）。
/// </summary>
public sealed class LogicEngine
{
    public LogicEngine()
    {
        RootStream = new EventStream();
        Bus = new Bus(this);
    }

    /// <summary>总事件流（树根；顶层执行流自动挂载于其下，引擎域一切因果记录最终可达）。</summary>
    public EventStream RootStream { get; }

    /// <summary>总线（S3；与所属引擎一对一绑定；公开只读、不可替换；多实例互不相通）。</summary>
    public Bus Bus { get; }

    /// <summary>更新广播便捷转发（与 <see cref="Bus.Emit"/> 行为完全一致）。</summary>
    public Task Emit(
        string updateType, IReadOnlyDictionary<string, object?>? payload = null, CancellationToken ct = default)
        => Bus.Emit(updateType, payload, ct);
}
