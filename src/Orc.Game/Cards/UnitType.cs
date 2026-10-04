namespace Orc.Game.Cards;

/// <summary>
/// 单位类型枚举（KARDS 模仿；E6 定）：Infantry / Tank / Artillery / Fighter / Bomber。
/// 以列表承载（<see cref="UnitStateData.UnitTypes"/>）：允许 0 个 / 多个（不设非空校验）；扩展留。
/// 普通枚举（非 [Flags]）——组合语义由列表承载。
/// </summary>
public enum UnitType
{
    /// <summary>步兵。</summary>
    Infantry = 0,

    /// <summary>坦克。</summary>
    Tank = 1,

    /// <summary>炮兵。</summary>
    Artillery = 2,

    /// <summary>战斗机。</summary>
    Fighter = 3,

    /// <summary>轰炸机。</summary>
    Bomber = 4,
}
