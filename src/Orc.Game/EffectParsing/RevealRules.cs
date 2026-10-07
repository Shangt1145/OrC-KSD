using Orc.Cards;
using Orc.Game.Board;
using Orc.Game.Cards;

namespace Orc.Game.EffectParsing;

/// <summary>
/// 揭示动作的解析层编排支持（S3；csx 渲染的运行时挂钩）：提供 csx 可达的「卡 → 隐蔽候选」读口包装。
/// <para>存在理由（技术约束）：csx 沙箱为**文本级屏蔽**（<c>CSharpScriptSecurityOptions</c> 的
/// 「屏蔽片段」列表含 <c>Environment</c>，Ordinal 子串匹配）⇒ csx 中不得出现 <c>GameEnvironment</c> 字样，
/// 故「卡 → 环境 → <see cref="CovertRules.CollectCovertUnits"/>」链收敛于此面、csx 只引用本包装。</para>
/// <para>单一真源：候选仍由既有读口 <see cref="CovertRules.CollectCovertUnits"/> 产出（查询面、**不经豁免剔除**——
/// 「揭示 1 个隐蔽单位」类编排的候选来源）；本面不重建筛选逻辑、不触碰剔除。</para>
/// <para>降级：null 卡／无环境（脱局）＝空列表、不抛错（对齐既有服务注入先例）。</para>
/// </summary>
public static class RevealRules
{
    /// <summary>
    /// 「隐蔽单位」候选读口（对卡入口）：卡 → 环境 → <see cref="CovertRules.CollectCovertUnits"/>（转发）。
    /// 顺序＝战场槽位序（既有读口序）；不分敌我（归属筛选由使用方组合）。
    /// </summary>
    /// <exception cref="ArgumentNullException">无（null＝空列表降级）。</exception>
    public static IReadOnlyList<UnitCard> CollectCovertUnits(Card? viewer)
    {
        if (viewer is null || GameEnvironment.ResolveFor(viewer) is not { } environment)
        {
            return Array.Empty<UnitCard>();
        }

        return CovertRules.CollectCovertUnits(environment);
    }
}
