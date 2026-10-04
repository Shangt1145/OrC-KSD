namespace Orc.Game.Cards;

/// <summary>
/// 卡牌类别（三大类卡牌体系在定义层的区分）：单位 / 指令 / 反制。
/// 定义层经 <see cref="CardDefinition.Category"/> 承载；实例化经 <see cref="CardLibrary.Instantiate"/>
/// 产出对应基类实例（<see cref="UnitCard"/> / <see cref="CommandCard"/> / <see cref="CounterCard"/>）。
/// 未定义的类别值在定义期（构造）被拒绝（配置错误不吞）。
/// </summary>
public enum CardCategory
{
    /// <summary>单位：部署入战场、参与战斗（打出链属 2B）。</summary>
    Unit = 0,

    /// <summary>指令：打出即生效的主动效果载体（打出链属 2B）。</summary>
    Command = 1,

    /// <summary>反制：激活/取消两态、使用反制的触发器（流程属 2B/2C）。</summary>
    Counter = 2,
}
