using Orc.Game.Collections;
using Orc.Game.Players;

namespace Orc.Game.Board;

/// <summary>
/// 战场（KARDS 模仿）：三条战线——玩家A 支援线 / 前线（中立共享）/ 玩家B 支援线，各为卡牌实例集合（<see cref="CardSet"/>）。
/// 归属固定（对象稳定、不随回合滚动；"我方/敌方"为调用方视图概念，战场不做滚动）；前线不属于任何一方。
/// 容量配置字段 4/5/4 各线可读；校验逻辑后置（本批不做）。
/// 初始化后三线均为空集合（不含 HQ、不含任何实体——HQ 本批为玩家纯数据、不落位）。
/// </summary>
public sealed class Battlefield
{
    /// <summary>玩家A 的支援线（固定归属）。</summary>
    public CardSet PlayerASupportLine { get; } = new();

    /// <summary>前线（中立共享）。</summary>
    public CardSet FrontLine { get; } = new();

    /// <summary>玩家B 的支援线（固定归属）。</summary>
    public CardSet PlayerBSupportLine { get; } = new();

    /// <summary>玩家A 支援线容量配置（4；可读，校验后置）。</summary>
    public int PlayerASupportLineCapacity => 4;

    /// <summary>前线容量配置（5；可读，校验后置）。</summary>
    public int FrontLineCapacity => 5;

    /// <summary>玩家B 支援线容量配置（4；可读，校验后置）。</summary>
    public int PlayerBSupportLineCapacity => 4;

    /// <summary>按玩家索引取支援线（0＝玩家A 线、1＝玩家B 线）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">playerIndex 非 0/1。</exception>
    public CardSet GetSupportLine(int playerIndex) => playerIndex switch
    {
        0 => PlayerASupportLine,
        1 => PlayerBSupportLine,
        _ => throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex, "玩家索引须为 0（玩家A）或 1（玩家B）。"),
    };

    /// <summary>按玩家取支援线（便捷转发到索引版；形式＝按玩家查询能力）。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public CardSet GetSupportLine(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return GetSupportLine(player.Index);
    }
}
