using Orc.Game.Collections;

namespace Orc.Game.Players;

/// <summary>
/// 玩家（对局数据类；本批不继承引擎 Entity——无引用/生命周期需求，实体化留后续批次评估）：
/// 资源（指挥点数 / 指挥点槽）＋ 卡组（名单）＋ 手牌（实例集）＋ HQ（血量数据）。
/// 读面直接可读（供测试断言与展示）；写面经管理器（资源结算＝资源管理器；卡组消耗/手牌装载＝玩家管理器），无旁路修改入口。
/// HQ＝纯数据（初始 20 血、可读、本批无修改入口、不落位——"占支援线一格"为设计预留）；归零判定与胜负逻辑后置。
/// </summary>
public sealed class Player
{
    /// <summary>HQ 初始血量（20）。</summary>
    public const int InitialHqHealth = 20;

    /// <summary>手牌上限（9；本批仅数据、可读、不触发超限裁决——弃置后置）。</summary>
    public const int HandLimit = 9;

    internal Player(int index, CardList deck)
    {
        Index = index;
        Deck = deck;
    }

    /// <summary>玩家索引（0＝玩家A/第一位玩家、1＝玩家B）。</summary>
    public int Index { get; }

    /// <summary>卡组（名单：剩余可抽的卡牌 id 序列；抽牌＝取首张并移除）。</summary>
    public CardList Deck { get; }

    /// <summary>手牌（卡牌实例集合）。</summary>
    public CardSet Hand { get; } = new();

    /// <summary>指挥点数（当前值；回合开始结算＝槽值，回合结束清零、不保留）。</summary>
    public int Points { get; internal set; }

    /// <summary>指挥点槽（回合开始 +1、至上限封顶；保留于回合结束）。</summary>
    public int PointSlots { get; internal set; }

    /// <summary>HQ 血量（初始 20；本批无修改入口）。</summary>
    public int HqHealth { get; } = InitialHqHealth;
}
