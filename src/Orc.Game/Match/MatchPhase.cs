namespace Orc.Game;

/// <summary>
/// 对局相位（R1 公开读面 `MatchPhase{Mulligan,Play,Ended}`）——**派生投影**：
/// 单一真源＝<see cref="MatchState"/>（本枚举不独立存储——避免双真源）。
/// 映射：<see cref="MatchState.Mulligan"/>→<see cref="Mulligan"/>；<see cref="MatchState.InProgress"/>→<see cref="Play"/>；
/// <see cref="MatchState.Ended"/>→<see cref="Ended"/>；<see cref="MatchState.Preparing"/>→未就绪（读取抛错）。
/// </summary>
public enum MatchPhase
{
    /// <summary>换牌（mulligan）：开局换牌阶段（动作入口一律拒绝）。</summary>
    Mulligan = 0,

    /// <summary>进行（对局主相位：动作入口开放）。</summary>
    Play = 1,

    /// <summary>结束（终局：动作入口拒绝、只读查询面可用）。</summary>
    Ended = 2,
}
