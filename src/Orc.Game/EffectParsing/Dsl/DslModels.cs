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
        IReadOnlyList<DslEffectInstance>? nested = null)
    {
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
    }

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

    /// <summary>落点区域。</summary>
    public string? Zone { get; }

    /// <summary>csx 逃生舱脚本（仅 <c>Op = "csx"</c>）。</summary>
    public string? Script { get; }
}

/// <summary>
/// 条件（结构支持）：<see cref="Kind"/>＝<c>owner</c>（归属过滤——**真实 csx**：宿主与事件卡同主/异主）
/// 或 <c>raw</c>（其它条件——**占位**：渲染为 <c>if (false /* TODO */)</c>，待求值面就绪）。
/// </summary>
public sealed class DslCondition
{
    /// <summary>归属过滤：同主。</summary>
    public const string OwnerSame = "owner.same";

    /// <summary>归属过滤：异主。</summary>
    public const string OwnerDifferent = "owner.different";

    /// <summary>其它条件（占位）。</summary>
    public const string RawKind = "raw";

    /// <summary>创建条件。</summary>
    /// <exception cref="ArgumentException">kind 空白。</exception>
    public DslCondition(string kind, string? raw = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
        Raw = raw;
    }

    /// <summary>条件种类（<c>owner.same</c>｜<c>owner.different</c>｜<c>raw</c>）。</summary>
    public string Kind { get; }

    /// <summary>原文（诊断/占位注释用）。</summary>
    public string? Raw { get; }
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
    /// <exception cref="ArgumentException">两个维度都为空。</exception>
    public DslFilter(string? unitType = null, string? keyword = null)
    {
        if (string.IsNullOrWhiteSpace(unitType) && string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("过滤条件至少需要一个维度（unitType/keyword）。", nameof(unitType));
        }

        UnitType = unitType;
        Keyword = keyword;
    }

    /// <summary>兵种（unitType）。</summary>
    public string? UnitType { get; }

    /// <summary>词条（keyword）。</summary>
    public string? Keyword { get; }
}
