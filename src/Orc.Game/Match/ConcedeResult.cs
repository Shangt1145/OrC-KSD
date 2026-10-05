namespace Orc.Game;

/// <summary>认输结局状态（两态：接受／拒绝）。</summary>
public enum ConcedeStatus
{
    /// <summary>接受（已置终局：对手为胜者、相位置"结束"）。</summary>
    Accepted,

    /// <summary>拒绝（零副作用、状态不推进）。</summary>
    Rejected,
}

/// <summary>认输拒绝原因（类别化；接受＝null）。</summary>
public enum ConcedeFailureReason
{
    /// <summary>对局已结束（终局后认输被拒绝）。</summary>
    GameEnded,

    /// <summary>非"进行"相位（准备／换牌相位认输被拒绝）。</summary>
    PhaseBlocked,
}

/// <summary>
/// 认输结果（不抛；消费方读 <see cref="Status"/> ＋ <see cref="FailureReason"/> 分流）。
/// </summary>
/// <param name="Status">结局状态。</param>
/// <param name="FailureReason">拒绝原因（仅拒绝时非 null）。</param>
public sealed record ConcedeResult(ConcedeStatus Status, ConcedeFailureReason? FailureReason)
{
    /// <summary>是否已被接受。</summary>
    public bool IsAccepted => Status == ConcedeStatus.Accepted;

    internal static ConcedeResult Accepted() => new(ConcedeStatus.Accepted, null);

    internal static ConcedeResult Rejected(ConcedeFailureReason reason) => new(ConcedeStatus.Rejected, reason);
}
