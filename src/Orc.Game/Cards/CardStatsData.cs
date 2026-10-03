namespace Orc.Game.Cards;

/// <summary>
/// 卡牌基础数据组件（KARDS 模仿）：部署费 / 行动费 / 攻击力 / 防御力 四整数——单组件承载（"基础数据"为原子组合、不拆分）。
/// 以引擎数据组件形态挂载：经 <see cref="Orc.Cards.Card.AddData"/> 装配、<see cref="Orc.Cards.Card.GetData{T}"/> 读取（引用共享）。
/// 本批仅承载字段（纯数据、无行为方法）；数值域校验后置（负数等，规则批次）。
/// </summary>
public sealed class CardStatsData
{
    /// <summary>部署费（花费）。</summary>
    public int DeployCost { get; set; }

    /// <summary>行动费（行动点数）。</summary>
    public int OperateCost { get; set; }

    /// <summary>攻击力。</summary>
    public int Attack { get; set; }

    /// <summary>防御力（游戏语义＝HP）。</summary>
    public int Defense { get; set; }
}
