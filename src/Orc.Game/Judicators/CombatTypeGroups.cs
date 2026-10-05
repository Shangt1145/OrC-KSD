using Orc.Game.Cards;

namespace Orc.Game.Judicators;

/// <summary>
/// 交战类型组（K1；C2/C3/C4 共表——「类型组」概念的唯一单源定义/判定处；K2 加性扩展：服务交战判定族 A 档全部面）：
/// 任意线组＝炮兵/战斗机/轰炸机（任意线＝全组合允许，含同线、含 HQ）；
/// 资格组＝炮兵/轰炸机（被守护目标的攻击资格——不含战斗机）；
/// 拦截相关类型判定（轰炸机〔攻击者侧〕与战斗机〔目标/拦截者侧〕）同表；
/// 反击豁免表类型判定（轰炸机／战斗机／炮兵——C5 combat.counter.eligibility 消费）同表（K2）。
/// 组内容＝代码内单源定义（不新增配置文件面）；可改通道＝moding 改写判定器（C2/C3/C4/C5 条目）。
/// 语义保持（收编前现状）：存在性判定（含任一即适用）；单位无类型数据组件＝不含任何类型；
/// 多类型＝逐组独立成立（不叠加、不冲突）。
/// </summary>
internal static class CombatTypeGroups
{
    /// <summary>任意线组（炮/战/轰——C2 范围矩阵：任一命中＝任意线全组合允许）。</summary>
    private static readonly UnitType[] AnyLineRangeTypes = { UnitType.Artillery, UnitType.Fighter, UnitType.Bomber };

    /// <summary>资格组（炮/轰——C3 被守护攻击资格：任一命中＝有资格；不含战斗机）；任意线组＝资格组＋战斗机（子集关系同处定义——「共表」）。</summary>
    private static readonly UnitType[] GuardEligibilityTypes = { UnitType.Artillery, UnitType.Bomber };

    /// <summary>任意线组判定（存在性——含任一即适用）。</summary>
    public static bool HasAnyLineRange(UnitCard unit) => HasAnyType(unit, AnyLineRangeTypes);

    /// <summary>资格组判定（存在性——含任一即适用；不含战斗机）。</summary>
    public static bool CountsAsBombard(UnitCard unit) => HasAnyType(unit, GuardEligibilityTypes);

    /// <summary>轰炸机判定（拦截——攻击者侧；反击豁免表——目标/攻击者侧；K2 复用）。</summary>
    public static bool IsBomber(UnitCard unit) => HasType(unit, UnitType.Bomber);

    /// <summary>战斗机判定（拦截——目标与拦截者侧；反击豁免表——例外条款目标侧；K2 复用）。</summary>
    public static bool IsFighter(UnitCard unit) => HasType(unit, UnitType.Fighter);

    /// <summary>炮兵判定（反击豁免表——攻击者侧；K2 加性补充——消除原 CounterAttackRules 私有散落实现）。</summary>
    public static bool IsArtillery(UnitCard unit) => HasType(unit, UnitType.Artillery);

    private static bool HasAnyType(UnitCard unit, UnitType[] types)
    {
        if (!unit.TryGetData<UnitStateData>(out var state))
        {
            return false;
        }

        foreach (var type in types)
        {
            if (state.UnitTypes.Contains(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasType(UnitCard unit, UnitType type)
        => unit.TryGetData<UnitStateData>(out var state) && state.UnitTypes.Contains(type);
}
