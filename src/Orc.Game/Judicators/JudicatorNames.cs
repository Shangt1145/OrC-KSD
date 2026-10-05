namespace Orc.Game.Judicators;

/// <summary>
/// 判定器名常量集（受控常量集合；J1 机制骨架）：注册源与消费引用的唯一称谓来源（防散落字面——沿用 KeywordIds 先例）。
/// 命名规范（J1 定型；J2/J3 引用基准）：英文点分式、「域.用途」两级起（必要时多级）、全小写、ordinal 逐字敏感；
/// 判定器名系代码级引用标识（机制性标识体系——与信号名 card.played 等同族），非卡面词汇域（KeywordIds 中文词属卡面词汇域）。
/// 实验性成分不在字面标注（字面保持干净）：以常量注释＋实现记录申报承载。
/// 冻结性：名称一经 J3 引用视为冻结契约（更名需申报迁移说明）。
/// 机制骨架阶段暂无条目——具体名字随各单（J2/J3/后续）提报加入。
/// </summary>
public static class JudicatorNames
{
    // ---------- J2（合法性验证替换）：默认验证判定器（四条；内置注册段固定注册——无条件可用） ----------

    /// <summary>费用检查（费用验证点专属；预打出/打出复用同一判定器——moding 改写即其全部引用点生效）。</summary>
    public const string CostCheck = "validation.cost.check";

    /// <summary>反制使用检查（反制使用验证点专属；触发器验证与入口预检同一判定源——改写两侧同步生效）。</summary>
    public const string CounterUse = "validation.counter.use";

    /// <summary>移动复验（单位移动验证点专属；执行前兜底复验）。</summary>
    public const string MoveRecheck = "validation.move.recheck";

    /// <summary>攻击复验（单位攻击验证点专属；执行前兜底复验——与移动复验相互独立改写）。</summary>
    public const string AttackRecheck = "validation.attack.recheck";

    // ---------- J3（两示范落地）：示范判定器（两条；注册经既有装配期注册面〔外部装配段〕——见 J3 实现记录） ----------

    /// <summary>卡组顶特点判定（示范①；签名＝（玩家，特点标识）→ bool）：默认按真实卡组顶求值（TagData 开放 tag）；
    /// 卡组空＝不满足（false 降级）；恒真/恒假改写经 moding（全局生效）、注销回退。</summary>
    public const string DeckTopTag = "deck.top.tag";

    /// <summary>目标候选合法性判定（示范②；签名＝（候选引用）→ bool）：默认规则＝在场单位可选；
    /// 改写用例＝允许/禁止某类目标可选（经 moding 整体更换选择规则）。</summary>
    public const string TargetCandidateEligibility = "targeting.candidate.eligibility";
}
