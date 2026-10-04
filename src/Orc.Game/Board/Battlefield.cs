using Orc.Game.Players;

namespace Orc.Game.Board;

/// <summary>
/// 战场（KARDS 模仿）：三条战线——玩家A 支援线 / 前线（中立共享）/ 玩家B 支援线，各为固定容量槽位序列
/// （<see cref="BattleLine"/>；容量 4/5/4）。
/// 归属固定（对象稳定、不随回合滚动；"我方/敌方"为调用方视图概念，战场不做滚动）；前线不属于任何一方。
/// HQ 初始占位（2A 槽位化受控变更）：各自支援线固定槽位（索引 0）＝对应玩家引用（与「HQ＝玩家纯数据」一致、无独立 HQ 实体）；
/// HQ 占用容量格——支援线各含 HQ 占位（槽 0）＋其余初始为空；前线初始全空。
/// 〔旧语义反转说明：原注释「初始化后三线均为空集合（不含 HQ、HQ 不落位）」已随 2A 槽位模型失效——
///   现为「战场构造期即含 HQ 占位」。〕
/// 容量配置 4/5/4 经各线容量属性可读；校验逻辑后置（本批不做）。
/// </summary>
public sealed class Battlefield
{
    /// <summary>创建战场（构造期即含 HQ 占位：玩家A 支援线槽 0＝playerA；玩家B 支援线槽 0＝playerB）。</summary>
    /// <exception cref="ArgumentNullException">playerA 或 playerB 为 null。</exception>
    public Battlefield(Player playerA, Player playerB)
    {
        ArgumentNullException.ThrowIfNull(playerA);
        ArgumentNullException.ThrowIfNull(playerB);

        PlayerASupportLine = new BattleLine(4, "玩家A支援线");
        FrontLine = new BattleLine(5, "前线");
        PlayerBSupportLine = new BattleLine(4, "玩家B支援线");

        PlayerASupportLine[0].Place(playerA);
        PlayerBSupportLine[0].Place(playerB);
    }

    /// <summary>玩家A 的支援线（固定归属）。</summary>
    public BattleLine PlayerASupportLine { get; }

    /// <summary>前线（中立共享）。</summary>
    public BattleLine FrontLine { get; }

    /// <summary>玩家B 的支援线（固定归属）。</summary>
    public BattleLine PlayerBSupportLine { get; }

    /// <summary>玩家A 支援线容量配置（4；可读，校验后置）。</summary>
    public int PlayerASupportLineCapacity => PlayerASupportLine.Capacity;

    /// <summary>前线容量配置（5；可读，校验后置）。</summary>
    public int FrontLineCapacity => FrontLine.Capacity;

    /// <summary>玩家B 支援线容量配置（4；可读，校验后置）。</summary>
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
