namespace Orc.Game.EffectParsing.Dsl;

/// <summary>
/// DSL 官方原语登记项（转换段 S1/S4）：原语名 + 参数约束。
/// <see cref="Required"/> ＝必须出现；<see cref="AnyOf"/> ＝至少出现其一（空＝不要求）。
/// </summary>
/// <param name="Name">原语名（DSL <c>op</c> 字段取值）。</param>
/// <param name="Required">必须出现的参数字段名。</param>
/// <param name="AnyOf">至少出现其一的参数字段名。</param>
public sealed record DslOpSpec(
    string Name,
    IReadOnlyList<string>? Required = null,
    IReadOnlyList<string>? AnyOf = null)
{
    /// <summary>必须出现的参数（非 null 空列表）。</summary>
    public IReadOnlyList<string> RequiredFields => Required ?? Array.Empty<string>();

    /// <summary>至少出现其一的参数（非 null 空列表）。</summary>
    public IReadOnlyList<string> AnyOfFields => AnyOf ?? Array.Empty<string>();
}

/// <summary>
/// DSL 官方原语注册表（转换段 S1/S4）：本批 MVP＝
/// <c>damage</c>／<c>draw</c>／<c>buff</c>／<c>grant</c>／<c>move</c> ＋ <c>csx</c>（逃生舱）。
/// 词表条目须引用此表内的原语名（单一真源）。
/// </summary>
public static class DslOpRegistry
{
    /// <summary>逃生舱原语名（csx 脚本注入）。</summary>
    public const string CsxOpName = "csx";

    /// <summary>"需要 csx 实现"留痕原语名（op 无法解析时保留原文；用户口径）。</summary>
    public const string NeedsCsxOpName = "needsCsx";

    /// <summary>内嵌效果原语名（引号内的效果文本递归解析后的承载）。</summary>
    public const string NestedOpName = "nested";

    /// <summary>已登记原语（声明序）。</summary>
    public static IReadOnlyList<DslOpSpec> All { get; } = new[]
    {
        new DslOpSpec("damage", Required: new[] { "amount" }),
        new DslOpSpec("draw", Required: new[] { "count" }),
        new DslOpSpec("buff", AnyOf: new[] { "attack", "defense" }),
        new DslOpSpec("grant", Required: new[] { "keyword" }),
        new DslOpSpec("move", Required: new[] { "zone" }),
        new DslOpSpec("destroy", Required: new[] { "target" }),
        new DslOpSpec("pin"),
        new DslOpSpec("silence"),
        new DslOpSpec("costMod", Required: new[] { "amount" }),
        new DslOpSpec("addToHand", Required: new[] { "name" }),
        new DslOpSpec("shuffleIn", Required: new[] { "name" }),
        // E1-25（指挥点槽事件）：额外获得 / 失去 n 个指挥点槽（经 EffectRuntime 受控面）。
        new DslOpSpec("gainSlot", Required: new[] { "amount" }),
        new DslOpSpec("loseSlot", Required: new[] { "amount" }),
        // E1-25 后续（指挥点事件）：获得 / 失去 n 个指挥点（经 EffectRuntime 受控面）。
        new DslOpSpec("gainPoint", Required: new[] { "amount" }),
        new DslOpSpec("losePoint", Required: new[] { "amount" }),
        new DslOpSpec(CsxOpName, Required: new[] { "script" }),
        // "op 无法解析"的留痕原语（用户口径）：原文保留进 DSL，标记为**需要 csx 实现**。
        new DslOpSpec(NeedsCsxOpName, Required: new[] { "script" }),
        // 内嵌效果（引号内的效果文本递归解析后的承载）。
        new DslOpSpec(NestedOpName, Required: new[] { "nested" }),
        // E1-56：光环（持续态的正确机制——受益随进出/位置实时重算）。
        new DslOpSpec("aura", Required: new[] { "field", "amount" }),
        // S3：升为老兵 / 揭示（csx 对接 EffectRuntime.UpgradeAsync / RevealAsync——S1/S2 冻结入口）。
        new DslOpSpec("upgrade"),
        new DslOpSpec("reveal"),
    };

    private static readonly Dictionary<string, DslOpSpec> ByName =
        All.ToDictionary(spec => spec.Name, StringComparer.Ordinal);

    /// <summary>按名查原语。</summary>
    public static bool TryGet(string name, out DslOpSpec spec) => ByName.TryGetValue(name, out spec!);

    /// <summary>
    /// 校验单个 op：原语是否登记、必填/择一参数是否满足、数值是否合法。
    /// 返回错误清单（空＝通过）。
    /// </summary>
    /// <exception cref="ArgumentNullException">op 为 null。</exception>
    public static IReadOnlyList<string> Validate(DslOp op)
    {
        ArgumentNullException.ThrowIfNull(op);

        var errors = new List<string>();

        if (!TryGet(op.Op, out var spec))
        {
            errors.Add($"DSL 原语未登记：'{op.Op}'。");
            return errors;
        }

        foreach (var field in spec.RequiredFields)
        {
            if (!HasField(op, field))
            {
                errors.Add($"原语 '{op.Op}' 缺少必填参数 '{field}'。");
            }
        }

        if (spec.AnyOfFields.Count > 0 && !spec.AnyOfFields.Any(field => HasField(op, field)))
        {
            errors.Add($"原语 '{op.Op}' 至少需要参数之一：{string.Join(" / ", spec.AnyOfFields)}。");
        }

        // E1-41/E1-56：`amount` 的"不能为负"只适用于**数值语义**原语（伤害/抽牌/资源增量）；
        // `costMod`／`aura` 的 amount 是**修饰增量**（`-1 行动花费` 合法）⇒ 不在此列。
        if (op.Amount is < 0 && op.Op is not ("costMod" or "aura"))
        {
            errors.Add($"原语 '{op.Op}' 的 amount 不能为负。");
        }

        if (op.Count is < 1)
        {
            errors.Add($"原语 '{op.Op}' 的 count 必须为正整数。");
        }

        // E1-41：期限只对**可携带期限**的 op 生效——否则"忽略期限＝永久增益"属静默语义错误。
        if (!string.IsNullOrWhiteSpace(op.Until))
        {
            if (op.Until is not (Untils.TurnEnd or Untils.NextOwnerTurnStart))
            {
                errors.Add($"原语 '{op.Op}' 的 until 取值非法：'{op.Until}'（允许 {Untils.TurnEnd} / {Untils.NextOwnerTurnStart}）。");
            }
            else if (op.Op is not ("buff" or "costMod"))
            {
                errors.Add($"原语 '{op.Op}' 不支持期限（until 仅 buff / costMod 可用——忽略期限会产生永久增益）。");
            }
        }

        // 词条效果化·批 0：参值（value）＝**授予类 op 的参值形态**（「重甲3」的 3）；
        // 其它 op 携带 value 会被其模板静默忽略（模板无 {{value}} 占位符）——按"不支持参值"明确拒绝，防静默忽略。
        if (op.Value is not null && op.Op != "grant")
        {
            errors.Add($"原语 '{op.Op}' 不支持参值（value 仅 grant 可用；通道层不校验值域——值域由词条组件钳制）。");
        }

        return errors;
    }

    private static bool HasField(DslOp op, string field) => field switch
    {
        "target" => op.Target is not null,
        "filter" => op.Filter is not null,
        "amount" => op.Amount is not null,
        "count" => op.Count is not null,
        "attack" => op.Attack is not null,
        "defense" => op.Defense is not null,
        "keyword" => !string.IsNullOrWhiteSpace(op.Keyword),
        "zone" => !string.IsNullOrWhiteSpace(op.Zone),
        "script" => !string.IsNullOrWhiteSpace(op.Script),
        "name" => !string.IsNullOrWhiteSpace(op.Name),
        "nested" => op.Nested is { Count: > 0 },
        "until" => !string.IsNullOrWhiteSpace(op.Until),
        "field" => !string.IsNullOrWhiteSpace(op.Field),
        _ => false,
    };
}
