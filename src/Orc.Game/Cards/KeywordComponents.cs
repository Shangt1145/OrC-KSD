using Orc.Cards;
using Orc.Core;
using Orc.Game.Commanding;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 生产内建四词条的组件实现（2C-A1 迁移；行为与迁移前逐条保真——见验收汇报「随改清单」）：
// 闪击/伏击/奋战＝能力型；烟幕＝标记型（轻量组件 PlainKeywordComponent——
// 仅数据、无主动逻辑，消费方经词条管理组件查询）。四者均走同一组件基类与同一挂载/卸载机制
// （闪击/奋战＝运行逻辑组件的自含行为；伏击经批 2 效果化收薄为壳——见下）。
// 批 2 词条效果化（B 档扩展）：伏击（伤害改写族）行为迁效果承载（组件收薄为壳——标识／授予-移除／读取面；
// 「造成攻击伤害」改写注册/撤销与判定链由内嵌效果 AmbushRewriteEffect 承载）；效果制品与装配
// （构造期 EmbedEffect＋装载/卸载/死亡注销/回滚/复装随词条生灭）见 KeywordEffects.cs。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 闪击（能力型）词条组件：部署链收尾（扣费后）执行——允许单位可移动和攻击（覆盖部署初值 false/false）。
/// 「加载时完成预备、运行时于部署链收尾执行」；仅部署路径生效（加入路径不置位——加入链不走部署收尾）。
/// </summary>
public sealed class BlitzKeywordComponent : KeywordComponent
{
    /// <summary>创建闪击词条组件。</summary>
    public BlitzKeywordComponent()
        : base(KeywordIds.Blitz)
    {
    }

    /// <inheritdoc />
    internal override Task OnDeployChainFinalizedAsync(Card card, CancellationToken ct)
    {
        if (card.TryGetData<CommandData>(out var command))
        {
            command.CanMove = true;
            command.CanAttack = true;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 奋战（能力型）词条组件：保留运行逻辑承载（攻击记账 / 收尾判定 / 回合清零——非纯标记、不退化为「标记型＋外部读取」）。
/// 自含记账数据（「本轮已攻次数」——不保留通用字符串键计数器形态）：首次攻击后仍可攻、二次后不可；
/// 随回合恢复一并清零。读取方（攻击执行段/收尾/回合恢复）经 <see cref="KeywordRules"/> 静态助手面调用（转发本组件）。
/// </summary>
public sealed class FuryKeywordComponent : KeywordComponent
{
    private int _attackCount;

    /// <summary>创建奋战词条组件。</summary>
    public FuryKeywordComponent()
        : base(KeywordIds.Fury)
    {
    }

    /// <summary>攻击记账（攻击动作成功执行后、由攻击执行段调用）：「本轮已攻次数」+1。</summary>
    internal void RecordAttack() => _attackCount++;

    /// <summary>攻击动作后的 CanAttack 收尾取值：「本轮已攻次数」＜2（首次攻击后保持可攻、二次后不可）。</summary>
    internal bool CanAttackAfterAttack() => _attackCount < 2;

    /// <summary>回合恢复：清零「本轮已攻次数」（随回合恢复一并清零）。</summary>
    internal void ResetTurnCounters() => _attackCount = 0;
}

/// <summary>
/// 伏击（能力型）词条组件（批 2 效果化：收薄为壳——标识／授予-移除／读取面）：
/// 「造成攻击伤害」改写逻辑（先资格、后条件的判定链与置改写标志）由内嵌效果
/// <see cref="AmbushRewriteEffect"/> 承载（注册/撤销随效果生命周期；判定器通道经同一装载上下文取用；
/// 无装载上下文或通道缺失＝不注册——防御、不抛错、不回退直调；单源约束保持）。不区分攻击者类型；HQ 攻击不走该流程。
/// </summary>
public sealed class AmbushKeywordComponent : KeywordComponent
{
    /// <summary>创建伏击词条组件。</summary>
    public AmbushKeywordComponent()
        : base(KeywordIds.Ambush)
    {
        EmbedEffect(new AmbushRewriteEffect()); // 批 2：伤害改写行为（注册/撤销与判定链）迁效果承载
    }
}
