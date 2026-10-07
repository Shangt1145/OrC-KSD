namespace Orc.Game.Targeting;

/// <summary>
/// 流程宿主面（组装方在组装 targeter 时使用）：
/// <see cref="Step{TResult}"/> 产出一个选择器并等待用户应答；<see cref="Retry{TResult}"/> 重入当前选择器。
/// 组装方据此编写分支（继续 / 中止）——"代码即流程"（I3＝c）。
/// </summary>
public interface ITargeterFlow
{
    /// <summary>
    /// 产出选择器并等待应答（前端经会话取到并提交语义事件）。
    /// </summary>
    /// <typeparam name="TResult">选择器产出类型（由选择器定义就地声明）。</typeparam>
    /// <param name="selector">选择器定义（模板；进程内共用）。</param>
    /// <param name="parameter">参数（候选/数量/域判定/呈现参数）。</param>
    /// <returns>选择器结果（Ok / Cancelled / Failed）。</returns>
    Task<SelectorResult<TResult>> Step<TResult>(Selector<TResult> selector, SelectorParameter parameter);

    /// <summary>
    /// 重入当前选择器（同一实例、同一标识；前端再次取到并重新选择）。
    /// 超过重试上限＝返回 <see cref="SelectorFailureReason.RetryLimitExceeded"/>。
    /// </summary>
    /// <typeparam name="TResult">当前选择器的产出类型。</typeparam>
    Task<SelectorResult<TResult>> Retry<TResult>();

    /// <summary>当前选择器已重试次数。</summary>
    int RetryCount { get; }
}
