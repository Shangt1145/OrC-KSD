namespace Orc.Game.EffectParsing.Parsing;

/// <summary>
/// AST 构建（解析段 S8）：token 序列 → 中性语法树。
/// <list type="bullet">
///   <item>触发短语＝句首 → 第一个 <c>：</c> 或第一个 <c>，</c>；监听型要求短语内含触发后缀。</item>
///   <item>软边界单元**继承**最近硬边界单元的触发（D1）；代词在同效果内回指前文目标（D5 安全阀）。</item>
///   <item>多事件在 <see cref="TriggerNode.Events"/> 并列（拆分由语义层做）。</item>
/// </list>
/// </summary>
public sealed class AstBuilder
{
    /// <summary>构建。</summary>
    /// <exception cref="ArgumentNullException">segments 为 null。</exception>
    public IReadOnlyList<EffectAst> Build(IReadOnlyList<SegmentedEffect> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var results = new List<EffectAst>();
        EffectAst? currentHard = null;
        TargetPhrase? lastTarget = null;

        foreach (var segment in segments)
        {
            var (trigger, bodyStart) = DetectTrigger(segment.Tokens);
            var clauses = BuildClauses(segment.Tokens, bodyStart, ref lastTarget);
            var boundary = segment.HardBoundary ? BoundaryKind.Hard : BoundaryKind.Soft;

            TriggerNode? effective = trigger;
            if (trigger is null && boundary == BoundaryKind.Soft && currentHard is not null)
            {
                effective = currentHard.Trigger;
            }

            var ast = new EffectAst(
                new TextSpan(segment.Start, segment.Length),
                boundary,
                boundary == BoundaryKind.Soft ? currentHard : null,
                effective,
                clauses);

            results.Add(ast);
            if (segment.HardBoundary)
            {
                currentHard = ast;
            }
        }

        return results;
    }

    // ---------- 触发识别 ----------

    private static (TriggerNode? Trigger, int BodyStart) DetectTrigger(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count == 0)
        {
            return (null, 0);
        }

        var first = tokens[0];
        if (first.Type == TokenType.Trigger && first.Get("role") == TriggerRoles.Named)
        {
            var end = FindPhraseEnd(tokens);
            var phrase = tokens.Take(end + 1).ToList();
            var events = phrase
                .Where(token => (token.Type == TokenType.Trigger && token.Get("role") == TriggerRoles.Named)
                                || token.Type == TokenType.Action)
                .Select(token => new TriggerEventPhrase(token.Lexeme, new TextSpan(token.Start, token.Length)))
                .ToList();

            if (events.Count == 0)
            {
                events.Add(new TriggerEventPhrase(first.Lexeme, new TextSpan(first.Start, first.Length)));
            }

            var span = SpanOf(phrase);
            var node = new TriggerNode(TriggerSyntaxKind.Named, JoinRaw(phrase), events, span);
            return (node, SkipBodyDelimiters(tokens, end + 1));
        }

        var suffixIndex = FindListenSuffix(tokens);
        if (suffixIndex >= 0)
        {
            var phrase = tokens.Take(suffixIndex + 1).ToList();
            var span = SpanOf(phrase);
            var raw = JoinRaw(phrase);
            string? sideRaw = null;
            string? sideValue = null;
            foreach (var token in phrase)
            {
                if (token.Type == TokenType.Side)
                {
                    sideRaw = token.Lexeme;
                    sideValue = token.Get("value");
                    break;
                }
            }

            var node = new TriggerNode(
                TriggerSyntaxKind.Listen,
                raw,
                new[] { new TriggerEventPhrase(raw, span) },
                span,
                sideRaw,
                sideValue);
            return (node, SkipBodyDelimiters(tokens, suffixIndex + 1));
        }

        return (null, 0);
    }

    private static int FindPhraseEnd(IReadOnlyList<Token> tokens)
    {
        for (var i = 1; i < tokens.Count; i++)
        {
            if (tokens[i].IsPunct(PunctKinds.Colon) || tokens[i].IsPunct(PunctKinds.Clause))
            {
                return i - 1;
            }

            if (tokens[i].Type == TokenType.Trigger && tokens[i].Get("role") == TriggerRoles.Suffix)
            {
                return i;
            }
        }

        return tokens.Count - 1;
    }

    private static int FindListenSuffix(IReadOnlyList<Token> tokens)
    {
        var limit = tokens.Count;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].IsPunct(PunctKinds.Clause) || tokens[i].IsPunct(PunctKinds.Colon))
            {
                limit = i;
                break;
            }
        }

        for (var i = 0; i < limit; i++)
        {
            if (tokens[i].Type == TokenType.Trigger && tokens[i].Get("role") == TriggerRoles.Suffix)
            {
                return i;
            }
        }

        return -1;
    }

    private static int SkipBodyDelimiters(IReadOnlyList<Token> tokens, int index)
    {
        while (index < tokens.Count
               && (tokens[index].IsPunct(PunctKinds.Colon)
                   || tokens[index].IsPunct(PunctKinds.Clause)
                   || tokens[index].IsPunct(PunctKinds.List)))
        {
            index++;
        }

        return index;
    }

    private static TextSpan SpanOf(IReadOnlyList<Token> tokens) =>
        tokens.Count == 0 ? default : new TextSpan(tokens[0].Start, tokens[^1].End - tokens[0].Start);

    private static string JoinRaw(IEnumerable<Token> tokens) => string.Concat(tokens.Select(token => token.Lexeme));

    // ---------- 子句构建 ----------

    private List<ClauseNode> BuildClauses(IReadOnlyList<Token> tokens, int start, ref TargetPhrase? lastTarget)
    {
        var clauses = new List<ClauseNode>();
        var buffer = new List<Token>();

        for (var i = start; i <= tokens.Count; i++)
        {
            var isBoundary = i >= tokens.Count
                             || tokens[i].IsPunct(PunctKinds.Clause)
                             || tokens[i].IsPunct(PunctKinds.List);
            if (!isBoundary)
            {
                if (!tokens[i].IsPunct(PunctKinds.Colon))
                {
                    buffer.Add(tokens[i]);
                }

                continue;
            }

            if (buffer.Count > 0)
            {
                var clause = BuildClause(buffer, ref lastTarget);
                clauses.Add(clause);
                buffer.Clear();
            }
        }

        return clauses;
    }

    private static ClauseNode BuildClause(List<Token> tokens, ref TargetPhrase? lastTarget)
    {
        string? quantifierRaw = null;
        string? quantifierSel = null;
        string? sideRaw = null;
        string? sideValue = null;
        string? zoneRaw = null;
        string? zoneValue = null;
        var filters = new List<FilterPhrase>();
        var hasPronoun = false;
        string? pronounForm = null;
        string? conditionRaw = null;
        var conditionSpan = default(TextSpan);
        var actions = new List<(int Index, string Verb, string? Key, TextSpan Span)>();
        var objectParts = new List<string>();
        int? numberValue = null;
        string? numberRaw = null;
        var numberSpan = default(TextSpan);
        var numberIndex = -1;
        int? secondValue = null;
        string? secondRaw = null;
        var secondSpan = default(TextSpan);
        var hasUnrecognizedQualifier = false;
        string? comparisonOp = null;
        var comparisonSpan = default(TextSpan);
        int? comparisonRight = null;
        var comparisonRightIsCount = false;
        string? comparisonRightSide = null;
        string? comparisonRightZone = null;
        var leftIsCount = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Type)
            {
                case TokenType.Quant:
                    var sel = token.Get("sel");
                    if (sel == "measure")
                    {
                        break;
                    }

                    quantifierRaw ??= token.Lexeme;
                    quantifierSel ??= sel;
                    break;
                case TokenType.Side:
                    if (comparisonOp is not null)
                    {
                        comparisonRightSide ??= token.Get("value");
                        break;
                    }

                    sideRaw ??= token.Lexeme;
                    sideValue ??= token.Get("value");
                    break;
                case TokenType.Zone:
                    if (comparisonOp is not null)
                    {
                        comparisonRightZone ??= token.Get("value");
                        break;
                    }

                    zoneRaw ??= token.Lexeme;
                    zoneValue ??= token.Get("value");
                    break;
                case TokenType.Filter:
                    filters.Add(new FilterPhrase(
                        ParseFilterKind(token.Get("dimension")),
                        token.Lexeme,
                        token.Get("value"),
                        new TextSpan(token.Start, token.Length)));
                    break;
                case TokenType.Num:
                    // E1-57：**比较算子之后**的数值＝右操作数（不是动作载荷——否则 `消灭 1 个花费不大于 3 的单位`
                    // 的 `3` 会被当成"第二数值"）。
                    if (comparisonOp is not null && token.Start > comparisonSpan.Start)
                    {
                        comparisonRight ??= token.IntValue;
                        break;
                    }

                    // E1-41：`[数字][量词(measure)]` 且**此子句尚未出现动作词** ⇒ 该数字是**数量短语**
                    // （目标量词，如 `使 1 个友方步兵具有 +2 攻击力` 的 `1 个`），不是动作载荷；
                    // 例外：紧跟**引号卡名**时仍是数量载荷（`将 3 张“X”加入手中`）。
                    if (i + 1 < tokens.Count
                        && tokens[i + 1].Type == TokenType.Quant
                        && string.Equals(tokens[i + 1].Get("sel"), "measure", StringComparison.Ordinal)
                        && actions.Count == 0
                        && !(i + 2 < tokens.Count
                             && tokens[i + 2].Type == TokenType.Filter
                             && string.Equals(tokens[i + 2].Get("dimension"), "name", StringComparison.Ordinal)))
                    {
                        break;
                    }

                    if (numberValue is null)
                    {
                        numberValue = token.IntValue;
                        numberRaw = token.Lexeme;
                        numberSpan = new TextSpan(token.Start, token.Length);
                        numberIndex = i;
                    }
                    else if (secondValue is null)
                    {
                        secondValue = token.IntValue;
                        secondRaw = token.Lexeme;
                        secondSpan = new TextSpan(token.Start, token.Length);
                    }

                    break;
                case TokenType.Action:
                    actions.Add((i, token.Lexeme, token.Get("key"), new TextSpan(token.Start, token.Length)));
                    break;
                case TokenType.Pronoun:
                    hasPronoun = true;
                    pronounForm ??= token.Lexeme;
                    break;
                case TokenType.Cond:
                    conditionRaw ??= token.Lexeme;
                    conditionSpan = new TextSpan(token.Start, token.Length);
                    if (ComparisonOperators.TryGetValue(token.Lexeme, out var opKey))
                    {
                        comparisonOp = opKey;
                        comparisonSpan = new TextSpan(token.Start, token.Length);
                    }

                    break;
                case TokenType.Unknown:
                    // 计数标记＝`单位数` 这类**多字**未知段结尾的 `数`（裸 `数` 除外——如 `指挥点数` 的 `数`
                    // 被过滤器切开后自成一字，不当计数）。
                    if (comparisonOp is null && token.Lexeme.Trim().Length >= 2
                        && token.Lexeme.TrimEnd().EndsWith('数'))
                    {
                        leftIsCount = true; // 友方单位数：计数度量（E1-57）
                    }

                    if (actions.Count > 0)
                    {
                        objectParts.Add(token.Lexeme);
                    }
                    else if (!IsSyntacticParticle(token.Lexeme))
                    {
                        // 目标区的未识别限定词（E1-52）：记录在案，供语义层拒绝"静默丢弃限定词"的映射。
                        hasUnrecognizedQualifier = true;
                    }

                    break;
            }
        }

        var span = new TextSpan(tokens[0].Start, tokens[^1].End - tokens[0].Start);
        var condition = conditionRaw is null ? null : new ConditionPhrase(conditionRaw, conditionSpan);
        var comparison = BuildComparison(tokens, comparisonOp, comparisonSpan, comparisonRight, comparisonRightSide, comparisonRightZone, leftIsCount, sideValue, zoneValue, span);

        var hasQualifier = quantifierRaw is not null || sideRaw is not null || zoneRaw is not null || filters.Count > 0;
        TargetPhrase? target;
        if (hasPronoun && !hasQualifier)
        {
            target = lastTarget; // 代词回指；无前文目标则 null（交由语义层显式失败）
        }
        else
        {
            target = new TargetPhrase(
                quantifierRaw, quantifierSel, sideRaw, sideValue, zoneRaw, zoneValue,
                filters, excludeSelf: false, hasPronoun, pronounForm, span, hasUnrecognizedQualifier);
            lastTarget = target;
        }

        if (actions.Count == 0)
        {
            // 纯目标声明判定：无动作、但含至少一个**已识别** token（如「指向 1 个单位」）。
            var isDeclaration = tokens.Any(token => token.Type != TokenType.Unknown);
            return new ClauseNode(condition, target, Array.Empty<ActionPhrase>(), span, isDeclaration, comparison);
        }

        var actionPhrases = actions
            .Select(action => new ActionPhrase(action.Verb, action.Key, null, null, action.Span))
            .ToList();

        if (numberValue is not null || objectParts.Count > 0)
        {
            var attach = 0;
            var limit = numberIndex < 0 ? int.MaxValue : numberIndex;
            for (var i = 0; i < actions.Count; i++)
            {
                if (actions[i].Index < limit)
                {
                    attach = i;
                }
            }

            var payload = numberValue is null
                ? null
                : new PayloadNode(PayloadKind.Integer, numberValue.Value, numberRaw!, numberSpan);
            var secondary = secondValue is null
                ? null
                : new PayloadNode(PayloadKind.Integer, secondValue.Value, secondRaw!, secondSpan);
            var objectRaw = objectParts.Count == 0 ? null : string.Concat(objectParts);
            var source = actions[attach];
            actionPhrases[attach] = new ActionPhrase(source.Verb, source.Key, objectRaw, payload, source.Span, secondary);
        }

        return new ClauseNode(condition, target, actionPhrases, span, comparison: comparison);
    }

    /// <summary>
    /// 构造**数值比较短语**（E1-57）：左度量＝算子**之前**的过滤短语（属性/对象维度）＋阵营/区域＋计数标记。
    /// </summary>
    private static ComparisonPhrase? BuildComparison(
        List<Token> tokens,
        string? op,
        TextSpan opSpan,
        int? rightValue,
        string? rightSide,
        string? rightZone,
        bool leftIsCount,
        string? leftSide,
        string? leftZone,
        TextSpan span)
    {
        if (op is null)
        {
            return null;
        }

        var left = new List<FilterPhrase>();
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.Filter && token.Start < opSpan.Start)
            {
                left.Add(new FilterPhrase(
                    ParseFilterKind(token.Get("dimension")), token.Lexeme, token.Get("value"), new TextSpan(token.Start, token.Length)));
            }
        }

        var rightIsCount = rightValue is null && rightSide is not null;
        return new ComparisonPhrase(
            tokens.First(token => token.Start == opSpan.Start && token.Length == opSpan.Length).Lexeme,
            op, left, leftSide, leftZone, leftIsCount,
            rightValue, rightIsCount, rightSide, rightZone, new TextSpan(opSpan.Start, opSpan.Length));
    }

    /// <summary>
    /// 纯句法虚词（E1-52）：出现在动词前也不构成"未识别限定词"——如 `使 1 个友方步兵具有…` 的 `使`、
    /// `对 1 个敌方单位造成…` 的 `对`。**只收虚词**（不收实义词：`相邻`／`单位`／`指令`／`陆军`… 仍视为限定词）。
    /// </summary>
    /// <summary>比较算子词表（E1-57）：不小于／不大于／大于／小于 ⇒ 键。</summary>
    private static readonly Dictionary<string, string> ComparisonOperators = new(StringComparer.Ordinal)
    {
        ["不小于"] = "gte",
        ["不大于"] = "lte",
        ["大于"] = "gt",
        ["小于"] = "lt",
        ["等于"] = "eq",
    };

    private static bool IsSyntacticParticle(string lexeme)
    {
        var trimmed = lexeme.Replace(" ", string.Empty, StringComparison.Ordinal);

        // 通用名词（"单位/卡牌/牌"）：本身不承载限定语义（`1 个友方单位`／`所有敌方单位`），
        // 而其**修饰词**（`受伤单位`／`相邻陆军` 等）各自成段、仍会被判为限定词。
        if (trimmed is "单位" or "卡牌" or "牌" or "其他" or "其它")
        {
            return true;
        }

        // `其他/其它` 的部分片段（`其` 是代词、余下字符自成一未知段）——E1-56 由排除自身维度承载。
        if (trimmed is "他" or "它" or "其")
        {
            return true;
        }

        return trimmed.Length > 0 && trimmed.Length <= 2 && (trimmed[0]) switch
        {
            '使' or '令' or '将' or '把' or '对' or '向' or '给' or '与' or '和' or '且' or '并'
                or '的' or '之' or '于' or '为' or '则' or '后' or '前' or '时' or '中' or '内' or '外' => true,
            _ => false,
        };
    }

    private static FilterKind ParseFilterKind(string? dimension) => dimension switch
    {
        "unitType" => FilterKind.UnitType,
        "keyword" => FilterKind.Keyword,
        "set" => FilterKind.Set,
        "attribute" => FilterKind.Attribute,
        "threshold" => FilterKind.Threshold,
        "object" => FilterKind.Object,
        _ => FilterKind.Name,
    };
}
