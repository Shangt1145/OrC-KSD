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

    // ---------- K1（A 档 C1-C4：交战合法性判定族）：交战判定器（四条；内置注册段固定注册——无条件可用） ----------

    /// <summary>目标合法性判定（组合判定器；签名＝（攻击者, 目标引用）→ bool）：编排六重＝归属→存活/在场→烟幕→守护资格→拦截→范围
    /// （HQ 分支＝归属→占位槽→守护→拦截→范围）；候选链与复验链共用同一条目——改写后全部调用点同步生效。</summary>
    public const string CombatTargetLegal = "combat.target.legal";

    /// <summary>范围矩阵判定（签名＝（攻击者, 目标槽位）→ bool）：炮/战/轰任意线、步/坦/零类型仅相邻；类型组单源共表。</summary>
    public const string CombatRange = "combat.range";

    /// <summary>被守护攻击资格判定（签名＝（攻击者）→ bool）：炮/轰组——不含战斗机；类型组与 combat.range 共表。</summary>
    public const string CombatGuardEligibility = "combat.guard.eligibility";

    /// <summary>轰炸机拦截判定（签名＝（攻击者, 目标槽位, 目标是否战斗机）→ bool）：轰炸机∧非战斗机目标∧同线存活敌方战斗机＝拦截。</summary>
    public const string CombatInterception = "combat.interception";

    // ---------- K2（A 档 C5/C6：反击豁免与伏击条件）：反击/伏击判定器（两条；内置注册段固定注册——无条件可用） ----------

    /// <summary>反击资格判定（签名＝（攻击者, 目标）→ bool）：反击豁免表四条款、豁免优先（目标轰炸机永不反击／攻击者炮兵不受任何反击／
    /// 攻击者轰炸机不受反击〔例外＝目标战斗机〕／其余正常）；默认互伤区与伏击资格共用同一条目——改写后全部调用点同步生效。</summary>
    public const string CombatCounterEligibility = "combat.counter.eligibility";

    /// <summary>伏击条件判定（签名＝（被攻击单位, 攻击者）→ bool）：被攻击单位攻击有效值 ＞ 攻击者防御有效值（严格大于、相等＝不命中）。</summary>
    public const string CombatAmbushCondition = "combat.ambush.condition";

    // ---------- K3（A 档 C7/C8：复验消重）：leg 资格与推进前置判定器（三条；内置注册段固定注册——无条件可用） ----------

    /// <summary>move leg 资格判定（签名＝（单位, 源位置）→ LegEligibilityFailure?〔null＝通过〕）：共享『leg 资格』条件序列
    /// （owner==current／!destroyed／CanMove／被压制／行动费／位置〔源∈支援线〕）；可用性聚合与移动复验共用同一条目——
    /// 改写后两调用点同步生效（分区条目：不影响 attack 侧）。</summary>
    public const string MoveLegEligibility = "move.leg.eligibility";

    /// <summary>attack leg 资格判定（签名＝（单位, 位置〔忽略〕）→ LegEligibilityFailure?〔null＝通过〕）：共享『leg 资格』条件序列
    /// （owner==current／!destroyed／CanAttack／被压制／行动费）；可用性聚合与攻击复验共用同一条目——
    /// 改写后两调用点同步生效（分区条目：不影响 move 侧）。</summary>
    public const string AttackLegEligibility = "attack.leg.eligibility";

    /// <summary>推进前置判定（签名＝（所有者）→ bool）：前线是否存在存活敌方单位（空前线或己方已占＝false）；
    /// 移动可用性与移动复验共用同一条目——改写后两调用点同步生效。</summary>
    public const string MoveFrontlineEnemy = "move.frontline-enemy";
}
