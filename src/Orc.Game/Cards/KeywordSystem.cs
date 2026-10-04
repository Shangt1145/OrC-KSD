using Orc.Core;
using Orc.Game.Commanding;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 2C 词条系统（对战词条；A1 组件化体系）：
// 词条＝独立组件（KeywordComponent：自含数据〔含参值〕＋运行逻辑＋授予/移除回调＋可内嵌效果）；
// 卡上「词条管理组件」（CardBase.Keywords → KeywordManager）负责注册与索引（有无/参值查询）与
// 授予/移除/参值改写统一读写口；注册面（KeywordRegistry）为合法标识集与组件构造的唯一来源。
// 装载（加载时＝CardBase.LoadAsync 的词条装载步骤）：从定义声明逐条经管理组件授予链挂载
// （存在性置位 → 运行逻辑装载＋内嵌效果装载 → OnGrant）——先于 card.load，卡组/手牌即可被读取查询。
// 无词条卡＝无副作用（不注册词条组件、不装载逻辑；词条面可寻址、空内容）。
// 本文件承载：标识常量（KeywordIds）／词条装载上下文（KeywordLoadContext）／静态便利读取与奋战记账面（KeywordRules）。
// 四词条组件实现见 KeywordComponents.cs；注册面见 KeywordRegistry.cs；管理组件见 KeywordManager.cs。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 词条标识常量集（2C；需求指定固定字面值、精确匹配；对外读取契约）。
/// 本批生产内建四枚：<see cref="Blitz"/> / <see cref="Fury"/> / <see cref="SmokeScreen"/> / <see cref="Ambush"/>
/// （经 <see cref="KeywordRegistry"/> 注册面装配）；合法标识集＝注册面内容（含开放扩展注册的标识）。
/// 词条声明含未注册标识＝定义期明确错误（fail-fast；校验承载于 <see cref="CardDefinition"/>）。
/// </summary>
public static class KeywordIds
{
    /// <summary>闪击（能力型）：部署链收尾（扣费后）置单位可移动＋可攻击。</summary>
    public const string Blitz = "闪击";

    /// <summary>奋战（能力型）：攻击后记账——首次攻击后仍可攻、二次后不可（「本轮已攻次数」由词条组件自维护）。</summary>
    public const string Fury = "奋战";

    /// <summary>烟幕（标记型）：不可被攻击（对一切攻击者生效；攻击 targeting 中被筛除、不入候选）——轻量组件形态。</summary>
    public const string SmokeScreen = "烟幕";

    /// <summary>伏击（能力型）：「造成攻击伤害」内改写——被攻击单位实时攻击力＞攻击者实时防御力时，改为攻击者死亡、被攻击者不受伤。</summary>
    public const string Ambush = "伏击";

    /// <summary>生产内建词条标识清单（登记序；注册面静态装配依据与诊断用——合法集真源＝注册面内容）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Blitz, Fury, SmokeScreen, Ambush };

    /// <summary>标识是否已注册（存在性判定，转发注册面——单源；null/空白＝false）。</summary>
    public static bool IsDefined(string keyword) => KeywordRegistry.IsDefined(keyword);
}

/// <summary>
/// 词条装载上下文（2C；装载词条组件运行逻辑所需的对局级服务面）：
/// 经卡牌库注入（延迟读取）、<see cref="CardBase.LoadAsync"/> 的词条装载步骤与运行时授予按需取用；独立构造（脱离对局）＝null
/// （词条组件运行逻辑装载跳过注册、不抛错——功能不可用、加载不失败）。
/// </summary>
public sealed class KeywordLoadContext
{
    internal KeywordLoadContext(LogicEngine engine, Trigger<AttackDamageTriggerView> attackDamageTrigger)
    {
        Engine = engine;
        AttackDamageTrigger = attackDamageTrigger;
    }

    /// <summary>对局引擎（发射更新 / 触发子触发器所需）。</summary>
    public LogicEngine Engine { get; }

    /// <summary>「造成攻击伤害」共享流程触发器（伏击挂载点；引擎侧创建、与对局同生）。</summary>
    public Trigger<AttackDamageTriggerView> AttackDamageTrigger { get; }
}

/// <summary>
/// 词条规则助手（2C；静态便利口——卡上词条面 <see cref="CardBase.Keywords"/> 的转发面，调用形态保持）：
/// 读取（有无/参值）与奋战记账语义转发至词条组件体系（无第二套实现；无词条卡＝动态查询 false/null、不抛错）。
/// </summary>
public static class KeywordRules
{
    /// <summary>是否含指定词条（标识查询；无词条卡＝false、不抛错——转发卡上词条面）。</summary>
    public static bool HasKeyword(CardBase card, string keyword)
        => card.Keywords.Has(keyword);

    /// <summary>读取词条参值（加性扩展；无词条卡/无该词条/无参值＝null——与 <see cref="HasKeyword"/> 组合区分「有词条无参值」）。</summary>
    public static int? GetKeywordValue(CardBase card, string keyword)
        => card.Keywords.GetValue(keyword);

    /// <summary>
    /// 奋战记账（攻击动作成功执行后、由攻击执行段调用）：含奋战时「本轮已攻次数」+1；非奋战不记账。
    /// </summary>
    public static void RecordAttack(CardBase unit)
    {
        if (unit.Keywords.TryGetComponent<FuryKeywordComponent>(KeywordIds.Fury, out var fury))
        {
            fury.RecordAttack();
        }
    }

    /// <summary>
    /// 攻击动作后的 CanAttack 收尾取值（外层收尾读取记账）：非奋战＝false（统一清位）；
    /// 奋战＝「本轮已攻次数」＜2（首次攻击后保持可攻、二次后不可）。
    /// </summary>
    public static bool ResolveCanAttackAfterAttack(CardBase unit)
        => unit.Keywords.TryGetComponent<FuryKeywordComponent>(KeywordIds.Fury, out var fury)
            && fury.CanAttackAfterAttack();

    /// <summary>回合恢复：清零「本轮已攻次数」（随回合恢复一并清零；非奋战/无词条＝无操作）。</summary>
    public static void ResetTurnCounters(CardBase unit)
    {
        if (unit.Keywords.TryGetComponent<FuryKeywordComponent>(KeywordIds.Fury, out var fury))
        {
            fury.ResetTurnCounters();
        }
    }

    /// <summary>
    /// 部署链收尾驱动（扣费完成后、链返回前；仅部署路径调用）：遍历已挂载词条组件、逐组件执行收尾钩子
    /// （闪击＝置位两 bool；非闪击默认空操作）——闪击钩子的静态入口（消费点收口、细节在组件面）。
    /// </summary>
    public static async Task OnDeployChainFinalizedAsync(CardBase unit, CancellationToken ct)
    {
        foreach (var component in unit.Keywords.Components)
        {
            await component.OnDeployChainFinalizedAsync(unit, ct);
        }
    }

    /// <summary>
    /// 死亡注销驱动（统一死亡流程调用；转发至词条管理面——仅行为撤销：OnRevoke 序列＋运行逻辑注销＋内嵌效果卸载；
    /// 不执行存在性清除——登记/参值保留、照常可读）。重复调用＝无操作（幂等）。
    /// </summary>
    public static void RevokeAllOnDeath(CardBase unit) => unit.Keywords.RevokeOnDeath();
}
