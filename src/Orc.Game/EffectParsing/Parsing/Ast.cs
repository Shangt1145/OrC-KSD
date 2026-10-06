namespace Orc.Game.EffectParsing.Parsing;

/// <summary>原文文本区间（B-D4：一律指原文偏移）。</summary>
/// <param name="Start">起始偏移。</param>
/// <param name="Length">长度。</param>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>结束偏移（不含）。</summary>
    public int End => Start + Length;
}

/// <summary>边界种类。</summary>
public enum BoundaryKind
{
    /// <summary>硬边界（`。！？` 或文末）。</summary>
    Hard,

    /// <summary>软边界（`；`）——继承最近的硬边界单元。</summary>
    Soft,
}

/// <summary>触发语法种类。</summary>
public enum TriggerSyntaxKind
{
    /// <summary>具名触发（冒号界定，如"部署"）。</summary>
    Named,

    /// <summary>监听型触发（"…时/后"）。</summary>
    Listen,

    /// <summary>无显式触发（默认：尽量解析为被动触发器）。</summary>
    Implicit,
}

/// <summary>过滤维度。</summary>
public enum FilterKind
{
    /// <summary>兵种。</summary>
    UnitType,

    /// <summary>词条。</summary>
    Keyword,

    /// <summary>系列。</summary>
    Set,

    /// <summary>卡名。</summary>
    Name,

    /// <summary>属性（攻击力/防御力）。</summary>
    Attribute,

    /// <summary>对象名词（花费／指挥点槽等——用于消歧动作含义）。</summary>
    Object,

    /// <summary>阈值。</summary>
    Threshold,
}

/// <summary>载荷种类（本批仅整数）。</summary>
public enum PayloadKind
{
    /// <summary>整数。</summary>
    Integer,
}

/// <summary>触发事件短语（多事件时并列）。</summary>
/// <param name="RawText">原文短语。</param>
/// <param name="Span">原文区间。</param>
public sealed record TriggerEventPhrase(string RawText, TextSpan Span);

/// <summary>触发节点。</summary>
public sealed class TriggerNode
{
    /// <summary>创建触发节点。</summary>
    public TriggerNode(
        TriggerSyntaxKind kind,
        string rawText,
        IReadOnlyList<TriggerEventPhrase> events,
        TextSpan span,
        string? sideRaw = null,
        string? sideValue = null)
    {
        Kind = kind;
        RawText = rawText;
        Events = events;
        Span = span;
        SideRaw = sideRaw;
        SideValue = sideValue;
    }

    /// <summary>监听短语内的阵营原词（如"友方"；无＝null）。</summary>
    public string? SideRaw { get; }

    /// <summary>监听短语内的阵营归类（friendly/enemy/both；无＝null）。</summary>
    public string? SideValue { get; }

    /// <summary>语法种类。</summary>
    public TriggerSyntaxKind Kind { get; }

    /// <summary>原文触发短语。</summary>
    public string RawText { get; }

    /// <summary>事件短语（多事件并列；Implicit 时为空）。</summary>
    public IReadOnlyList<TriggerEventPhrase> Events { get; }

    /// <summary>原文区间。</summary>
    public TextSpan Span { get; }
}

/// <summary>过滤短语。</summary>
/// <param name="Kind">维度。</param>
/// <param name="RawText">原词。</param>
/// <param name="Value">归类值。</param>
/// <param name="Span">原文区间。</param>
public sealed record FilterPhrase(FilterKind Kind, string RawText, string? Value, TextSpan Span);

/// <summary>目标短语（携带词法归类值——B-D1）。</summary>
public sealed class TargetPhrase
{
    /// <summary>创建目标短语。</summary>
    public TargetPhrase(
        string? quantifierRaw,
        string? quantifierSel,
        string? sideRaw,
        string? sideValue,
        string? zoneRaw,
        string? zoneValue,
        IReadOnlyList<FilterPhrase> filters,
        bool excludeSelf,
        bool hasPronoun,
        string? pronounForm,
        TextSpan span,
        bool hasUnrecognizedQualifier = false)
    {
        HasUnrecognizedQualifier = hasUnrecognizedQualifier;
        QuantifierRaw = quantifierRaw;
        QuantifierSel = quantifierSel;
        SideRaw = sideRaw;
        SideValue = sideValue;
        ZoneRaw = zoneRaw;
        ZoneValue = zoneValue;
        Filters = filters;
        ExcludeSelf = excludeSelf;
        HasPronoun = hasPronoun;
        PronounForm = pronounForm;
        Span = span;
    }

    /// <summary>量词原词（如"一个"）。</summary>
    public string? QuantifierRaw { get; }

    /// <summary>量词归类（one/all/random/any）。</summary>
    public string? QuantifierSel { get; }

    /// <summary>阵营原词。</summary>
    public string? SideRaw { get; }

    /// <summary>阵营归类（friendly/enemy/both）。</summary>
    public string? SideValue { get; }

    /// <summary>区域原词。</summary>
    public string? ZoneRaw { get; }

    /// <summary>区域归类（frontline/support/hand/deck/hq）。</summary>
    public string? ZoneValue { get; }

    /// <summary>过滤短语。</summary>
    public IReadOnlyList<FilterPhrase> Filters { get; }

    /// <summary>"其他/其它"。</summary>
    public bool ExcludeSelf { get; }

    /// <summary>是否含代词。</summary>
    public bool HasPronoun { get; }

    /// <summary>代词原形。</summary>
    public string? PronounForm { get; }

    /// <summary>原文区间。</summary>
    public TextSpan Span { get; }

    /// <summary>
    /// 目标区（**动词之前**）是否含**未识别限定词**（E1-52）：如 `相邻陆军`／`本单位左侧所有单位`／
    /// `敌方指令`／`受伤单位`——此类限定词无法表达，**静默丢弃会产出错误目标**，故由语义层据此拒绝映射。
    /// 纯句法虚词（`使`/`将`/`对`/`的`…）不计入。
    /// </summary>
    public bool HasUnrecognizedQualifier { get; }
}

/// <summary>数值载荷节点（本批仅整数——用户修订）。</summary>
/// <param name="Kind">种类。</param>
/// <param name="Int">整数值。</param>
/// <param name="Raw">原文。</param>
/// <param name="Span">原文区间。</param>
public sealed record PayloadNode(PayloadKind Kind, int Int, string Raw, TextSpan Span);

/// <summary>动作短语。</summary>
public sealed class ActionPhrase
{
    /// <summary>创建动作短语。</summary>
    public ActionPhrase(
        string verbRaw,
        string? verbKey,
        string? objectRaw,
        PayloadNode? payload,
        TextSpan span,
        PayloadNode? secondaryPayload = null)
    {
        VerbRaw = verbRaw;
        VerbKey = verbKey;
        ObjectRaw = objectRaw;
        Payload = payload;
        Span = span;
        SecondaryPayload = secondaryPayload;
    }

    /// <summary>第二数值（如「获得 +1+1」的防御力值；无＝null）。</summary>
    public PayloadNode? SecondaryPayload { get; }

    /// <summary>动作词原文。</summary>
    public string VerbRaw { get; }

    /// <summary>动作词归类键（damage/draw/gain/move/destroy…）。</summary>
    public string? VerbKey { get; }

    /// <summary>宾语原文（未命中词表的连续片段）。</summary>
    public string? ObjectRaw { get; }

    /// <summary>数值载荷。</summary>
    public PayloadNode? Payload { get; }

    /// <summary>原文区间。</summary>
    public TextSpan Span { get; }
}

/// <summary>条件短语（本批入 AST，由 S9 判"超子集"）。</summary>
/// <param name="RawText">原文。</param>
/// <param name="Span">原文区间。</param>
public sealed record ConditionPhrase(string RawText, TextSpan Span);

/// <summary>子句节点。</summary>
/// <summary>
/// **数值比较短语**（E1-57）：`&lt;左度量&gt; &lt;比较算子&gt; &lt;右操作数&gt;`——如
/// `友方单位数 不小于 3`／`其攻击力 不大于 3`／`友方总部防御力 大于 敌方总部`。
/// <para>左度量由**算子之前的**过滤短语/阵营/区域与"计数标记（`…数`）"判定；右操作数＝算子的数值，
/// 或"同一度量在**另一方**上"（`敌方单位`/`敌方总部`）。</para>
/// </summary>
public sealed class ComparisonPhrase
{
    /// <summary>创建比较短语。</summary>
    public ComparisonPhrase(
        string opRaw,
        string op,
        IReadOnlyList<FilterPhrase> leftFilters,
        string? leftSide,
        string? leftZone,
        bool leftIsCount,
        int? rightValue,
        bool rightIsCount,
        string? rightSide,
        string? rightZone,
        TextSpan span)
    {
        OpRaw = opRaw;
        Op = op;
        LeftFilters = leftFilters;
        LeftSide = leftSide;
        LeftZone = leftZone;
        LeftIsCount = leftIsCount;
        RightValue = rightValue;
        RightIsCount = rightIsCount;
        RightSide = rightSide;
        RightZone = rightZone;
        Span = span;
    }

    /// <summary>算子原文（`不小于`…）。</summary>
    public string OpRaw { get; }

    /// <summary>算子键（`gte`／`lte`／`gt`／`lt`）。</summary>
    public string Op { get; }

    /// <summary>左度量的过滤短语（算子之前的属性/对象维度）。</summary>
    public IReadOnlyList<FilterPhrase> LeftFilters { get; }

    /// <summary>左度量阵营（`friendly`／`enemy`）。</summary>
    public string? LeftSide { get; }

    /// <summary>左度量区域（如 `hq`）。</summary>
    public string? LeftZone { get; }

    /// <summary>左度量是否"计数"（`…数`）。</summary>
    public bool LeftIsCount { get; }

    /// <summary>右操作数数值（无＝null）。</summary>
    public int? RightValue { get; }

    /// <summary>右操作数是否"计数"（`敌方单位`）。</summary>
    public bool RightIsCount { get; }

    /// <summary>右操作数阵营（引用形态）。</summary>
    public string? RightSide { get; }

    /// <summary>右操作数区域（引用形态）。</summary>
    public string? RightZone { get; }

    /// <summary>原文区间。</summary>
    public TextSpan Span { get; }
}

public sealed class ClauseNode
{
    /// <summary>创建子句节点。</summary>
    public ClauseNode(
        ConditionPhrase? condition,
        TargetPhrase? target,
        IReadOnlyList<ActionPhrase> actions,
        TextSpan span,
        bool isTargetDeclaration = false,
        ComparisonPhrase? comparison = null)
    {
        Condition = condition;
        Target = target;
        Actions = actions;
        Span = span;
        IsTargetDeclaration = isTargetDeclaration;
        Comparison = comparison;
    }

    /// <summary>数值比较短语（E1-57；无＝null）。</summary>
    public ComparisonPhrase? Comparison { get; }

    /// <summary>
    /// 是否**纯目标声明**子句（无动作、但含至少一个已识别 token）——用于"先声明目标、后接动作"句式
    /// （如「指向 1 个单位，使其移至前线」）；此类子句不作未解析，其目标供后续子句的代词回指。
    /// </summary>
    public bool IsTargetDeclaration { get; }

    /// <summary>前导条件（可空）。</summary>
    public ConditionPhrase? Condition { get; }

    /// <summary>目标短语（可空）。</summary>
    public TargetPhrase? Target { get; }

    /// <summary>动作短语（空＝未映射子句）。</summary>
    public IReadOnlyList<ActionPhrase> Actions { get; }

    /// <summary>原文区间。</summary>
    public TextSpan Span { get; }
}

/// <summary>单效果语法树（中性语法树，与 DSL 解耦）。</summary>
public sealed class EffectAst
{
    /// <summary>创建效果语法树。</summary>
    public EffectAst(
        TextSpan span,
        BoundaryKind boundary,
        EffectAst? inheritsFrom,
        TriggerNode? trigger,
        IReadOnlyList<ClauseNode> clauses)
    {
        Span = span;
        Boundary = boundary;
        InheritsFrom = inheritsFrom;
        Trigger = trigger;
        Clauses = clauses;
    }

    /// <summary>整单元原文区间。</summary>
    public TextSpan Span { get; }

    /// <summary>边界种类。</summary>
    public BoundaryKind Boundary { get; }

    /// <summary>软边界的继承源（最近的硬边界单元；硬边界时为 null）。</summary>
    public EffectAst? InheritsFrom { get; }

    /// <summary>触发节点（可空＝无显式触发）。</summary>
    public TriggerNode? Trigger { get; }

    /// <summary>子句序列。</summary>
    public IReadOnlyList<ClauseNode> Clauses { get; }
}
