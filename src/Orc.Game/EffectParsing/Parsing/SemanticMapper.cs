using Orc.Game.EffectParsing.Dsl;

namespace Orc.Game.EffectParsing.Parsing;

/// <summary>
/// AST → DSL 语义映射（解析段 S9）：语法节点映射为 <see cref="DslEffectInstance"/>；
/// 任何落在子集外的地方 → <see cref="UnresolvedRecord"/>（显式失败，不产占位效果）。
/// <list type="bullet">
///   <item>多事件触发在此**展开为多个效果**（B-D2）。</item>
///   <item>无显式触发 → 尽量解析为被动触发器（D2）；本批仅 <c>deploy_basic</c> 一个被动骨架。</item>
/// </list>
/// </summary>
public sealed class SemanticMapper
{
    private const string DefaultTemplate = "deploy_basic";

    private readonly Func<string, ParseResult>? _innerParser;

    /// <summary>创建映射器。</summary>
    /// <param name="innerParser">
    /// **内嵌效果**递归解析回调（由 `EffectParser` 注入自身）：引号里"本身是一段效果"的文本经它递归走完整管线。
    /// </param>
    public SemanticMapper(Func<string, ParseResult>? innerParser = null) => _innerParser = innerParser;

    private static readonly Dictionary<string, string> EventTemplates = new(StringComparer.Ordinal)
    {
        ["部署"] = DefaultTemplate,
        // 亡计 = 「本单位死亡时」→ 复用死亡监听骨架（hook card.died，施动卡＝宿主）。
        ["亡计"] = "death_basic",
        // 动员 = 「本单位移至前线时」→ 复用位置变化骨架。
        ["动员"] = "position_basic",
    };

    /// <summary>映射。</summary>
    /// <exception cref="ArgumentNullException">original 或 asts 为 null。</exception>
    public ParseResult Map(string original, IReadOnlyList<EffectAst> asts)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(asts);

        var effects = new List<DslEffectInstance>();
        var unresolved = new List<UnresolvedRecord>();

        foreach (var ast in asts)
        {
            MapEffect(original, ast, effects, unresolved);
        }

        return new ParseResult(effects, unresolved);
    }

    private void MapEffect(
        string original,
        EffectAst ast,
        List<DslEffectInstance> effects,
        List<UnresolvedRecord> unresolved)
    {
        // 触发 → 模板（多事件展开）
        if (ast.Trigger is { Kind: TriggerSyntaxKind.Listen } listen)
        {
            var listenTemplate = ResolveListenTemplate(listen.RawText);
            if (listenTemplate is null)
            {
                unresolved.Add(Fail(original, ast.Span, $"监听型触发暂不在子集内：'{listen.RawText}'。"));
                return;
            }

            // 归属过滤（A）：监听短语里的阵营 → owner 条件（真实 csx；仅当载荷携带事件卡时可用）。
            var sideCondition = SupportsOwnerFilter(listenTemplate)
                ? listen.SideValue switch
                {
                    "friendly" => new DslCondition(DslCondition.OwnerSame),
                    "enemy" => new DslCondition(DslCondition.OwnerDifferent),
                    _ => null,
                }
                : null;

            if (!TryBuildOps(original, ast, out var listenOps, out var listenFailures, sideCondition))
            {
                unresolved.AddRange(listenFailures);
                return;
            }

            effects.Add(new DslEffectInstance(
                listenTemplate,
                new Dictionary<string, DslSlotFill>(StringComparer.Ordinal)
                {
                    [SlotNameFor(listenTemplate)] = new(listenOps),
                }));
            return;
        }

        var templates = new List<(string Template, TextSpan Span)>();
        if (ast.Trigger is { Kind: TriggerSyntaxKind.Named } named)
        {
            foreach (var ev in named.Events)
            {
                if (EventTemplates.TryGetValue(ev.RawText, out var template))
                {
                    templates.Add((template, ev.Span));
                }
                else
                {
                    unresolved.Add(Fail(original, ev.Span, $"事件暂不在子集内：'{ev.RawText}'。"));
                }
            }
        }
        else
        {
            // 无显式触发（或隐含）：尽量解析为被动触发器
            templates.Add((DefaultTemplate, ast.Span));
        }

        if (templates.Count == 0)
        {
            return;
        }

        if (!TryBuildOps(original, ast, out var ops, out var failures))
        {
            unresolved.AddRange(failures);
            return;
        }

        foreach (var (template, _) in templates)
        {
            effects.Add(new DslEffectInstance(
                template,
                new Dictionary<string, DslSlotFill>(StringComparer.Ordinal)
                {
                    [SlotNameFor(template)] = new(ops),
                }));
        }
    }

    /// <summary>模板的（唯一）槽位名：部署骨架＝<c>on_deploy</c>；监听/事件骨架＝<c>on_event</c>。</summary>
    private static string SlotNameFor(string template) =>
        string.Equals(template, DefaultTemplate, StringComparison.Ordinal) ? "on_deploy" : "on_event";

    private bool TryBuildOps(
        string original,
        EffectAst ast,
        out List<DslOp> ops,
        out List<UnresolvedRecord> failures,
        DslCondition? effectCondition = null)
    {
        ops = new List<DslOp>();
        failures = new List<UnresolvedRecord>();
        var ok = true;

        // 前置扫：纯条件子句（无动作）＝整效果的占位条件（甲：结构支持）——归属过滤用 owner（真实 csx）。
        var defaultCondition = effectCondition;
        foreach (var clause in ast.Clauses)
        {
            if (clause.Condition is not null && clause.Actions.Count == 0)
            {
                defaultCondition = new DslCondition(DslCondition.RawKind, clause.Condition.RawText);
            }
        }

        foreach (var clause in ast.Clauses)
        {
            var condition = clause.Condition is null
                ? defaultCondition
                : new DslCondition(DslCondition.RawKind, clause.Condition.RawText);

            if (clause.Actions.Count == 0)
            {
                if (clause.Condition is not null || clause.IsTargetDeclaration)
                {
                    continue; // 纯条件子句 / 纯目标声明子句：不产出 op、也不计失败
                }

                failures.Add(Fail(original, clause.Span, "无法识别动作短语。"));
                ok = false;
                continue;
            }

            foreach (var action in clause.Actions)
            {
                if (TryMapAction(action, clause.Target, out var op, out var reason))
                {
                    ops.Add(WithCondition(op, condition));
                    continue;
                }

                // 「op 无法解析」（非句式）⇒ **原文保留进 DSL** 并标记"需要 csx 实现"（用户口径）——
                // 不产未解析记录（句式层失败才计未解析）。
                _ = reason;
                ops.Add(WithCondition(
                    new DslOp(DslOpRegistry.NeedsCsxOpName, script: RawOf(original, clause.Span)),
                    condition));
            }
        }

        if (ops.Count == 0)
        {
            ok = false;
        }

        return ok;
    }

    private static DslOp WithCondition(DslOp op, DslCondition? condition) =>
        condition is null
            ? op
            : new DslOp(
                op.Op, op.Target, op.Filter, op.Amount, op.Count, op.Attack, op.Defense,
                op.Keyword, op.Zone, op.Script, condition, op.Name, op.Nested);

    /// <summary>引号内容是否"本身是一段效果"（含触发界定符或句号 ⇒ 判为内嵌效果文本，而非卡名）。</summary>
    private static bool LooksLikeNestedEffect(string quoted) =>
        quoted.Contains('：') || quoted.Contains(':') || quoted.Contains('。');

    private bool TryMapAction(ActionPhrase action, TargetPhrase? target, out DslOp op, out string? reason)
    {
        op = null!;
        reason = null;
        var selector = BuildSelector(target);

        switch (action.VerbKey)
        {
            case "damage":
                if (action.Payload is null)
                {
                    reason = "造成伤害缺少数值。";
                    return false;
                }

                op = new DslOp("damage", selector, amount: action.Payload.Int);
                return true;

            case "draw":
                if (action.Payload is null)
                {
                    reason = "抽牌缺少张数。";
                    return false;
                }

                op = new DslOp("draw", selector, count: action.Payload.Int);
                return true;

            case "gain":
                // **嵌套效果**（任务 2）：引号里的内容"本身是一段效果"（如 `获得：“亡计：…”`）⇒ 递归解析并以内嵌效果承载。
                var quoted = FindFilterRaw(target, FilterKind.Name);
                if (quoted is not null && _innerParser is not null && LooksLikeNestedEffect(quoted))
                {
                    var inner = _innerParser(quoted);
                    if (inner.Effects.Count > 0)
                    {
                        op = new DslOp(DslOpRegistry.NestedOpName, nested: inner.Effects);
                        return true;
                    }
                }

                // 「获得」的含义**取决于其后的 token**（用户口径）：属性 → buff；花费 → costMod；
                // 指挥点槽 → gainSlot（额外获得 n 个指挥点槽）；指挥点 → gainPoint（获得 n 个指挥点）；
                // 词条 → grant；其余 → 显式失败。
                var attribute = FindFilter(target, FilterKind.Attribute);
                if (attribute is not null && action.Payload is not null)
                {
                    op = attribute == "attack"
                        ? new DslOp("buff", selector, attack: action.Payload.Int)
                        : new DslOp("buff", selector, defense: action.Payload.Int);
                    return true;
                }

                // 「获得 +1+1」＝两个数：第一＝攻击力、第二＝防御力（无属性名词时的形态）。
                if (action.Payload is not null && action.SecondaryPayload is not null && attribute is null)
                {
                    op = new DslOp("buff", selector, attack: action.Payload.Int, defense: action.SecondaryPayload.Int);
                    return true;
                }

                var objectNoun = FindFilter(target, FilterKind.Object);
                if (objectNoun == "opCost" && action.Payload is not null)
                {
                    op = new DslOp("costMod", selector, amount: action.Payload.Int);
                    return true;
                }

                if (objectNoun == "pointSlot")
                {
                    if (action.Payload is null)
                    {
                        reason = "额外获得指挥点槽缺少数值（需『获得 N 个指挥点槽』形式）。";
                        return false;
                    }

                    op = new DslOp("gainSlot", selector, amount: action.Payload.Int);
                    return true;
                }

                if (objectNoun == "point")
                {
                    if (action.Payload is null)
                    {
                        reason = "获得指挥点缺少数值（需『获得 N 个指挥点』形式）。";
                        return false;
                    }

                    op = new DslOp("gainPoint", selector, amount: action.Payload.Int);
                    return true;
                }

                var keyword = FindFilter(target, FilterKind.Keyword);
                if (keyword is not null)
                {
                    op = new DslOp("grant", selector, keyword: keyword);
                    return true;
                }

                reason = "无法判定的获得类效果（既非词条、属性、花费，也非指挥点槽）。";
                return false;

            case "lose":
                // 「失去」：本批覆盖「失去 n 个指挥点槽」→ loseSlot、「失去 n 个指挥点」→ losePoint；其余 → 显式失败。
                var lostNoun = FindFilter(target, FilterKind.Object);
                if (lostNoun == "pointSlot")
                {
                    if (action.Payload is null)
                    {
                        reason = "失去指挥点槽缺少数值（需『失去 N 个指挥点槽』形式）。";
                        return false;
                    }

                    op = new DslOp("loseSlot", selector, amount: action.Payload.Int);
                    return true;
                }

                if (lostNoun == "point")
                {
                    if (action.Payload is null)
                    {
                        reason = "失去指挥点缺少数值（需『失去 N 个指挥点』形式）。";
                        return false;
                    }

                    op = new DslOp("losePoint", selector, amount: action.Payload.Int);
                    return true;
                }

                reason = $"无法判定的失去类效果（'{action.VerbRaw}'）。";
                return false;

            case "move":
                if (string.IsNullOrWhiteSpace(target?.ZoneValue))
                {
                    reason = "移动缺少目标区域。";
                    return false;
                }

                op = new DslOp("move", selector, zone: target.ZoneValue);
                return true;

            case "addToHand":
            case "shuffleIn":
                var cardName = FindFilterRaw(target, FilterKind.Name);
                if (string.IsNullOrWhiteSpace(cardName))
                {
                    reason = $"'{action.VerbRaw}' 缺少引号卡名（需 “卡名” 形式）。";
                    return false;
                }

                op = new DslOp(action.VerbKey, selector, count: action.Payload?.Int ?? 1, name: cardName);
                return true;

            case "pin":
                op = new DslOp("pin", selector);
                return true;

            case "silence":
                op = new DslOp("silence", selector);
                return true;

            case "destroy":
                // 消灭＝**游戏层死亡链**（亡计/词条注销/修饰清理/card.died）；
                // 与总线 card.destroyed（内存销毁，可能只是弃牌）语义不同——不容混用。
                op = new DslOp("destroy", selector);
                return true;

            default:
                reason = $"未知动作：'{action.VerbRaw}'。";
                return false;
        }
    }

    private static DslSelector? BuildSelector(TargetPhrase? target)
    {
        if (target is null)
        {
            return null;
        }

        var hasQualifier = target.QuantifierSel is not null || target.SideValue is not null
                           || target.ZoneValue is not null || target.Filters.Count > 0;
        if (!hasQualifier)
        {
            return null;
        }

        var unitType = FindFilter(target, FilterKind.UnitType);
        var keyword = FindFilter(target, FilterKind.Keyword);
        var filter = unitType is null && keyword is null ? null : new DslFilter(unitType, keyword);

        return new DslSelector(target.QuantifierSel ?? "one", target.SideValue, target.ZoneValue, filter, null);
    }

    private static string? FindFilter(TargetPhrase? target, FilterKind kind)
    {
        if (target is null)
        {
            return null;
        }

        foreach (var filter in target.Filters)
        {
            if (filter.Kind == kind)
            {
                return filter.Value;
            }
        }

        return null;
    }

    /// <summary>取过滤短语的**原文**（卡名等无归类值的维度用）。</summary>
    private static string? FindFilterRaw(TargetPhrase? target, FilterKind kind)
    {
        if (target is null)
        {
            return null;
        }

        foreach (var filter in target.Filters)
        {
            if (filter.Kind == kind)
            {
                return filter.RawText;
            }
        }

        return null;
    }

    /// <summary>
    /// 监听短语 → 监听模板（按事件关键词；本批覆盖：被消灭、移动、抽牌）。
    /// 未命中＝null（由调用方记未解析——保持显式失败）。
    /// </summary>
    private static string? ResolveListenTemplate(string rawText)
    {
        if (rawText.Contains("被消灭", StringComparison.Ordinal)
            || rawText.Contains("阵亡", StringComparison.Ordinal)
            || rawText.Contains("死亡", StringComparison.Ordinal))
        {
            return "death_basic";
        }

        // E1-33 新信号：受到伤害 / 行动后。
        if (rawText.Contains("受到伤害", StringComparison.Ordinal) || rawText.Contains("受伤", StringComparison.Ordinal))
        {
            return "damaged_basic";
        }

        if (rawText.Contains("行动", StringComparison.Ordinal))
        {
            return "acted_basic";
        }

        // 攻击类：无总线信号 ⇒ 走**对局级具名流程触发器**的 injects 型模板（E1-27）。
        if (rawText.Contains("攻击", StringComparison.Ordinal))
        {
            return "attack_basic";
        }

        if (rawText.Contains("移动", StringComparison.Ordinal) || rawText.Contains("前线", StringComparison.Ordinal))
        {
            return "position_basic";
        }

        if (rawText.Contains("抽牌", StringComparison.Ordinal))
        {
            return "drawn_basic";
        }

        // 数值变更（攻击力/防御力"获得/改变时"）——已有信号 card.stat.changed。
        if (rawText.Contains("防御力", StringComparison.Ordinal) || rawText.Contains("攻击力", StringComparison.Ordinal))
        {
            return "stat_basic";
        }

        if (rawText.Contains("弃", StringComparison.Ordinal))
        {
            return "discard_basic";
        }

        if (rawText.Contains("爆牌", StringComparison.Ordinal))
        {
            return "burn_basic";
        }

        if (rawText.Contains("获得卡牌", StringComparison.Ordinal) || rawText.Contains("加入手牌", StringComparison.Ordinal))
        {
            return "hand_add_basic";
        }

        if (rawText.Contains("类型", StringComparison.Ordinal) && rawText.Contains("改变", StringComparison.Ordinal))
        {
            return "types_basic";
        }

        if (rawText.Contains("加载", StringComparison.Ordinal))
        {
            return "load_basic";
        }

        if (rawText.Contains("放置", StringComparison.Ordinal))
        {
            return "placed_basic";
        }

        if (rawText.Contains("回合结束前", StringComparison.Ordinal))
        {
            return "turn_end_before_basic";
        }

        if (rawText.Contains("回合开始前", StringComparison.Ordinal))
        {
            return "turn_start_before_basic";
        }

        if (rawText.Contains("回合结束", StringComparison.Ordinal))
        {
            return "turn_end_basic";
        }

        if (rawText.Contains("回合开始", StringComparison.Ordinal))
        {
            return "turn_start_basic";
        }

        if (rawText.Contains("摧毁", StringComparison.Ordinal))
        {
            return "destroy_basic"; // 引擎销毁（card.destroyed——与死亡链 card.died 语义不同）
        }

        if (rawText.Contains("洗切", StringComparison.Ordinal) || rawText.Contains("洗牌", StringComparison.Ordinal))
        {
            return "shuffle_basic";
        }

        if (rawText.Contains("加入", StringComparison.Ordinal))
        {
            return "join_basic";
        }

        if (rawText.Contains("部署", StringComparison.Ordinal))
        {
            return "deploy_listener";
        }

        if (rawText.Contains("使用指令", StringComparison.Ordinal) || rawText.Contains("打出指令", StringComparison.Ordinal))
        {
            return "played_basic";
        }

        return null;
    }

    /// <summary>
    /// 该监听模板的视图是否携带"事件卡"（决定能否做归属过滤——白名单口径，避免误用）：
    /// 载荷/视图不含卡（如 <c>turn.start</c>/<c>turn.end</c>/<c>deck.shuffled</c> 只有 Player/Deck；
    /// <c>attack_basic</c> 视图是 <c>UnitAttackTriggerView</c>）⇒ 不做归属过滤（否则守卫恒假或编译不过）。
    /// </summary>
    private static readonly HashSet<string> OwnerFilterableTemplates = new(StringComparer.Ordinal)
    {
        "death_basic",      // card.died        载荷 {Card}
        "position_basic",   // unit.position.*  载荷 {Unit,...}
        "drawn_basic",      // card.drawn       载荷 {Player, Card}
        "played_basic",     // card.played      载荷 {Card, Player}
        "deploy_listener",  // unit.deployed    载荷 {Card}
        "join_basic",       // unit.joined      载荷 {Card}
        "destroy_basic",    // card.destroyed   载荷 {Card}
        "stat_basic",       // card.stat.changed 载荷 {Card, ChangedFields}
        "discard_basic",    // card.discarded   载荷 {Card, Player}
        "burn_basic",       // card.burned      载荷 {Card, Player}
        "types_basic",      // unit.types.changed 载荷 {Unit, AddedType}
        "load_basic",       // card.load        载荷 {Player, Card}
        "placed_basic",     // card.placed      载荷 {Card}
        "damaged_basic",    // card.damaged     载荷 {Card, Amount}（E1-33）
    };

    private static bool SupportsOwnerFilter(string template) => OwnerFilterableTemplates.Contains(template);

    private static UnresolvedRecord Fail(string original, TextSpan span, string reason) =>
        new(span.Start, span.Length, RawOf(original, span), reason);

    private static string RawOf(string original, TextSpan span) =>
        span.Start >= 0 && span.Start + span.Length <= original.Length
            ? original.Substring(span.Start, span.Length)
            : string.Empty;
}
