namespace Orc.Core;

/// <summary>
/// 逻辑引擎（S2 最小壳）：仅持总事件流（读访问）＋支撑顶层挂载（框架内部自动机制，无公共挂载 API）。
/// 允许并主张多实例（每实例独立总流）；无全局单例；链上执行恒随「调用时显式传入的引擎引用」。
/// 总线 / Mount / Emit / UnmountOwner 等留后续阶段扩展（本阶段不预埋）。
/// </summary>
public sealed class LogicEngine
{
    public LogicEngine()
    {
        RootStream = new EventStream();
    }

    /// <summary>总事件流（树根；顶层执行流自动挂载于其下，引擎域一切因果记录最终可达）。</summary>
    public EventStream RootStream { get; }
}
