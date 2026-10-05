namespace Orc.Game;

/// <summary>对局状态（三态：准备 → 进行 → 结束；"结束"＝后置项 B 胜负判定引入——HQ≤0 触发、不可逆）。</summary>
public enum MatchState
{
    /// <summary>准备：已创建、未初始化（数据已装配；无管理器、无玩家、无更新）。</summary>
    Preparing = 0,

    /// <summary>进行：初始化完成、回合可推进。</summary>
    InProgress = 1,

    /// <summary>结束：HQ≤0 立即终局（状态置结束＋胜者记录；其后所有游戏动作入口拒绝、只读查询面保持可用）。</summary>
    Ended = 2,

    /// <summary>
    /// 换牌（mulligan；A1 加性——追加于末尾以保持既有值不变）：Initialize 完成装配与起手装载后、
    /// 双方确认前的相位；动作入口一律拒绝（相位门禁统一经 <see cref="MatchLifecycle.IsActionAllowed"/> 判定）；
    /// 双方确认后置"进行"并执行先手第 1 回合。
    /// </summary>
    Mulligan = 3,
}
