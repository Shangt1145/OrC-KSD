namespace Orc.Game;

/// <summary>对局状态（本批两态：准备 → 进行；"结束"态随胜负批次引入）。</summary>
public enum MatchState
{
    /// <summary>准备：已创建、未初始化（数据已装配；无管理器、无玩家、无更新）。</summary>
    Preparing = 0,

    /// <summary>进行：初始化完成、回合可推进。</summary>
    InProgress = 1,
}
