using Orc.Core;

namespace Orc.Game.Judicators;

/// <summary>
/// 内置默认验证判定器解析（J2；独立构造路径的「绑定解析内置可用」承载）：
/// 默认名（费用/反制——零装配依赖项）→ 内置默认判定器实例（构造即可用、零配置——不依赖对局注册表/装配段）；
/// 未收录名（自定义名）→ 未注册 fail-fast（独立构造路径无注册表——自定义名经对局路径注册表注册与解析）。
/// 复验（移动/攻击）因需注入对局级只读设施、由 <see cref="Orc.Game.Commanding.CommandManager"/> 创建位
/// 承载内置解析（独立构造路径）——对局路径统一经注册表（固定内置注册段）。
/// 注：「默认名恒可解析、自定义名未注册＝装配期 fail-fast」的解析校验语义由本解析（独立路径）
/// 与注册表解析（对局路径）共同承载。
/// </summary>
internal static class BuiltInValidationJudicators
{
    /// <summary>
    /// 解析内置默认验证判定器（独立构造路径）：默认名 → 内置默认实例；
    /// 未收录名 → <see cref="KeyNotFoundException"/>（未注册引用＝配置错误——fail-fast、不设宽容旁路）。
    /// </summary>
    /// <exception cref="KeyNotFoundException">未注册引用（内置默认表不含该名）。</exception>
    public static JudicatorBinding Resolve(string name) => name switch
    {
        JudicatorNames.CostCheck => JudicatorBinding.FromStandalone(name, new CostCheckJudicator()),
        JudicatorNames.CounterUse => JudicatorBinding.FromStandalone(name, new CounterUseJudicator()),
        _ => throw new KeyNotFoundException(
            $"判定器 '{name}' 未注册（独立构造路径无注册表——内置默认仅含费用/反制；未注册引用＝配置错误）。"),
    };
}
