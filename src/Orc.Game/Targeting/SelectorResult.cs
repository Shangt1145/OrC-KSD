namespace Orc.Game.Targeting;

/// <summary>选择器结局三态（Rust 风格）：Ok / Cancelled / Failed。</summary>
public enum SelectorStatus
{
    /// <summary>成功（携带强类型产出）。</summary>
    Ok,

    /// <summary>用户取消（点取消按钮 / 拖拽离开屏幕无目标）。</summary>
    Cancelled,

    /// <summary>失败（非法选择 / 重试超限 / 系统异常）。</summary>
    Failed,
}

/// <summary>选择器失败原因（封闭枚举；当前含 <see cref="InvalidSelection"/>、<see cref="RetryLimitExceeded"/>，预留 <see cref="Fault"/>）。</summary>
public enum SelectorFailureReason
{
    /// <summary>非法选择（不在候选集 / 数量不符 / 已失效 / 未过域判定）。</summary>
    InvalidSelection,

    /// <summary>重试超过上限。</summary>
    RetryLimitExceeded,

    /// <summary>系统异常（预留）。</summary>
    Fault,
}

/// <summary>
/// 选择器结果（三段式；<b>取消为一等状态</b>，与 targeter 级结果同构）。
/// 产出仅 <see cref="Ok"/> 时非空；只读快照。
/// </summary>
public sealed class SelectorResult<TResult>
{
    private SelectorResult(SelectorStatus status, TResult? value, SelectorFailureReason? failure)
    {
        Status = status;
        Value = value;
        Failure = failure;
    }

    /// <summary>结局三态。</summary>
    public SelectorStatus Status { get; }

    /// <summary>产出（仅 <see cref="SelectorStatus.Ok"/> 时非 null）。</summary>
    public TResult? Value { get; }

    /// <summary>失败原因（仅 <see cref="SelectorStatus.Failed"/> 时非 null）。</summary>
    public SelectorFailureReason? Failure { get; }

    /// <summary>是否成功。</summary>
    public bool IsOk => Status == SelectorStatus.Ok;

    /// <summary>是否取消。</summary>
    public bool IsCancelled => Status == SelectorStatus.Cancelled;

    /// <summary>是否失败。</summary>
    public bool IsFailed => Status == SelectorStatus.Failed;

    /// <summary>创建成功结果。</summary>
    public static SelectorResult<TResult> Ok(TResult value) => new(SelectorStatus.Ok, value, null);

    /// <summary>创建取消结果。</summary>
    public static SelectorResult<TResult> Cancelled() => new(SelectorStatus.Cancelled, default, null);

    /// <summary>创建失败结果。</summary>
    public static SelectorResult<TResult> Failed(SelectorFailureReason reason)
        => new(SelectorStatus.Failed, default, reason);
}
