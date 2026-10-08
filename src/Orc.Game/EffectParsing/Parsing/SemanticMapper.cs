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
        // S3：「揭示：X」＝事件前缀（「…被揭示时」同族）→ 揭示骨架（hook unit.revealed——S2 信号）。
        ["揭示"] = "reveal_basic",
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
            // 事件卡过滤的**原文切片**（含引号——`listen.RawText` 已剥引号，卡名维度须用原切片）。
            var listenOriginal = RawOf(original, listen.Span);
            var listenTemplate = ResolveListenTemplate(listen.RawText, listenOriginal, out var eventCardFilters);
            if (listenTemplate is null
                || (NeedsPayloadSubject(listenTemplate) && PayloadGuardOf(listenTemplate, listen.RawText) is null))
            {
                // 主体不可辨识（既非"本单位"也非"友方/敌方单位"）⇒ 显式失败（不泛触发）。
                unresolved.Add(Fail(original, ast.Span, $"监听型触发暂不在子集内：'{listen.RawText}'。"));
                return;
            }

            // 归属过滤（A）：监听短语里的阵营 → owner 条件（真实 csx；仅当**载荷面**能承载归属比较时可用）。
            // 事件卡属性过滤（E1-42 乙）：`使用情报牌时` 等 → **事件卡词条守卫**（真实 csx）。
            // 自指/受方面过滤（E1-47）：`本单位造成伤害时`／`本单位消灭…时`／`…对敌方总部…` → **载荷字段守卫**。
            // 三者可并存 ⇒ 合成**合取**条件（`all`）。
            var guards = new List<DslCondition?>();
            if (OwnerGuardKind(listenTemplate, listen.SideValue) is { } guardKind)
            {
                guards.Add(new DslCondition(guardKind));
            }

            // S3：监听短语内的「在场上的第 N 回合」限定（E1-56 口径的 V1 构成）→ **真实条件**
            // （turns 度量——求值挂钩既有读取面 UnitCard.TurnsInPlay；"达到 N"＝gte 读法）。
            if (InPlayTurnSpecOf(listen.RawText) is { } turnsSpec)
            {
                guards.Add(new DslCondition(DslCondition.Compare, turnsSpec));
            }

            // 事件卡属性过滤（可多维——**扁平**合取，避免嵌套 all）。
            foreach (var filter in eventCardFilters)
            {
                guards.Add(new DslCondition(DslCondition.EventCardFilter, filter));
            }

            guards.Add(PayloadGuardOf(listenTemplate, listen.RawText));
            var sideCondition = CombineGuards(guards.ToArray());

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
        DslCondition? namedGuard = null;
        if (ast.Trigger is { Kind: TriggerSyntaxKind.Named } named)
        {
            foreach (var ev in named.Events)
            {
                if (EventTemplates.TryGetValue(ev.RawText, out var template))
                {
                    templates.Add((template, ev.Span));

                    // S3：「揭示：X」＝该卡被揭示时——**自指守卫**（被揭示者==宿主；不泛触发于同场
                    // 其它单位的揭示——unit.revealed 载荷 {Unit} 与 damage_dealt 同构）。
                    if (string.Equals(template, "reveal_basic", StringComparison.Ordinal))
                    {
                        namedGuard = new DslCondition(DslCondition.PayloadSelf, "Unit");
                    }
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

        if (!TryBuildOps(original, ast, out var ops, out var failures, namedGuard))
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

    /// <summary>
    /// **数值比较 → 条件规范串**（E1-57）：只产出**可求值**的形态，其余返回 null（交回 `raw` 占位）。
    /// <para>左度量：`友方单位数`（计数）／`剩余指挥点数`（视角玩家资源）／`友方总部防御力`（己方 HQ 属性）。
    /// 右操作数：`#n` 或**同类度量在另一方**（`敌方单位`；敌方 HQ 属性因缺"对手对象"而**不接**，
    /// 与求值层保持一致 ⇒ 侧向引用为 `#n`/计数时才产出）。</para>
    /// </summary>
    private static string? ComparisonSpecOf(ComparisonPhrase? comparison)
    {
        if (comparison is null)
        {
            return null;
        }

        var left = MeasureSpec(comparison.LeftFilters, comparison.LeftSide, comparison.LeftZone, comparison.LeftIsCount);
        if (left is null)
        {
            return null;
        }

        string right;
        if (comparison.RightValue is { } literal)
        {
            right = "#" + literal;
        }
        else if (comparison.LeftIsCount && comparison.RightSide is { } rightSide)
        {
            right = $"count=s={rightSide}";
        }
        else
        {
            return null; // 侧向引用形态（如 `大于敌方总部`）暂不可求值 ⇒ 交回占位。
        }

        return $"{left}:{comparison.Op}:{right}";
    }

    /// <summary>
    /// **目标阈值**比较（E1-57）：左度量是**可被选中的卡的属性**（`攻击力`／`防御力`／`花费`）且右操作数是数值
    /// ⇒ 作为选择器过滤（`(字段, 算子, 取值)`）；"计数/区域"形态（`友方单位数`／`友方总部防御力`）→ null（走全局条件）。
    /// </summary>
    private static (string Field, string Op, int Value)? ThresholdOf(ComparisonPhrase? comparison)
    {
        if (comparison is null || comparison.LeftIsCount || comparison.LeftZone is not null
            || comparison.RightValue is not { } value)
        {
            return null;
        }

        var attribute = FindFilter(comparison.LeftFilters, FilterKind.Attribute);
        var objectNoun = FindFilter(comparison.LeftFilters, FilterKind.Object);
        var field = attribute ?? (objectNoun == "opCost" ? "opCost" : null);
        return field is null ? null : (field, comparison.Op, value);
    }

    /// <summary>度量规范（`count:`／`points:`／`stat:`；不可表达＝null）。</summary>
    private static string? MeasureSpec(
        IReadOnlyList<FilterPhrase> filters, string? side, string? zone, bool isCount)
    {
        if (isCount)
        {
            return $"count=s={side ?? "friendly"}";
        }

        var objectNoun = FindFilter(filters, FilterKind.Object);
        if (objectNoun == "point")
        {
            return "points=s=friendly"; // `剩余指挥点数`＝视角玩家资源
        }

        var attribute = FindFilter(filters, FilterKind.Attribute);
        return attribute is not null && zone == "hq" && side is not null
            ? $"stat=f={attribute};s={side};z=hq"
            : null;
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

        // E1-41：期限（`直到回合结束`／`本回合…`／`直到下个友方回合开始`）——**整效果**颗粒度（子句切分后
        // 「本回合」往往自成前置子句，逐子句识别会漏）；取值见 Dsl.Untils。
        var effectText = RawOf(original, ast.Span);
        var until = UntilOf(effectText);

        // E1-56：**静态/持续**文本（无触发且无期限）⇒ `具有 ±N` 走**光环**（受益随进出/位置实时重算）；
        // 触发体内的一次性动作仍走修饰器（`buff`/`costMod`，可带期限）。
        var staticText = ast.Trigger is null && until is null;
        var excludeSelf = effectText.Contains("其他", StringComparison.Ordinal)
                          || effectText.Contains("其它", StringComparison.Ordinal);

        // 前置扫：**纯条件子句**（无动作）＝整效果的条件——与既有条件（监听守卫等）**合取**累积
        // （任何一环都不丢失：`guard && 条件`；单条件时＝原样）——
        // E1-57：可识别的**数值比较**（`若友方单位数不小于 3`）⇒ **真实条件**（求值是纯函数）；
        // S3：「若是友方回合／敌方回合」⇒ **回合归属类真实条件**（求值挂钩既有回合归属判定面）；
        // 其余（`若有友方动员单位`／`若上回合没有被攻击`…）⇒ `raw` 占位（`if (false)`）。
        var defaultCondition = effectCondition;
        foreach (var clause in ast.Clauses)
        {
            if (clause.Actions.Count > 0)
            {
                continue;
            }

            if (ComparisonSpecOf(clause.Comparison) is { } spec)
            {
                defaultCondition = CombineGuards(defaultCondition, new DslCondition(DslCondition.Compare, spec));
            }
            else if (TurnOwnershipOf(RawOf(original, clause.Span)) is { } turnOwner)
            {
                defaultCondition = CombineGuards(defaultCondition, new DslCondition(DslCondition.TurnOwner, turnOwner));
            }
            else if (clause.Condition is not null)
            {
                defaultCondition = CombineGuards(
                    defaultCondition, new DslCondition(DslCondition.RawKind, clause.Condition.RawText));
            }
        }

        foreach (var clause in ast.Clauses)
        {
            // E1-57：同子句的**目标阈值**比较（`消灭 1 个花费不大于 3 的单位`）⇒ 落到**选择器过滤**
            // （不再是占位条件——占位＝`if (false)`＝效果永不执行）。
            var threshold = ThresholdOf(clause.Comparison);
            var condition = clause.Condition is null
                ? defaultCondition
                : threshold is not null
                    ? null
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

            for (var actionIndex = 0; actionIndex < clause.Actions.Count; actionIndex++)
            {
                var action = clause.Actions[actionIndex];
                var clauseText = RawOf(original, clause.Span);

                // 序列③：**宾语窗口**＝本动作之后、下一动作之前（多动作子句下防"后位动作的宾语"溢出到前位动作）；
                // 无后续动作＝窗口开至无穷（既有形态）。
                var keywordWindowEnd = actionIndex + 1 < clause.Actions.Count
                    ? clause.Actions[actionIndex + 1].Span.Start
                    : int.MaxValue;

                if (string.Equals(action.VerbKey, "state", StringComparison.Ordinal))
                {
                    if (TryMapStateAction(
                        action, clause.Target, effectText, staticText, excludeSelf, threshold, out var stateOps, out var stateReason))
                    {
                        foreach (var stateOp in stateOps)
                        {
                            if (until is not null && stateOp.Op is not ("buff" or "costMod"))
                            {
                                // 期限不可承载（如 grant 无期限面）⇒ 不产"永久"错误效果，改留痕 needsCsx。
                                ops.Add(WithCondition(
                                    new DslOp(DslOpRegistry.NeedsCsxOpName, script: clauseText), condition));
                                continue;
                            }

                            ops.Add(WithCondition(WithUntil(stateOp, until), condition));
                        }

                        continue;
                    }

                    _ = stateReason;
                    ops.Add(WithCondition(new DslOp(DslOpRegistry.NeedsCsxOpName, script: clauseText), condition));
                    continue;
                }

                if (TryMapAction(action, clause.Target, threshold, keywordWindowEnd, out var mappedOps, out var reason))
                {
                    // 期限只对**可承载期限**的 op 有意义（buff/costMod）；其余（如 grant 无期限面）
                    // 若带期限 ⇒ 不产"永久"错误效果，改留痕 needsCsx（E1-41）。
                    // needsCsx 本身已是留痕 op（非可执行）——不参与替换（保持其原脚本）。
                    var untilReplaced = false;
                    foreach (var mappedOp in mappedOps)
                    {
                        if (until is not null
                            && mappedOp.Op is not ("buff" or "costMod" or DslOpRegistry.NeedsCsxOpName))
                        {
                            ops.Add(WithCondition(new DslOp(DslOpRegistry.NeedsCsxOpName, script: clauseText), condition));
                            untilReplaced = true;
                            continue;
                        }

                        ops.Add(WithCondition(WithUntil(mappedOp, until), condition));
                    }

                    // S3→序列③：`获得`类动作的**已识别部分完整落地**后，宾语区仍有未识别词（如「获得奋战和炮击」
                    // 的「炮击」——词条未实现）⇒ **局部显式留痕**（needsCsx 原文保留——不静默、不误跑）。
                    // 范围限 `gain`（其余动作的动词后词法不在此口径）。序列③回补：词条「冲击」经词法补入后
                    // 『获得冲击』不再触发本留痕（「使 1 个老兵单位获得奋战和冲击」→ **两个 grant 并列**、
                    // 无 needsCsx 残留）；留痕机制**保留**——其它未识别词的残留仍显式留痕（仅输入集合变化）。
                    if (!untilReplaced
                        && string.Equals(action.VerbKey, "gain", StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(action.ObjectRaw))
                    {
                        ops.Add(WithCondition(
                            new DslOp(DslOpRegistry.NeedsCsxOpName, script: action.ObjectRaw), condition));
                    }

                    continue;
                }

                // 「op 无法解析」（非句式）⇒ **原文保留进 DSL** 并标记"需要 csx 实现"（用户口径）——
                // 不产未解析记录（句式层失败才计未解析）。
                _ = reason;
                ops.Add(WithCondition(new DslOp(DslOpRegistry.NeedsCsxOpName, script: clauseText), condition));
            }
        }

        if (ops.Count == 0)
        {
            ok = false;

            // R8 修正（E1-38）：**整效果颗粒度**上不得"既无效果、又无诊断"。
            // 此前纯目标声明/纯条件子句在 `clause.Actions.Count == 0` 分支里 continue（不计失败），
            // 而此处只置 ok=false **不补记录** ⇒ 调用方 `unresolved.AddRange(failures)` 加了 0 条
            // ⇒ 整卡**静默丢弃**（语料实测 295 条：既无效果、也无未解析记录——违反 R8「不静默丢弃」）。
            // 现：以**整效果**为粒度补一条诊断（不改变"子句颗粒度不计失败"的既定口径——E1-20 的意图保留）。
            if (failures.Count == 0)
            {
                var declaration = FindTargetDeclaration(ast);
                failures.Add(declaration is null
                    ? Fail(original, ast.Clauses.Count > 0 ? ast.Clauses[0].Span : ast.Span,
                        "未产出任何可执行效果（整条均为条件/无动作短语）。")
                    : Fail(original, declaration.Span,
                        "未产出任何可执行效果（整条均为目标声明/回指，无动作短语）。"));
            }
        }

        return ok;
    }

    /// <summary>取首个"纯目标声明"子句（用于整效果无产出时的诊断定位；无＝null）。</summary>
    private static ClauseNode? FindTargetDeclaration(EffectAst ast)
    {
        foreach (var clause in ast.Clauses)
        {
            if (clause.Actions.Count == 0 && clause.IsTargetDeclaration)
            {
                return clause;
            }
        }

        return null;
    }

    private static DslOp WithCondition(DslOp op, DslCondition? condition) =>
        condition is null
            ? op
            : new DslOp(
                op.Op, op.Target, op.Filter, op.Amount, op.Count, op.Attack, op.Defense,
                op.Keyword, op.Zone, op.Script, condition, op.Name, op.Nested, op.Until);

    /// <summary>引号内容是否"本身是一段效果"（含触发界定符或句号 ⇒ 判为内嵌效果文本，而非卡名）。</summary>
    private static bool LooksLikeNestedEffect(string quoted) =>
        quoted.Contains('：') || quoted.Contains(':') || quoted.Contains('。');

    private bool TryMapAction(
        ActionPhrase action,
        TargetPhrase? target,
        (string Field, string Op, int Value)? threshold,
        int keywordWindowEnd,
        out List<DslOp> mappedOps,
        out string? reason)
    {
        mappedOps = null!;
        reason = null;
        var selector = BuildSelector(target, action, threshold: threshold);

        switch (action.VerbKey)
        {
            case "damage":
                if (action.Payload is null)
                {
                    reason = "造成伤害缺少数值。";
                    return false;
                }

                mappedOps = new List<DslOp> { new DslOp("damage", selector, amount: action.Payload.Int) };
                return true;

            case "draw":
                if (action.Payload is null)
                {
                    reason = "抽牌缺少张数。";
                    return false;
                }

                mappedOps = new List<DslOp> { new DslOp("draw", selector, count: action.Payload.Int) };
                return true;

            case "gain":
                // **嵌套效果**（任务 2）：引号里的内容"本身是一段效果"（如 `获得：“亡计：…”`）⇒ 递归解析并以内嵌效果承载。
                var quoted = FindFilterRaw(target, FilterKind.Name);
                if (quoted is not null && _innerParser is not null && LooksLikeNestedEffect(quoted))
                {
                    var inner = _innerParser(quoted);
                    if (inner.Effects.Count > 0)
                    {
                        mappedOps = new List<DslOp> { new DslOp(DslOpRegistry.NestedOpName, nested: inner.Effects) };
                        return true;
                    }
                }

                // 「获得」的含义**取决于其后的 token**（用户口径）：属性 → buff；花费 → costMod；
                // 指挥点槽 → gainSlot（额外获得 n 个指挥点槽）；指挥点 → gainPoint（获得 n 个指挥点）；
                // 词条 → grant；其余 → 显式失败。
                var attribute = FindFilter(target, FilterKind.Attribute);
                if (attribute is not null && action.Payload is not null)
                {
                    mappedOps = new List<DslOp>
                    {
                        attribute == "attack"
                            ? new DslOp("buff", selector, attack: action.Payload.Int)
                            : new DslOp("buff", selector, defense: action.Payload.Int),
                    };
                    // 序列③：混合宾语（属性 ＋ 词条）下**未落地词条**的显式留痕（不静默）。
                    AppendKeywordTraces(mappedOps, target, action, keywordWindowEnd);
                    return true;
                }

                // 「获得 +1+1」＝两个数：第一＝攻击力、第二＝防御力（无属性名词时的形态）。
                if (action.Payload is not null && action.SecondaryPayload is not null && attribute is null)
                {
                    mappedOps = new List<DslOp>
                    {
                        new DslOp("buff", selector, attack: action.Payload.Int, defense: action.SecondaryPayload.Int),
                    };
                    // 序列③：混合宾语（数值 ＋ 词条）下**未落地词条**的显式留痕（不静默）。
                    AppendKeywordTraces(mappedOps, target, action, keywordWindowEnd);
                    return true;
                }

                var objectNoun = FindFilter(target, FilterKind.Object);
                if (objectNoun == "opCost" && action.Payload is not null)
                {
                    mappedOps = new List<DslOp> { new DslOp("costMod", selector, amount: action.Payload.Int) };
                    return true;
                }

                if (objectNoun == "pointSlot")
                {
                    if (action.Payload is null)
                    {
                        reason = "额外获得指挥点槽缺少数值（需『获得 N 个指挥点槽』形式）。";
                        return false;
                    }

                    mappedOps = new List<DslOp> { new DslOp("gainSlot", selector, amount: action.Payload.Int) };
                    return true;
                }

                if (objectNoun == "point")
                {
                    if (action.Payload is null)
                    {
                        reason = "获得指挥点缺少数值（需『获得 N 个指挥点』形式）。";
                        return false;
                    }

                    mappedOps = new List<DslOp> { new DslOp("gainPoint", selector, amount: action.Payload.Int) };
                    return true;
                }

                // 序列③（回补＋泛化）：宾语区词条**逐个授予**——「获得 X 和 Y」（X、Y 均已识别词条）＝
                // 两个 grant 并列（一 op 一词条；不采用"单授予携带多词条"）。宾语（**动词之后**）词条优先——
                // 如「使 1 个老兵单位获得奋战和冲击」的「老兵」是目标限定、「奋战」「冲击」是宾语；
                // 宾语区无词条时才回退全量过滤短语**首个**（既有形态兼容）。
                // 混合形态（已识别＋未识别并存）＝已识别逐个授予 ＋ 未识别部分残留 needsCsx（调用方局部留痕）。
                var objectKeywords = KeywordsOf(ObjectFilters(target, action, keywordWindowEnd));
                if (objectKeywords.Count == 0 && FindFilter(target, FilterKind.Keyword) is { } fallbackKeyword)
                {
                    objectKeywords.Add(fallbackKeyword);
                }

                if (objectKeywords.Count > 0)
                {
                    mappedOps = new List<DslOp>(objectKeywords.Count);
                    foreach (var keyword in objectKeywords)
                    {
                        mappedOps.Add(new DslOp("grant", selector, keyword: keyword));
                    }

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

                    mappedOps = new List<DslOp> { new DslOp("loseSlot", selector, amount: action.Payload.Int) };
                    return true;
                }

                if (lostNoun == "point")
                {
                    if (action.Payload is null)
                    {
                        reason = "失去指挥点缺少数值（需『失去 N 个指挥点』形式）。";
                        return false;
                    }

                    mappedOps = new List<DslOp> { new DslOp("losePoint", selector, amount: action.Payload.Int) };
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

                mappedOps = new List<DslOp> { new DslOp("move", selector, zone: target.ZoneValue) };
                return true;

            case "addToHand":
            case "shuffleIn":
                var cardName = FindFilterRaw(target, FilterKind.Name);
                if (string.IsNullOrWhiteSpace(cardName))
                {
                    reason = $"'{action.VerbRaw}' 缺少引号卡名（需 “卡名” 形式）。";
                    return false;
                }

                mappedOps = new List<DslOp>
                {
                    new DslOp(action.VerbKey, selector, count: action.Payload?.Int ?? 1, name: cardName),
                };
                return true;

            case "pin":
                mappedOps = new List<DslOp> { new DslOp("pin", selector) };
                return true;

            case "silence":
                mappedOps = new List<DslOp> { new DslOp("silence", selector) };
                return true;

            case "destroy":
                // 消灭＝**游戏层死亡链**（亡计/词条注销/修饰清理/card.died）；
                // 与总线 card.destroyed（内存销毁，可能只是弃牌）语义不同——不容混用。
                mappedOps = new List<DslOp> { new DslOp("destroy", selector) };
                return true;

            case "upgrade":
                // S3：升为老兵（csx 对接 EffectRuntime.UpgradeAsync——S1 冻结的升级发动公共路径）。
                mappedOps = new List<DslOp> { new DslOp("upgrade", selector) };
                return true;

            case "reveal":
                // S3：揭示（管理动作域——csx 候选来源经 CovertRules.CollectCovertUnits、执行经
                // EffectRuntime.RevealAsync 唯一标准口）。「揭示 1 个隐蔽单位」的限定词位于动词**之后**
                // ⇒ 以**全量过滤短语**为限定面（动词后限定词＝该动作的目标——与「获得…」的动词后宾语区分）。
                var revealSelector = BuildSelector(target, action: null, threshold: threshold);
                mappedOps = new List<DslOp> { new DslOp("reveal", revealSelector, count: action.Payload?.Int ?? 1) };
                return true;

            default:
                reason = $"未知动作：'{action.VerbRaw}'。";
                return false;
        }
    }

    /// <summary>
    /// 「具有」类状态/属性描述（E1-41）：动词**之后**的过滤短语＝宾语（属性/词条/花费），可含多项
    /// （`+1 攻击力和奋战`）⇒ 展开为多个 op。
    /// <para>**保守守卫**：整效果文本含**计数/对抗/相位**语式（`每有`/`对抗`/`回合中`/`每回合`）时不做映射——
    /// 那些语式映射成"单次无条件"即为语义错误（交由 `needsCsx` 留痕）。</para>
    /// </summary>
    private static bool TryMapStateAction(
        ActionPhrase action,
        TargetPhrase? target,
        string effectText,
        bool staticText,
        bool excludeSelf,
        (string Field, string Op, int Value)? threshold,
        out List<DslOp> ops,
        out string? reason)
    {
        ops = new List<DslOp>();
        reason = null;

        if (HasUnmappableStateModifier(effectText))
        {
            reason = "持续态含计数/对抗/相位条件（暂不可表达）。";
            return false;
        }

        // E1-52（**正确性修正**）：**目标区的未识别限定词**（`相邻陆军`／`本单位左侧所有单位`／`敌方指令`／
        // `谢尔曼`／`受伤单位`／`手牌中的所有单位`…）一旦存在，目标就**不可精确表达**——
        // 静默丢弃它们会产出"打到宿主自己/全场"的**错误效果** ⇒ 一律拒绝（落 needsCsx）。
        if (target?.HasUnrecognizedQualifier == true)
        {
            reason = "状态描述的目标区含未识别限定词——目标不可精确表达。";
            return false;
        }

        // 宾语区的未识别词（`具有 山地` 等）同理拒绝。
        if (!string.IsNullOrWhiteSpace(action.ObjectRaw))
        {
            reason = $"状态描述含未识别宾语（'{action.ObjectRaw}'）——不可表达。";
            return false;
        }

        var selector = BuildSelector(target, action, excludeSelf, threshold);
        var objects = ObjectFilters(target, action);

        // `具有 +1+1`（两个数、无属性名词）＝攻击力/防御力。
        if (action.Payload is not null && action.SecondaryPayload is not null
            && FindFilter(objects, FilterKind.Attribute) is null)
        {
            ops.Add(new DslOp("buff", selector, attack: action.Payload.Int, defense: action.SecondaryPayload.Int));
            return true;
        }

        // E1-52：**两个数值 ＋ 属性名词**＝歧义（首个数值多属限定词，如 `防御力为 1 的友方步兵具有 +2 攻击力`）
        // ⇒ 拒绝（避免把限定值当成增益量）。
        if (action.Payload is not null && action.SecondaryPayload is not null)
        {
            reason = "状态描述含两个数值（限定值与增益量歧义）——不可表达。";
            return false;
        }

        foreach (var phrase in objects)
        {
            switch (phrase.Kind)
            {
                case FilterKind.Attribute when action.Payload is not null:
                    ops.Add(string.Equals(phrase.Value, "attack", StringComparison.Ordinal)
                        ? new DslOp("buff", selector, attack: action.Payload.Int)
                        : new DslOp("buff", selector, defense: action.Payload.Int));
                    break;
                case FilterKind.Object when string.Equals(phrase.Value, "opCost", StringComparison.Ordinal)
                                           && action.Payload is not null:
                    ops.Add(new DslOp("costMod", selector, amount: action.Payload.Int));
                    break;
                case FilterKind.Keyword when !string.IsNullOrWhiteSpace(phrase.Value):
                    ops.Add(new DslOp("grant", selector, keyword: phrase.Value));
                    break;
                default:
                    break; // 未支持宾语（未实现词条/阈值等）→ 不产 op（由下方"无产出"判定兜底）
            }
        }

        if (ops.Count == 0)
        {
            reason = "『具有』后无法判定的宾语。";
            return false;
        }

        // E1-56：静态文本 ⇒ `buff`/`costMod` 改走**光环**（受益集合随进出/位置实时重算）。
        if (staticText)
        {
            ops = ToAuraOps(ops);
        }

        return true;
    }

    /// <summary>
    /// 把"持续态"的 `buff`/`costMod` 转换为**光环** op（E1-56）：`buff(atk)` ⇒ `aura(field=attack)`、
    /// `buff(def)` ⇒ `aura(field=defense)`、`buff(atk,def)` ⇒ **两条**、`costMod` ⇒ `aura(field=opCost)`；
    /// 其余（如 `grant` —— 词条无光环面）**原样保留**（一次性授予，登记为待改进）。
    /// </summary>
    private static List<DslOp> ToAuraOps(IReadOnlyList<DslOp> ops)
    {
        var result = new List<DslOp>();
        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case "buff" when op.Attack is { } attackValue:
                    result.Add(AuraOp(op, "attack", attackValue));
                    if (op.Defense is { } defenseValue)
                    {
                        result.Add(AuraOp(op, "defense", defenseValue));
                    }

                    break;
                case "buff" when op.Defense is { } onlyDefense:
                    result.Add(AuraOp(op, "defense", onlyDefense));
                    break;
                case "costMod" when op.Amount is { } amount:
                    result.Add(AuraOp(op, "opCost", amount));
                    break;
                default:
                    result.Add(op);
                    break;
            }
        }

        return result;
    }

    private static DslOp AuraOp(DslOp source, string field, int amount) =>
        new("aura", source.Target, source.Filter, amount: amount, field: field);

    /// <summary>
    /// 持续态守卫（E1-41）：**计数/对抗/相位**语式——映射成"单次无条件"即语义错误，故整效果不做映射。
    /// </summary>
    private static bool HasUnmappableStateModifier(string text) =>
        text.Contains("每有", StringComparison.Ordinal)
        || text.Contains("每拥有", StringComparison.Ordinal)
        || text.Contains("每回合", StringComparison.Ordinal)
        || text.Contains("对抗", StringComparison.Ordinal)
        || text.Contains("回合中", StringComparison.Ordinal)
        || text.Contains("交战", StringComparison.Ordinal)
        || text.Contains("对战时", StringComparison.Ordinal);
    // 注：`其他/其它`（排除自身）已由 E1-56 的过滤维度 `excludeSelf` 承载（不再是"不可表达"）。

    /// <summary>整效果文本里的**期限**语式 → <see cref="Untils"/> 取值（无＝null）。</summary>
    private static string? UntilOf(string text)
    {
        if (text.Contains("直到下个友方回合开始", StringComparison.Ordinal)
            || text.Contains("直到你的下个回合开始", StringComparison.Ordinal)
            || text.Contains("直到下个回合开始", StringComparison.Ordinal))
        {
            return Untils.NextOwnerTurnStart;
        }

        if (text.Contains("直到回合结束", StringComparison.Ordinal)
            || text.Contains("直到本回合结束", StringComparison.Ordinal)
            || text.Contains("本回合", StringComparison.Ordinal))
        {
            return Untils.TurnEnd;
        }

        return null;
    }

    /// <summary>
    /// 监听短语中的「在场上的第 N 回合」限定（S3；V1 完整档构成）→ **turns 度量条件规范串**
    /// （`turns=s=self:gte:#N`；"第三回合开始时"＝在场回合数**达到** 3——gte 读法，升级幂等使超限重复检查无害）。
    /// 未命中＝null（保持既有路径）。
    /// </summary>
    private static string? InPlayTurnSpecOf(string rawText)
    {
        const string prefix = "在场上的第";
        var start = rawText.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var rest = rawText[(start + prefix.Length)..];
        var end = rest.IndexOf("回合", StringComparison.Ordinal);
        if (end <= 0)
        {
            return null;
        }

        return TryChineseNumber(rest[..end], out var turnNumber)
            ? $"turns=s=self:gte:#{turnNumber}"
            : null;
    }

    /// <summary>中文数词（一~十，含"两"）或阿拉伯数字 → 整数；其余＝false。</summary>
    private static bool TryChineseNumber(string text, out int value)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["一"] = 1, ["二"] = 2, ["两"] = 2, ["三"] = 3, ["四"] = 4, ["五"] = 5,
            ["六"] = 6, ["七"] = 7, ["八"] = 8, ["九"] = 9, ["十"] = 10,
        };

        var trimmed = text.Trim();
        return map.TryGetValue(trimmed, out value) || int.TryParse(trimmed, out value);
    }

    /// <summary>
    /// 「若是友方回合／敌方回合」子句（S3）→ **回合归属类条件**取值（`friendly`／`enemy`）。未命中＝null。
    /// </summary>
    private static string? TurnOwnershipOf(string clauseText)
    {
        if (clauseText.Contains("若是友方回合", StringComparison.Ordinal))
        {
            return "friendly";
        }

        if (clauseText.Contains("若是敌方回合", StringComparison.Ordinal))
        {
            return "enemy";
        }

        return null;
    }

    /// <summary>补写期限（其它字段原样保留）。</summary>
    private static DslOp WithUntil(DslOp op, string? until) =>
        until is null
            ? op
            : new DslOp(
                op.Op, op.Target, op.Filter, op.Amount, op.Count, op.Attack, op.Defense,
                op.Keyword, op.Zone, op.Script, op.Condition, op.Name, op.Nested, until);

    private static DslSelector? BuildSelector(
        TargetPhrase? target,
        ActionPhrase? action = null,
        bool excludeSelf = false,
        (string Field, string Op, int Value)? threshold = null)
    {
        if (target is null)
        {
            return null;
        }

        // E1-41：**动词之后**的过滤短语是"动作的宾语"（如 `具有 +1 攻击力`、`获得闪击`），
        // 不是目标限定词——否则会被当成 target 过滤（`获得闪击` 会变成"选一个带闪击的单位"）。
        var qualifiers = QualifierFilters(target, action);
        var hasQualifier = target.QuantifierSel is not null || target.SideValue is not null
                           || target.ZoneValue is not null || qualifiers.Count > 0 || excludeSelf
                           || threshold is not null;
        if (!hasQualifier)
        {
            return null;
        }

        var unitType = FindFilter(qualifiers, FilterKind.UnitType);
        var keyword = FindFilter(qualifiers, FilterKind.Keyword);
        var filter = unitType is null && keyword is null && !excludeSelf && threshold is null
            ? null
            : new DslFilter(
                unitType, keyword, excludeSelf: excludeSelf,
                thresholdField: threshold?.Field, thresholdOp: threshold?.Op, thresholdValue: threshold?.Value);

        return new DslSelector(target.QuantifierSel ?? "one", target.SideValue, target.ZoneValue, filter, null);
    }

    /// <summary>目标限定词（过滤短语中位于**动词之前**者；动词之后者＝动作宾语——E1-41）。</summary>
    private static IReadOnlyList<FilterPhrase> QualifierFilters(TargetPhrase target, ActionPhrase? action)
    {
        if (action is null)
        {
            return target.Filters;
        }

        var result = new List<FilterPhrase>();
        foreach (var filter in target.Filters)
        {
            if (filter.Span.Start < action.Span.Start)
            {
                result.Add(filter);
            }
        }

        return result;
    }

    /// <summary>
    /// 动作宾语（过滤短语中位于**动词之后**者——E1-41；可选**窗口上界**——多动作子句下不含后位动作的宾语）。
    /// </summary>
    private static IReadOnlyList<FilterPhrase> ObjectFilters(TargetPhrase? target, ActionPhrase action, int windowEnd = int.MaxValue)
    {
        var result = new List<FilterPhrase>();
        if (target is null)
        {
            return result;
        }

        foreach (var filter in target.Filters)
        {
            if (filter.Span.Start >= action.Span.End && filter.Span.Start < windowEnd)
            {
                result.Add(filter);
            }
        }

        return result;
    }

    /// <summary>
    /// 过滤短语中的**词条**（FilterKind.Keyword）归类值清单（保序——序列③「逐词条授予」的宾语收集；
    /// null/空白值剔除）。
    /// </summary>
    private static List<string> KeywordsOf(IReadOnlyList<FilterPhrase> filters)
    {
        var result = new List<string>();
        foreach (var filter in filters)
        {
            if (filter.Kind == FilterKind.Keyword && !string.IsNullOrWhiteSpace(filter.Value))
            {
                result.Add(filter.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// 序列③：混合宾语（属性/数值 ＋ 词条）下**未落地词条**的显式留痕——`needsCsx` 原文保留
    /// （不静默、不误跑）。本单泛化的完整落地（逐词条授予）只覆盖"宾语区全部为词条"的形态；
    /// 属性/数值混合形态的完整映射（含连接词/选择语义）留后续批次——未落地词条一律**保持留痕**
    /// （历史对照：识别前因"未识别"由局部留痕捕捉〔script＝未识别原文〕；识别后经本函数继续留痕，脚本＝词条原文）。
    /// </summary>
    private static void AppendKeywordTraces(List<DslOp> ops, TargetPhrase? target, ActionPhrase action, int keywordWindowEnd)
    {
        foreach (var filter in ObjectFilters(target, action, keywordWindowEnd))
        {
            if (filter.Kind == FilterKind.Keyword && !string.IsNullOrWhiteSpace(filter.Value))
            {
                ops.Add(new DslOp(DslOpRegistry.NeedsCsxOpName, script: filter.RawText));
            }
        }
    }

    /// <summary>在过滤短语清单中按维度取值（E1-41 重载：供限定词/宾语分离后使用）。</summary>
    private static string? FindFilter(IReadOnlyList<FilterPhrase> filters, FilterKind kind)
    {
        foreach (var filter in filters)
        {
            if (filter.Kind == kind)
            {
                return filter.Value;
            }
        }

        return null;
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
    private static string? ResolveListenTemplate(string rawText, string rawOriginal, out IReadOnlyList<string> eventCardFilters)
    {
        eventCardFilters = FindEventCardFilters(rawText, rawOriginal);

        // S3：升为老兵（`…升为老兵时`——hook <c>unit.upgraded</c>；S1 冻结信号名）。置于最前——
        // 更具体者优先（「升为老兵」短语与其它分支关键词均不共现）。
        if (rawText.Contains("升为老兵", StringComparison.Ordinal))
        {
            return "veteran_basic";
        }

        // S3：被揭示（`…被揭示时`——hook <c>unit.revealed</c>；S2 冻结信号名）。「时/后」后缀经既有机制自然吸收。
        if (rawText.Contains("被揭示", StringComparison.Ordinal))
        {
            return "reveal_basic";
        }

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

        // E1-47 新信号：**造成伤害**（来源侧）——`本单位造成伤害时/后`／`本单位对敌方总部造成伤害时/后`／`…对战伤害…`。
        // 主体须为"本单位"（归属面由 PayloadGuardOf 表达；其余主体由调用方拒绝映射——不泛触发）。
        if (rawText.Contains("造成", StringComparison.Ordinal) && rawText.Contains("伤害", StringComparison.Ordinal))
        {
            return "damage_dealt_basic";
        }

        // E1-47 归属：**消灭**（击杀者）——`本单位消灭 N 个单位时/后`；`被消灭` 已在最前分支处理（受动面）。
        if (rawText.Contains("消灭", StringComparison.Ordinal))
        {
            return "killed_basic";
        }

        // E1-39 新信号：交战并存活（"本单位交战并存活后"/"本单位对战并存活后"）。
        // 置于"攻击/行动"之前——更具体者优先（"攻击并存活"应判为存活）。
        if (rawText.Contains("存活", StringComparison.Ordinal))
        {
            return "combat_survived_basic";
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

        // E1-38 修正：归一后文本里数量词与"牌"之间**夹数字**（"抽 1 张牌" → 归一为 "抽1张牌"），
        // 原判据 `Contains("抽牌")` 对其**恒不命中** ⇒ 改为"抽 … 牌"共现判据。
        if (rawText.Contains("抽", StringComparison.Ordinal) && rawText.Contains("牌", StringComparison.Ordinal))
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

        // E1-53：**反制触发**（`友方反制触发时`／`触发敌方反制时`）——载荷 {Card, Player} ⇒ 走**卡面**归属过滤。
        if (rawText.Contains("反制", StringComparison.Ordinal))
        {
            return "counter_basic";
        }

        // E1-42/E1-54：`使用<属性>牌时` ⇒ played_basic ＋ **事件卡属性守卫**（真实 csx；见 FindEventCardFilters）。
        if (eventCardFilters.Count > 0)
        {
            return "played_basic";
        }

        // E1-39：指挥点槽增减（`slot.gained`/`slot.lost` 已有信号；载荷只有 Player ⇒ 走**玩家面**归属过滤）。
        if (rawText.Contains("指挥点槽", StringComparison.Ordinal))
        {
            return rawText.Contains("失去", StringComparison.Ordinal) ? "slot_lost_basic" : "slot_gained_basic";
        }

        return null;
    }

    /// <summary>
    /// **事件卡属性过滤**（E1-42 乙 → **E1-54 甲**）：`使用/打出 …<属性>牌` ⇒ 返回 `维度:取值` 清单
    /// （多维度＝**合取**，如"英国指令"＝`faction:Britain` ＋ `category:Command`）。
    /// <para>维度：`keyword`（词条）／`tag`（子类别）／`category`（卡类型）／`faction`（阵营）／`name`（**引号卡名**，
    /// 取自**原文切片**——`listen.RawText` 已剥引号）。</para>
    /// </summary>
    private static IReadOnlyList<string> FindEventCardFilters(string rawText, string rawOriginal)
    {
        var filters = new List<string>();
        if (!rawText.Contains("使用", StringComparison.Ordinal) && !rawText.Contains("打出", StringComparison.Ordinal))
        {
            return filters;
        }

        foreach (var (lexeme, filter) in EventCardFilterLexicon)
        {
            if (rawText.Contains(lexeme, StringComparison.Ordinal))
            {
                filters.Add(filter);
            }
        }

        // 引号卡名：`使用“计划”时`（原文切片保留引号）⇒ `name:计划`。
        foreach (var quote in new[] { ('“', '”'), ('「', '」'), ('"', '"') })
        {
            var start = rawOriginal.IndexOf(quote.Item1);
            var end = start < 0 ? -1 : rawOriginal.IndexOf(quote.Item2, start + 1);
            if (start >= 0 && end > start + 1)
            {
                var name = rawOriginal[(start + 1)..end];
                if (!filters.Any(item => item.StartsWith("name:", StringComparison.Ordinal)))
                {
                    filters.Add("name:" + name);
                }
            }
        }

        return filters;
    }

    /// <summary>
    /// 事件卡**属性词表**（E1-54；值为 `维度:取值`——取值经枚举白名单校验，见 <c>EffectCompiler.EnumValue</c>）。
    /// </summary>
    private static readonly (string Lexeme, string Filter)[] EventCardFilterLexicon =
    {
        // 词条（KeywordIds 标识）
        ("情报", "keyword:" + Orc.Game.Cards.KeywordIds.Intelligence),
        // 子类别 tag（TagData 开放集合——值即中文 tag 本身）
        ("海军", "tag:海军"),
        ("协力", "tag:协力"),
        // 卡类型（CardCategory）
        ("指令", "category:Command"),
        // 阵营（Faction）
        ("英国", "faction:Britain"),
        ("日本", "faction:Japan"),
        ("美国", "faction:USA"),
        ("苏联", "faction:Soviet"),
        ("德国", "faction:Germany"),
        ("法国", "faction:France"),
        ("中立", "faction:Neutral"),
    };

    /// <summary>多个守卫条件的合成：全 null＝null／单条＝原样／多条＝**合取**（`all`）。</summary>
    private static DslCondition? CombineGuards(params DslCondition?[] guards)
    {
        var present = new List<DslCondition>();
        foreach (var guard in guards)
        {
            if (guard is not null)
            {
                present.Add(guard);
            }
        }

        return present.Count switch
        {
            0 => null,
            1 => present[0],
            _ => new DslCondition(DslCondition.AllKind, null, present),
        };
    }

    /// <summary>
    /// **载荷字段守卫**（E1-47）：<c>damage_dealt_basic</c>／<c>killed_basic</c> 的"自指/受方面"过滤。
    /// <para>**未支持的主体一律返回 null 且由调用方拒绝映射**（见 <see cref="NeedsPayloadSubject"/>）——
    /// 例如 `友方单位造成伤害时`（施动方归属面）／`目标单位造成伤害时` 暂不可表达，宁可未解析也不泛触发。</para>
    /// </summary>
    private static DslCondition? PayloadGuardOf(string template, string rawText)
    {
        // 主体归属面（E1-50）：**先看"本单位"**（自指），再看"友方/敌方"（归属）——
        // 注意 `本单位对敌方总部造成伤害时` 的"敌方"是**宾语**，不可误判为施动方归属。
        var selfSubject = ContainsSelfSubject(rawText);

        if (string.Equals(template, "damage_dealt_basic", StringComparison.Ordinal))
        {
            var subject = selfSubject
                ? new DslCondition(DslCondition.PayloadSelf, "Unit")              // 载荷 Unit＝施动方
                : SubjectOwnerGuard(rawText, "Unit");                            // 友方/敌方单位造成
            if (subject is null)
            {
                return null;
            }

            return ContainsHq(rawText)
                ? new DslCondition(DslCondition.AllKind, null,
                    new[] { subject, new DslCondition(DslCondition.PayloadIsHq, "Card") }) // 受方＝总部
                : subject;
        }

        if (string.Equals(template, "killed_basic", StringComparison.Ordinal))
        {
            return selfSubject
                ? new DslCondition(DslCondition.PayloadSelf, "Killer")           // 载荷 Killer＝击杀者
                : SubjectOwnerGuard(rawText, "Killer");
        }

        // S3：`unit.upgraded`／`unit.revealed` 载荷 {Unit}——与 damage_dealt 同构的自指面：
        // 裸短语（「升为老兵时」「被揭示时」）＝**隐式自指**（本单位）；「本单位/该单位」＝显式自指；
        // 「友方/敌方单位…」的归属面由 OwnerGuardKind 承载（见 OwnerFilterableTemplates 白名单）。
        if (string.Equals(template, "veteran_basic", StringComparison.Ordinal)
            || string.Equals(template, "reveal_basic", StringComparison.Ordinal))
        {
            return selfSubject || IsBareSelfSubjectPhrase(rawText)
                ? new DslCondition(DslCondition.PayloadSelf, "Unit")
                : null;
        }

        return null;
    }

    /// <summary>主体＝友方/敌方时的**归属守卫**（E1-50）；主体不可辨识＝null（调用方拒绝映射）。</summary>
    private static DslCondition? SubjectOwnerGuard(string rawText, string property)
    {
        if (rawText.Contains("友方", StringComparison.Ordinal))
        {
            return new DslCondition(DslCondition.PayloadOwnerSame, property);
        }

        if (rawText.Contains("敌方", StringComparison.Ordinal))
        {
            return new DslCondition(DslCondition.PayloadOwnerDifferent, property);
        }

        return null;
    }

    /// <summary>短语是否指向**总部**（受方面限定）。</summary>
    private static bool ContainsHq(string rawText) => rawText.Contains("总部", StringComparison.Ordinal);

    /// <summary>短语主体是否＝**本单位**（自指；`本单位`／`该单位`）。</summary>
    private static bool ContainsSelfSubject(string rawText) =>
        rawText.Contains("本单位", StringComparison.Ordinal) || rawText.Contains("该单位", StringComparison.Ordinal);

    /// <summary>
    /// S3：**裸自指监听短语**（无主体词缀——「升为老兵时」「被揭示时」）＝隐式"本单位"
    /// （该卡自己的升级/揭示事件；不泛触发于同场其它单位）。
    /// </summary>
    private static bool IsBareSelfSubjectPhrase(string rawText) =>
        rawText.StartsWith("升为老兵", StringComparison.Ordinal)
        || rawText.StartsWith("被揭示", StringComparison.Ordinal);

    /// <summary>
    /// 该监听模板是否**必须**有"本单位"主体（E1-47）：`damage_dealt_basic`／`killed_basic` 的归属面
    /// 无法用既有 owner 过滤表达 ⇒ 无"本单位"时不映射（避免"任意单位造成伤害/消灭"的泛触发）。
    /// </summary>
    private static bool NeedsPayloadSubject(string template) =>
        template is "damage_dealt_basic" or "killed_basic";

    /// <summary>
    /// 该监听模板的载荷**携带"事件卡/单位"**（决定能否按**卡面**做归属过滤——白名单口径，避免误用）：
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
        "combat_survived_basic", // unit.combat.survived 载荷 {Unit}（E1-39）
        "counter_basic",    // counter.triggered 载荷 {Card, Player}（E1-53）
        "veteran_basic",    // unit.upgraded    载荷 {Unit}（S3——「友方/敌方单位升为老兵时」归属守卫）
        "reveal_basic",     // unit.revealed    载荷 {Unit}（S3——「友方隐蔽单位被揭示时」归属守卫）
    };

    /// <summary>
    /// 该监听模板的载荷**只有玩家、没有事件卡**（E1-39）：按**玩家面**做归属过滤
    /// （宿主玩家 vs 载荷玩家；守卫读 <c>view.Player</c>）。
    /// </summary>
    private static readonly HashSet<string> PlayerFilterableTemplates = new(StringComparer.Ordinal)
    {
        "slot_gained_basic", // slot.gained 载荷 {Player, Amount}
        "slot_lost_basic",   // slot.lost   载荷 {Player, Amount}
    };

    /// <summary>
    /// 监听短语的阵营归类 → 归属过滤**条件种类**（按模板的载荷面二选一；无所属面/无阵营＝null ⇒ 不加守卫）。
    /// </summary>
    private static string? OwnerGuardKind(string template, string? sideValue) => sideValue switch
    {
        "friendly" when OwnerFilterableTemplates.Contains(template) => DslCondition.OwnerSame,
        "friendly" when PlayerFilterableTemplates.Contains(template) => DslCondition.OwnerSameByPlayer,
        "enemy" when OwnerFilterableTemplates.Contains(template) => DslCondition.OwnerDifferent,
        "enemy" when PlayerFilterableTemplates.Contains(template) => DslCondition.OwnerDifferentByPlayer,
        _ => null,
    };

    private static UnresolvedRecord Fail(string original, TextSpan span, string reason) =>
        new(span.Start, span.Length, RawOf(original, span), reason);

    private static string RawOf(string original, TextSpan span) =>
        span.Start >= 0 && span.Start + span.Length <= original.Length
            ? original.Substring(span.Start, span.Length)
            : string.Empty;
}
