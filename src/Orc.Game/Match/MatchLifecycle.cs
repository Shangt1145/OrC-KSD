using Orc.Game.Players;

namespace Orc.Game;

/// <summary>
/// 对局生命周期（后置项 B 加性；状态与胜者的单一真源）：准备 → 进行 → 结束（单向、不可逆）。
/// 终局（HQ 生命≤0 触发）：状态置"结束"＋胜者记录（使对方 HQ 归零的一方）；其后所有游戏动作入口经本对象判拒
/// （对外面拒绝、零副作用、状态不推进），只读查询面保持可用。终局不发射任何更新（不发额外通知）。
/// 独立构造（脱离对局）＝管理器生命周期参数缺省 null——行为与既有装配一致（无门禁来源）。
/// </summary>
public sealed class MatchLifecycle
{
    /// <summary>当前状态（初始＝准备；MarkInProgress 后＝进行；End 后＝结束）。</summary>
    public MatchState State { get; private set; } = MatchState.Preparing;

    /// <summary>胜者（仅终局后非 null：＝使对方 HQ 归零的一方；非终局＝null；平局不处理——本批无同时归零路径）。</summary>
    public Player? Winner { get; private set; }

    /// <summary>是否已终局（动作入口门禁的读取面）。</summary>
    public bool IsEnded => State == MatchState.Ended;

    /// <summary>置"进行"态（对局 Initialize 完成后调用）。</summary>
    internal void MarkInProgress() => State = MatchState.InProgress;

    /// <summary>
    /// 置终局（记录胜者；仅"进行"态生效——重复/非进行态调用＝幂等忽略，防御）。
    /// 调用时点＝HQ 伤害结算点（当刻置结束＋胜者）；当次结算收尾照常完成（内部步骤不经门禁）。
    /// </summary>
    /// <param name="winner">胜者（使对方 HQ 归零的一方）。</param>
    internal void End(Player winner)
    {
        ArgumentNullException.ThrowIfNull(winner);
        if (State != MatchState.InProgress)
        {
            return; // 幂等：已终局/未进行＝忽略（防御）
        }

        Winner = winner;
        State = MatchState.Ended;
    }
}
