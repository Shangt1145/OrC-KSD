namespace Orc.Game.Targeting;

/// <summary>
/// 前端桥接（后端↔前端交互通道；Manager 与桥接一对一——构造/装配注入）。
/// 交付<b>会话</b>：前端在会话上按队列逐个取选择器（<see cref="ITargeterSession.NextAsync"/>）、
/// 运行前端定义的视觉效果、经选择器实例提交语义事件。
/// 交付为同步方法（前端在自己的异步循环里拉取）；异常＝统一失败模式（后端捕获、失败结局、不抛、队列继续）。
/// </summary>
public interface ITargeterBridge
{
    /// <summary>
    /// 交付会话（前端据以逐个取选择器）。
    /// 实现不得阻塞（把会话挂到前端交互循环即可）；异常＝统一失败模式。
    /// </summary>
    /// <param name="session">会话（拉取选择器与读取终局）。</param>
    void BeginTargeting(ITargeterSession session);
}

/// <summary>
/// 会话（前端视角的一次 targeter 交互）：
/// 经 <see cref="NextAsync"/> 按队列逐个取选择器；取完（返回 <c>null</c>）后经 <see cref="Result"/> 读取终局。
/// </summary>
public interface ITargeterSession
{
    /// <summary>
    /// 取下一个选择器（按流程产出顺序）；返回 <c>null</c>＝流程结束（随后读 <see cref="Result"/>）。
    /// 异步：流程内部推进可能包含 await。
    /// </summary>
    Task<ISelectorInstance?> NextAsync();

    /// <summary>终局结果（流程结束后非 null；未结束＝null）。</summary>
    TargeterResult? Result { get; }
}
