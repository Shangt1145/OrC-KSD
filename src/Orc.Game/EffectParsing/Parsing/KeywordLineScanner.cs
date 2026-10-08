using Orc.Game.Cards;

namespace Orc.Game.EffectParsing.Parsing;

/// <summary>词条行扫描结果：声明（逐词条分列、行序＋行内序、保留重复）＋异常记录（复用既有无解析记录面）。</summary>
/// <param name="Declarations">词条行声明（判定成立行产出）。</param>
/// <param name="Anomalies">异常记录（数字成分无法作为参值——原文＋可辨识原因；归既有无解析记录面，不新增独立异常面）。</param>
public sealed record KeywordLineScanResult(
    IReadOnlyList<LineDeclaration> Declarations,
    IReadOnlyList<UnresolvedRecord> Anomalies);

/// <summary>
/// 词条行扫描器（词条行联动·批 4 序列③）：把「整行判定成立」的**词条行**（行内成分全部为数词／标点／
/// 三个维度过滤词）从"静默丢弃"改为产出**声明**（维度＋标识＋参值＋注册状态）供上层使用。
/// <list type="bullet">
///   <item>判定语义＝既有「词条行」判定（与 <see cref="Segmenter"/> 排除出效果切分流的口径共用同一实现——
///   判定成立行仍**不产效果单元**、不产生可执行效果；既有「丢弃」语义保留）。</item>
///   <item>逐成分处理（判定成立行）：词条成分 → 其**紧邻后随的单个数字**（允许空格；含零空格"重甲3"）
///   为参值（无则 null）；无法归属／形态非法的数字成分 → 异常记录（逐一列示）；标点忽略。</item>
///   <item>参值形态：无符号非负整数（含符号形态不构成参值 → 异常）；数字形态族＝Num token
///   （含中文数字词——整数化，与效果文本用语同一识别源）。</item>
///   <item>未知词条行：成词法但未注册 → **产出并标注**（<see cref="DeclarationRegistration.Unregistered"/>）；
///   不成词法 → 整行判定不成立 → 不产出（走既有解析路径）。</item>
///   <item>空白行（无 token）／纯标点行＝豁免（无内容可记——不作为"静默"违规）；非空但无有效声明可产出者
///   （如孤立数字）→ 异常记录（不静默）。</item>
///   <item>纯函数性：无副作用（不改输入、不依赖外部状态）；同输入同产出。</item>
/// </list>
/// </summary>
public sealed class KeywordLineScanner
{
    /// <summary>扫描词条行（全量 token 输入；只处理判定成立的行）。</summary>
    /// <exception cref="ArgumentNullException">original 或 tokens 为 null。</exception>
    public KeywordLineScanResult Scan(string original, IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(tokens);

        var declarations = new List<LineDeclaration>();
        var anomalies = new List<UnresolvedRecord>();

        // 行序 ＋ 行内序（token 序列即原文位置序；按行号排序保证跨行顺序＝原文顺序）。
        foreach (var pair in GroupByLine(original, tokens).OrderBy(pair => pair.Key))
        {
            if (IsKeywordOnlyLine(pair.Value))
            {
                ScanLine(original, pair.Value, declarations, anomalies);
            }
        }

        return new KeywordLineScanResult(declarations, anomalies);
    }

    /// <summary>逐成分处理单条判定成立行：词条→声明（参值＝紧邻后随数字）；数字无法归属→异常；标点忽略。</summary>
    private static void ScanLine(
        string original,
        IReadOnlyList<Token> lineTokens,
        List<LineDeclaration> declarations,
        List<UnresolvedRecord> anomalies)
    {
        Token? pendingKeyword = null;
        var pendingDimension = default(DeclarationDimension);

        foreach (var token in lineTokens)
        {
            if (TryGetDeclarationDimension(token, out var dimension))
            {
                if (pendingKeyword is not null)
                {
                    declarations.Add(CreateDeclaration(pendingKeyword, pendingDimension, value: null));
                }

                pendingKeyword = token;
                pendingDimension = dimension;
                continue;
            }

            if (token.Type == TokenType.Num)
            {
                if (pendingKeyword is not null
                    && pendingDimension != DeclarationDimension.UnitType
                    && CanBindValue(original, pendingKeyword, token))
                {
                    declarations.Add(CreateDeclaration(pendingKeyword, pendingDimension, token.IntValue));
                    pendingKeyword = null;
                    continue;
                }

                anomalies.Add(new UnresolvedRecord(
                    token.Start,
                    token.Length,
                    token.Lexeme,
                    $"词条行数字成分无法作为参值：'{token.Lexeme}'（参值须为词条紧邻后随的无符号非负整数）。"));
                continue;
            }

            // 标点：忽略（不产声明、不产异常）。
        }

        if (pendingKeyword is not null)
        {
            declarations.Add(CreateDeclaration(pendingKeyword, pendingDimension, value: null));
        }
    }

    /// <summary>
    /// 参值绑定判定：数字须**紧邻**词条其后（两者原文字符区间仅含空白；含零空格）且为**无符号**数词。
    /// 兵种维度不绑定（参值位恒空——见 <see cref="DeclarationDimension.UnitType"/> 口径）。
    /// </summary>
    private static bool CanBindValue(string original, Token keyword, Token number)
    {
        if (number.Start < keyword.End || IsSignedNumeral(number))
        {
            return false;
        }

        for (var i = keyword.End; i < number.Start; i++)
        {
            if (!char.IsWhiteSpace(original[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>含符号数词（`+3`／`-1`／全角符号——E1-41 起符号并入 Num）：参值限无符号非负整数，故不构成参值。</summary>
    private static bool IsSignedNumeral(Token token) =>
        token.Lexeme.Length > 0 && token.Lexeme[0] is '+' or '-' or '＋' or '－' or '−';

    /// <summary>词条成分判定（Filter 且维度 ∈ keyword／unitType／attribute）＋维度映射。</summary>
    private static bool TryGetDeclarationDimension(Token token, out DeclarationDimension dimension)
    {
        dimension = default;
        if (token.Type != TokenType.Filter)
        {
            return false;
        }

        switch (token.Get("dimension"))
        {
            case "keyword":
                dimension = DeclarationDimension.Keyword;
                return true;
            case "unitType":
                dimension = DeclarationDimension.UnitType;
                return true;
            case "attribute":
                dimension = DeclarationDimension.Attribute;
                return true;
            default:
                return false;
        }
    }

    private static LineDeclaration CreateDeclaration(Token keyword, DeclarationDimension dimension, int? value) =>
        new(
            dimension,
            keyword.Lexeme,
            value,
            dimension == DeclarationDimension.Keyword
                ? KeywordRegistry.IsDefined(keyword.Lexeme)
                    ? DeclarationRegistration.Registered
                    : DeclarationRegistration.Unregistered
                : DeclarationRegistration.NotApplicable,
            new TextSpan(keyword.Start, keyword.Length));

    /// <summary>
    /// 把 token 按**行**分组（行以换行为界；key＝行号、从 0 起）——词条行判定与
    /// <see cref="Segmenter"/> 的切分排除共用（同一判定实现，杜绝两处口径漂移）。
    /// </summary>
    internal static Dictionary<int, List<Token>> GroupByLine(string original, IReadOnlyList<Token> tokens)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < original.Length; i++)
        {
            if (original[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        var byLine = new Dictionary<int, List<Token>>();
        foreach (var token in tokens)
        {
            var line = LineOf(lineStarts, token.Start);
            if (!byLine.TryGetValue(line, out var list))
            {
                byLine[line] = list = new List<Token>();
            }

            list.Add(token);
        }

        return byLine;
    }

    /// <summary>
    /// 「整行只有词条/数值」判定（既有权威语义——原 <c>Segmenter.DropKeywordOnlyLines</c> 逐字保持）：
    /// 行内成分全部为数词／标点／三个维度过滤词（keyword／unitType／attribute）＝判定成立。
    /// </summary>
    internal static bool IsKeywordOnlyLine(IReadOnlyList<Token> lineTokens)
    {
        if (lineTokens.Count == 0)
        {
            return false;
        }

        foreach (var token in lineTokens)
        {
            var isKeywordFilter = token.Type == TokenType.Filter
                && (token.Get("dimension") is "keyword" or "unitType" or "attribute");
            if (token.Type != TokenType.Num && token.Type != TokenType.Punct && !isKeywordFilter)
            {
                return false;
            }
        }

        return true;
    }

    private static int LineOf(List<int> lineStarts, int offset)
    {
        var low = 0;
        var high = lineStarts.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (lineStarts[mid] <= offset)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }
}
