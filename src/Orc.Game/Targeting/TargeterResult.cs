namespace Orc.Game.Targeting;

/// <summary>targeter 级结局三态。</summary>
public enum TargeterStatus
{
    /// <summary>流程成功完成（无业务载荷；产出已由 targeter 内部消费/回写）。</summary>
    Ok,

    /// <summary>流程被取消（某选择器取消并传播）。</summary>
    Cancelled,

    /// <summary>流程失败（系统原因）。</summary>
    Failed,
}

/// <summary>targeter 失败原因（封闭枚举；与现役 <c>TargetingEndReason</c> 可映射）。</summary>
public enum TargeterFailureReason
{
    /// <summary>非法选择（选择器校验不通过且未重试或重试超限）。</summary>
    InvalidSelection,

    /// <summary>重试超过上限。</summary>
    RetryLimitExceeded,

    /// <summary>无可用候选（组装方给出的候选集为空）。</summary>
    NoAvailableCandidates,

    /// <summary>未装配桥接。</summary>
    BridgeNotAssembled,

    /// <summary>对局已结束（终局门禁）。</summary>
    GameEnded,

    /// <summary>系统异常（选择器/流程/桥接异常）。</summary>
    Fault,

    /// <summary>其他/未知（兜底）。</summary>
    Other,
}

/// <summary>
/// targeter 结果（三段式；不抛）。使用者据 <see cref="Status"/> 决定是否撤销本次放置/打出。
/// 产出不在本对象——各步骤产出已由 targeter 内部消费/回写到效果上下文。
/// </summary>
public sealed class TargeterResult
{
    private TargeterResult(TargeterStatus status, TargeterFailureReason? reason, string? detail)
    {
        Status = status;
        Reason = reason;
        Detail = detail;
    }

    /// <summary>结局三态。</summary>
    public TargeterStatus Status { get; }

    /// <summary>失败原因（仅 <see cref="TargeterStatus.Failed"/> 时非 null）。</summary>
    public TargeterFailureReason? Reason { get; }

    /// <summary>诊断细节（可选）。</summary>
    public string? Detail { get; }

    /// <summary>是否成功。</summary>
    public bool IsOk => Status == TargeterStatus.Ok;

    /// <summary>是否取消。</summary>
    public bool IsCancelled => Status == TargeterStatus.Cancelled;

    /// <summary>是否失败。</summary>
    public bool IsFailed => Status == TargeterStatus.Failed;

    /// <summary>创建成功结果。</summary>
    public static TargeterResult Ok() => new(TargeterStatus.Ok, null, null);

    /// <summary>创建取消结果。</summary>
    public static TargeterResult Cancelled() => new(TargeterStatus.Cancelled, null, null);

    /// <summary>创建失败结果。</summary>
    public static TargeterResult Failed(TargeterFailureReason reason, string? detail = null)
        => new(TargeterStatus.Failed, reason, detail);

    /// <summary>由选择器失败原因映射为 targeter 失败结果（便捷）。</summary>
    public static TargeterResult FromSelectorFailure(SelectorFailureReason? reason)
        => reason switch
        {
            SelectorFailureReason.InvalidSelection => Failed(TargeterFailureReason.InvalidSelection),
            SelectorFailureReason.RetryLimitExceeded => Failed(TargeterFailureReason.RetryLimitExceeded),
            _ => Failed(TargeterFailureReason.Fault),
        };
}
