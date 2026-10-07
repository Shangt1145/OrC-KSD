namespace Orc.Game.EffectParsing.Dsl;

// ─────────────────────────────────────────────────────────────────────────────
// 效果 DSL 模型（转换段 S1）：
//   DSL 实例＝「模板引用 + 各槽位填写」；一个实例描述**一个效果**。
//   槽位内容＝op 序列；op 参数模型沿用 kards-diy 结构（选择器/过滤/数值），
//   本批只启用子集字段：目标选择器（sel/side/zone/count + 极简 filter）、
//   常数数值；不启用条件（condition）。
//   csx 逃生舱是「一种 op」（Op = "csx"），其脚本由人工填写。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>DSL 实例：用某模板 + 各槽位填写内容描述一个效果。</summary>
public sealed class DslEffectInstance
{
    /// <summary>创建 DSL 实例（构造期 fail-fast）。</summary>
    /// <exception cref="ArgumentException">template 空白；fills 含空白槽位名。</exception>
    /// <exception cref="ArgumentNullException">fills 或其值为 null。</exception>
    public DslEffectInstance(string template, IReadOnlyDictionary<string, DslSlotFill> fills)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(fills);

        foreach (var pair in fills)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
        }

        Template = template;
        Fills = fills;
    }

    /// <summary>模板 id（指向 <c>Templates/*.tpl.json</c> 的 <c>id</c>）。</summary>
    public string Template { get; }

    /// <summary>槽位名 → 填写内容（声明序）。</summary>
    public IReadOnlyDictionary<string, DslSlotFill> Fills { get; }
}

/// <summary>槽位填写内容：有序 op 序列（至少一个）。</summary>
public sealed class DslSlotFill
{
    /// <summary>创建槽位填写（构造期 fail-fast）。</summary>
    /// <exception cref="ArgumentException">ops 为空。</exception>
    /// <exception cref="ArgumentNullException">ops 或其元素为 null。</exception>
    public DslSlotFill(IReadOnlyList<DslOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        if (ops.Count == 0)
        {
            throw new ArgumentException("槽位填写至少需要一个 op。", nameof(ops));
        }

        foreach (var op in ops)
        {
            ArgumentNullException.ThrowIfNull(op);
        }

        Ops = ops;
    }

    /// <summary>op 序列（声明序＝生成顺序）。</summary>
    public IReadOnlyList<DslOp> Ops { get; }
}

/// <summary>
/// DSL op（带参数原语）：<see cref="Op"/> 为原语名（须在 <see cref="DslOpRegistry"/> 内）；
/// 其余为可选用参数字段。csx 逃生舱＝<c>Op = "csx"</c> 且 <see cref="Script"/> 非空。
/// </summary>
public sealed class DslOp
{
    /// <summary>创建 op（构造期 fail-fast）。</summary>
    /// <exception cref="ArgumentException">op 名称空白；csx 逃生舱缺少脚本。</exception>
    public DslOp(
        string op,
        DslSelector? target = null,
        DslFilter? filter = null,
        int? amount = null,
        int? count = null,
        int? attack = null,
        int? defense = null,
        string? keyword = null,
        string? zone = null,
        string? script = null,
        DslCondition? condition = null,
        string? name = null,
        IReadOnlyList<DslEffectInstance>? nested = null,
        string? until = null,
        string? field = null,
        int? value = null)
    {
        Field = field;
        Value = value;
        ArgumentException.ThrowIfNullOrWhiteSpace(op);

        if (string.Equals(op, "csx", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(script))
        {
            throw new ArgumentException("csx 逃生舱必须提供 script。", nameof(script));
        }

        Op = op;
        Target = target;
        Filter = filter;
        Amount = amount;
        Count = count;
        Attack = attack;
        Defense = defense;
        Keyword = keyword;
        Zone = zone;
        Script = script;
        Condition = condition;
        Name = name;
        Nested = nested;
        Until = until;
    }

    /// <summary>
    /// **目标字段**（E1-56；仅 <c>aura</c>）：`attack`／`defense`／`opCost`／`deployCost`。
    /// </summary>
    public string? Field { get; }

    /// <summary>
    /// **期限**（E1-41）：<c>buff</c>/<c>costMod</c> 类"持续态"的到期相位。
    /// 取值＝<see cref="Untils"/> 中的常量（<c>turnEnd</c>＝本回合结束、<c>nextOwnerTurnStart</c>＝下个己方回合开始）；
    /// null＝无期限（修饰器随效果存续）。**忽略期限会产出"永久增益"＝语义错误**，故不支持期限的 op 不得带本字段。
    /// </summary>
    public string? Until { get; }

    /// <summary>
    /// **内嵌效果**（嵌套效果解析）：引号里"本身是一段效果"的文本被递归解析后的 DSL 效果序列
    /// （如 `获得：“亡计：将 1 张本单位的复制加入手中…”`）。
    /// </summary>
    public IReadOnlyList<DslEffectInstance>? Nested { get; }

    /// <summary>卡牌引用名（引号封装的原名；<c>addToHand</c>/<c>shuffleIn</c> 等用）。</summary>
    public string? Name { get; }

    /// <summary>原语名（官方 op 名 或 <c>csx</c>）。</summary>
    public string Op { get; }

    /// <summary>条件（可空；<c>owner.*</c>＝归属过滤、<c>raw</c>＝占位）。</summary>
    public DslCondition? Condition { get; }

    /// <summary>目标选择器。</summary>
    public DslSelector? Target { get; }

    /// <summary>过滤条件（MVP：兵种/词条）。</summary>
    public DslFilter? Filter { get; }

    /// <summary>伤害/治疗类数值。</summary>
    public int? Amount { get; }

    /// <summary>张数/次数。</summary>
    public int? Count { get; }

    /// <summary>攻击力增减。</summary>
    public int? Attack { get; }

    /// <summary>防御力增减。</summary>
    public int? Defense { get; }

    /// <summary>词条名。</summary>
    public string? Keyword { get; }

    /// <summary>
    /// **词条参值**（词条效果化·批 0）：授予类 op（<c>grant</c>）携带的参数值（如「重甲3」的 3）。
    /// 可空——null＝**未提供参值**（与「显式 0」为两种不同形态，渲染与承接均区分）；
    /// 通道层不做值域校验（非负/上限等语义域）——值域约束由词条组件既有机制承载（如重甲/情报 [0,3] 钳制）。
    /// </summary>
    public int? Value { get; }

    /// <summary>落点区域。</summary>
    public string? Zone { get; }

    /// <summary>csx 逃生舱脚本（仅 <c>Op = "csx"</c>）。</summary>
    public string? Script { get; }
}

/// <summary>
/// 期限取值（E1-41；<see cref="DslOp.Until"/> 的词表）：持续态修饰器的**到期相位**。
/// </summary>
public static class Untils
{
    /// <summary>本回合结束（`直到回合结束`／`本回合…`）。</summary>
    public const string TurnEnd = "turnEnd";

    /// <summary>下个己方回合开始（`直到下个友方回合开始`）。</summary>
    public const string NextOwnerTurnStart = "nextOwnerTurnStart";
}

/// <summary>
/// 条件（结构支持）：<see cref="Kind"/>＝归属过滤（**真实 csx**——按"事件归属"的载荷面取同主/异主）、
/// 事件卡属性过滤（**真实 csx**——<see cref="EventCardKeyword"/>）、合取（<see cref="AllKind"/>）
/// 或 <c>raw</c>（其它条件——**占位**：渲染为 <c>if (false /* TODO */)</c>，待求值面就绪）。
/// <para>归属过滤的**取值面**（E1-39 拆分）：事件载荷是**卡/单位**（<c>card.*</c>/<c>unit.*</c>）用
/// <see cref="OwnerSame"/>/<see cref="OwnerDifferent"/>；载荷**只有玩家、无事件卡**（<c>slot.gained</c>/<c>slot.lost</c>）
/// 用 <see cref="OwnerSameByPlayer"/>/<see cref="OwnerDifferentByPlayer"/>（比较宿主玩家与载荷玩家）。</para>
/// </summary>
public sealed class DslCondition
{
    /// <summary>归属过滤：同主（事件载荷为卡/单位）。</summary>
    public const string OwnerSame = "owner.same";

    /// <summary>归属过滤：异主（事件载荷为卡/单位）。</summary>
    public const string OwnerDifferent = "owner.different";

    /// <summary>归属过滤：同主（事件载荷为玩家——无事件卡信号）。</summary>
    public const string OwnerSameByPlayer = "owner.same.player";

    /// <summary>归属过滤：异主（事件载荷为玩家——无事件卡信号）。</summary>
    public const string OwnerDifferentByPlayer = "owner.different.player";

    /// <summary>
    /// **事件卡属性过滤**（E1-42 乙 → E1-54 甲）：<see cref="Raw"/> 承载 `维度:取值`——
    /// `keyword:情报`（词条）／`tag:海军`（子类别）／`category:Command`（卡类型）／`faction:Britain`（阵营）／`name:计划`（卡名）。
    /// 渲染为真实 csx 守卫（读 <c>view.Card</c>）；多个维度经 <see cref="AllKind"/> 合取。
    /// </summary>
    public const string EventCardFilter = "eventCard.filter";

    /// <summary>
    /// 载荷字段**自指**（E1-47）：<see cref="Raw"/> 承载**视图属性名**（受控词表：`Card`／`Unit`／`Killer`／`Player`／`Host`），
    /// 渲染为 <c>object.ReferenceEquals(view.&lt;属性&gt;, self)</c>——用于「本单位造成伤害时／本单位消灭…时」的**自指守卫**
    /// （修 I35 同族缺口：监听不再对"任意单位"泛触发）。
    /// </summary>
    public const string PayloadSelf = "payload.self";

    /// <summary>
    /// 载荷字段**是 HQ**（E1-47）：<see cref="Raw"/> 承载视图属性名，渲染为 <c>view.&lt;属性&gt; is Hq</c>——
    /// 用于「对敌方总部造成伤害时」（受方是总部）。
    /// </summary>
    public const string PayloadIsHq = "payload.hq";

    /// <summary>
    /// 载荷字段的**归属面**（E1-50）：<see cref="Raw"/> 承载视图属性名，渲染为
    /// 「该字段（按卡取 Owner）与 self 同主」——用于「友方单位造成伤害时／友方单位消灭…时」。
    /// </summary>
    public const string PayloadOwnerSame = "payload.owner.same";

    /// <summary>载荷字段的归属面：异主（见 <see cref="PayloadOwnerSame"/>）。</summary>
    public const string PayloadOwnerDifferent = "payload.owner.different";

    /// <summary>
    /// **数值比较条件**（E1-57）：<see cref="Raw"/> 承载 左度量:算子:右操作数 规范串（如 count:s=friendly:gte:#3）。
    /// 渲染为真实 csx（EffectRuntime.EvaluateCondition）——**求值是纯函数**（可安全参与 &amp;&amp; 合取）。
    /// </summary>
    public const string Compare = "compare";

    /// <summary>
    /// **回合归属类条件**（S3）：<see cref="Raw"/> 承载 `friendly`／`enemy`——语义＝「该卡拥有者视角的
    /// 回合归属判定」（owner == 当前行动方／≠ 当前行动方）；渲染为真实 csx（求值挂钩既有回合归属判定面）。
    /// </summary>
    public const string TurnOwner = "turn.owner";

    /// <summary>合取：<see cref="All"/> 中全部子条件为真才为真（渲染为 <c>(a &amp;&amp; b)</c>）。</summary>
    public const string AllKind = "all";

    /// <summary>其它条件（占位）。</summary>
    public const string RawKind = "raw";

    /// <summary>创建条件。</summary>
    /// <exception cref="ArgumentException">kind 空白。</exception>
    public DslCondition(string kind, string? raw = null, IReadOnlyList<DslCondition>? all = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
        Raw = raw;
        All = all;
    }

    /// <summary>条件种类（<c>owner.same</c>｜<c>owner.different</c>｜<c>owner.same.player</c>｜<c>owner.different.player</c>｜<c>eventCard.keyword</c>｜<c>all</c>｜<c>raw</c>）。</summary>
    public string Kind { get; }

    /// <summary>原文（诊断/占位注释用）；<see cref="EventCardKeyword"/> 时承载**词条标识**。</summary>
    public string? Raw { get; }

    /// <summary>合取子条件（仅 <see cref="AllKind"/>；空/null＝恒真）。</summary>
    public IReadOnlyList<DslCondition>? All { get; }
}

/// <summary>目标选择器（沿用 kards-diy 命名，MVP 启用子集字段）。</summary>
public sealed class DslSelector
{
    /// <summary>创建选择器（构造期 fail-fast）。</summary>
    /// <exception cref="ArgumentException">sel 空白。</exception>
    public DslSelector(string sel, string? side = null, string? zone = null, DslFilter? filter = null, int? count = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sel);
        Sel = sel;
        Side = side;
        Zone = zone;
        Filter = filter;
        Count = count;
    }

    /// <summary>选靶方式（all/random/one/self/ref…）。</summary>
    public string Sel { get; }

    /// <summary>阵营（friendly/enemy/both）。</summary>
    public string? Side { get; }

    /// <summary>区域（frontline/support/hand/deck）。</summary>
    public string? Zone { get; }

    /// <summary>过滤。</summary>
    public DslFilter? Filter { get; }

    /// <summary>数量。</summary>
    public int? Count { get; }
}

/// <summary>过滤条件（MVP：兵种/词条）。</summary>
public sealed class DslFilter
{
    /// <summary>创建过滤（至少一个维度）。</summary>
    /// <exception cref="ArgumentException">所有维度都为空。</exception>
    public DslFilter(
        string? unitType = null,
        string? keyword = null,
        string? faction = null,
        bool excludeSelf = false,
        string? thresholdField = null,
        string? thresholdOp = null,
        int? thresholdValue = null)
    {
        if (string.IsNullOrWhiteSpace(unitType) && string.IsNullOrWhiteSpace(keyword)
            && string.IsNullOrWhiteSpace(faction) && !excludeSelf && string.IsNullOrWhiteSpace(thresholdField))
        {
            throw new ArgumentException("过滤条件至少需要一个维度（unitType/keyword/faction/excludeSelf/threshold）。", nameof(unitType));
        }

        UnitType = unitType;
        Keyword = keyword;
        Faction = faction;
        ExcludeSelf = excludeSelf;
        ThresholdField = thresholdField;
        ThresholdOp = thresholdOp;
        ThresholdValue = thresholdValue;
    }

    /// <summary>兵种（unitType）。</summary>
    public string? UnitType { get; }

    /// <summary>词条（keyword）。</summary>
    public string? Keyword { get; }

    /// <summary>阵营（E1-56；<see cref="Orc.Game.Cards.Faction"/> 枚举名）。</summary>
    public string? Faction { get; }

    /// <summary>排除宿主自身（E1-56；`其他/其它…`）。</summary>
    public bool ExcludeSelf { get; }

    /// <summary>阈值维度（E1-57；ttack／defense／opCost——目标属性过滤，null＝不限）。</summary>
    public string? ThresholdField { get; }

    /// <summary>阈值算子（gte／lte／gt／lt／eq）。</summary>
    public string? ThresholdOp { get; }

    /// <summary>阈值取值。</summary>
    public int? ThresholdValue { get; }

}
