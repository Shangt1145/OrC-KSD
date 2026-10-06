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
                    sideRaw ??= token.Lexeme;
                    sideValue ??= token.Get("value");
                    break;
                case TokenType.Zone:
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
                    break;
                case TokenType.Unknown:
                    if (actions.Count > 0)
                    {
                        objectParts.Add(token.Lexeme);
                    }

                    break;
            }
        }

        var span = new TextSpan(tokens[0].Start, tokens[^1].End - tokens[0].Start);
        var condition = conditionRaw is null ? null : new ConditionPhrase(conditionRaw, conditionSpan);

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
                filters, excludeSelf: false, hasPronoun, pronounForm, span);
            lastTarget = target;
        }

        if (actions.Count == 0)
        {
            // 纯目标声明判定：无动作、但含至少一个**已识别** token（如「指向 1 个单位」）。
            var isDeclaration = tokens.Any(token => token.Type != TokenType.Unknown);
            return new ClauseNode(condition, target, Array.Empty<ActionPhrase>(), span, isDeclaration);
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

        return new ClauseNode(condition, target, actionPhrases, span);
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
