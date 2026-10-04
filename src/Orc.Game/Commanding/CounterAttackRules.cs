using Orc.Game.Cards;

namespace Orc.Game.Commanding;

/// <summary>
/// 反击豁免判定表（后置项 A；单位 vs 单位交战的目标方反击资格判定）：
/// 按 <see cref="UnitStateData.UnitTypes"/> 逐条套用、条款冲突时以「豁免（不发生反击）」优先
/// （仅当全部相关条款均允许反击时才有反击机会——进入伏击资格判定）：
/// ① 目标＝轰炸机 → 永不反击；② 攻击者＝炮兵 → 不受任何反击；③ 攻击者＝轰炸机 → 不受反击、例外＝目标战斗机可反击；④ 其余 → 正常反击。
/// 攻击 HQ 不属交战（无反击）——HQ 简路不经过本判定（不属本表范围）。
/// 与伏击改写的组合（仲裁原则：豁免约束改写——先资格、后条件）：无资格＝不发生反击、伏击改写不成立、按表单方结算；
/// 有资格＝检查伏击条件（命中＝改写；不命中＝按表正常同时互伤）。
/// </summary>
public static class CounterAttackRules
{
    /// <summary>
    /// 目标方是否对攻击者发生反击（豁免表判定；多类型＝存在性判定——任一相关条款适用即适用）。
    /// </summary>
    /// <param name="attacker">攻击者（含类型清单——存在性判定）。</param>
    /// <param name="target">被攻击单位（含类型清单——存在性判定）。</param>
    /// <exception cref="ArgumentNullException">attacker / target 为 null。</exception>
    public static bool CanCounterAttack(UnitCard attacker, UnitCard target)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        // ① 目标＝轰炸机 → 永不反击（绝对豁免、最高优先）
        if (HasUnitType(target, UnitType.Bomber))
        {
            return false;
        }

        // ② 攻击者＝炮兵 → 不受任何反击（绝对豁免——对轰炸机条款的例外亦优先）
        if (HasUnitType(attacker, UnitType.Artillery))
        {
            return false;
        }

        // ③ 攻击者＝轰炸机 → 不受反击；例外：目标＝战斗机可反击
        if (HasUnitType(attacker, UnitType.Bomber))
        {
            return HasUnitType(target, UnitType.Fighter);
        }

        // ④ 其余 → 正常反击
        return true;
    }

    private static bool HasUnitType(UnitCard unit, UnitType type)
        => unit.TryGetData<UnitStateData>(out var state) && state.UnitTypes.Contains(type);
}
