using Orc.Game.Players;

namespace Orc.Game.Board;

/// <summary>
/// 战场（KARDS 模仿）：三条战线——玩家A 支援线 / 前线（中立共享）/ 玩家B 支援线，各为固定容量槽位序列
/// （<see cref="BattleLine"/>；容量 5/5/5）。
/// 归属固定（对象稳定、不随回合滚动；"我方/敌方"为调用方视图概念，战场不做滚动）；前线不属于任何一方。
/// HQ 初始占位（2A 槽位化；W3-3 受控变更：占位者由「对应玩家引用」改为「玩家总部实体」<see cref="Players.Hq"/>——
/// 独立 HQ 实体占支援线固定槽位（<see cref="HqSlotIndex"/>＝2，居中），Player 持 HQ 引用；
/// 布局语义〔占容量格/邻位/守护/轰炸机拦截基准〕不变）：
/// 支援线各含 HQ 占位（槽 2）＋左右各 2 格初始为空；前线初始全空。
/// 〔W3-4 受控变更：原为「支援线槽 0＝HQ、容量 4」——总部贴在支援线最左端，单位只能落在它右侧，
///   于是"把单位摆在总部左边"在棋盘上无从表达（部署落点换算到最前仍是 HQ 右边第一格）。
///   现改为 HQ 居中：邻位候选自然包含 HQ 两侧，守护判定改为两侧任一邻位（见 CommandManager.MaintainHqGuard）。〕
/// 容量配置 5/5/5 经各线容量属性可读；校验逻辑后置（本批不做）。
/// </summary>
public sealed class Battlefield
{
    /// <summary>总部在支援线中的固定占位索引（居中：其左、其右各有 2 格可放单位）。</summary>
    public const int HqSlotIndex = 2;

    /// <summary>创建战场（构造期即含 HQ 占位：玩家A / 玩家B 支援线槽 <see cref="HqSlotIndex"/>＝各自总部实体）。</summary>
    /// <exception cref="ArgumentNullException">playerA 或 playerB 为 null。</exception>
    public Battlefield(Player playerA, Player playerB)
    {
        ArgumentNullException.ThrowIfNull(playerA);
        ArgumentNullException.ThrowIfNull(playerB);

        PlayerASupportLine = new BattleLine(5, "玩家A支援线");
        FrontLine = new BattleLine(5, "前线");
        PlayerBSupportLine = new BattleLine(5, "玩家B支援线");

        PlayerASupportLine[HqSlotIndex].Place(playerA.Hq);
        playerA.Hq.AttachToSlot(PlayerASupportLine[HqSlotIndex]); // W3-3：HQ 布局语义（占位槽引用）
        PlayerBSupportLine[HqSlotIndex].Place(playerB.Hq);
        playerB.Hq.AttachToSlot(PlayerBSupportLine[HqSlotIndex]);
    }

    /// <summary>
    /// 支援线中总部占位所在槽位索引；未找到＝-1。HQ 的位置以棋盘为准查询，调用方不再假设它在某一格
    /// （守护基准、攻击候选的 HQ 目标都经本方法取位）。
    /// </summary>
    public static int IndexOfHq(BattleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        for (var i = 0; i < line.Count; i++)
        {
            if (line[i].Occupant is Players.Hq)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>玩家A 的支援线（固定归属）。</summary>
    public BattleLine PlayerASupportLine { get; }

    /// <summary>前线（中立共享）。</summary>
    public BattleLine FrontLine { get; }

    /// <summary>玩家B 的支援线（固定归属）。</summary>
    public BattleLine PlayerBSupportLine { get; }

    /// <summary>玩家A 支援线容量配置（5；可读，校验后置）。</summary>
    public int PlayerASupportLineCapacity => PlayerASupportLine.Capacity;

    /// <summary>前线容量配置（5；可读，校验后置）。</summary>
    public int FrontLineCapacity => FrontLine.Capacity;

    /// <summary>玩家B 支援线容量配置（5；可读，校验后置）。</summary>
    public int PlayerBSupportLineCapacity => PlayerBSupportLine.Capacity;

    /// <summary>按玩家索引取支援线（0＝玩家A 线、1＝玩家B 线）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">playerIndex 非 0/1。</exception>
    public BattleLine GetSupportLine(int playerIndex) => playerIndex switch
    {
        0 => PlayerASupportLine,
        1 => PlayerBSupportLine,
        _ => throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex, "玩家索引须为 0（玩家A）或 1（玩家B）。"),
    };

    /// <summary>按玩家取支援线（便捷转发到索引版；形式＝按玩家查询能力）。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public BattleLine GetSupportLine(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return GetSupportLine(player.Index);
    }
}
