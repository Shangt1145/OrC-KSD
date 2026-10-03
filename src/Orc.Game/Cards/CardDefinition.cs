namespace Orc.Game.Cards;

/// <summary>
/// 卡牌定义（代码注册形态）：名称＋四项基础数值（部署费 / 行动费 / 攻击力 / 防御力）；
/// id 由注册键携带（定义自身不含 id，避免冗余与不一致）。
/// 名称不可为 null/空白（注册即配置、fail-fast）；数值域校验后置（负数等，本批不做）。
/// </summary>
public sealed class CardDefinition
{
    /// <summary>创建定义。</summary>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    public CardDefinition(string name, int deployCost, int operateCost, int attack, int defense)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        DeployCost = deployCost;
        OperateCost = operateCost;
        Attack = attack;
        Defense = defense;
    }

    /// <summary>卡牌名称（实例化时取用）。</summary>
    public string Name { get; }

    /// <summary>部署费初始值。</summary>
    public int DeployCost { get; }

    /// <summary>行动费初始值。</summary>
    public int OperateCost { get; }

    /// <summary>攻击力初始值。</summary>
    public int Attack { get; }

    /// <summary>防御力初始值（＝HP）。</summary>
    public int Defense { get; }
}

/// <summary>卡牌定义集条目：id（卡牌库注册键）＋定义。</summary>
public sealed record CardDefinitionEntry(string Id, CardDefinition Definition);
