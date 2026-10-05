namespace Orc.Game;

/// <summary>
/// 终局原因（B 加性；终局记录的一部分——与胜者并列记录，供外部区分终局来源）。
/// </summary>
public enum MatchEndReason
{
    /// <summary>HQ 归零（HQ 生命 ≤0 触发；胜者＝HQ 归零方之对手）。</summary>
    HqZero = 0,

    /// <summary>认输（玩家主动投降；胜者＝认输方之对手）。</summary>
    Concede = 1,
}
