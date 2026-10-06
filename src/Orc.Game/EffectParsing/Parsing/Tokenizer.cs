using System.Globalization;
using Orc.Game.EffectParsing.Lexicon;

namespace Orc.Game.EffectParsing.Parsing;

/// <summary>分词结果：归一文本 + token 序列。</summary>
/// <param name="Normalized">归一文本（含原文偏移映射）。</param>
/// <param name="Tokens">token 序列（原文偏移）。</param>
public sealed record TokenizationResult(NormalizedText Normalized, IReadOnlyList<Token> Tokens);

/// <summary>
/// 词法层（解析段 S7）：中文无空格 ⇒ 以**词表最长匹配**扫描；标点先分类；
/// 阿拉伯数字成 `Num`；连续未命中字符合成一个 `Unknown`（A-D5）。
/// </summary>
public sealed class Tokenizer
{
    private readonly LexiconSet _lexicons;

    /// <summary>创建分词器。</summary>
    /// <exception cref="ArgumentNullException">lexicons 为 null。</exception>
    public Tokenizer(LexiconSet lexicons)
    {
        ArgumentNullException.ThrowIfNull(lexicons);
        _lexicons = lexicons;
    }

    /// <summary>分词。</summary>
    /// <exception cref="ArgumentNullException">original 为 null。</exception>
    public TokenizationResult Tokenize(string original)
    {
        ArgumentNullException.ThrowIfNull(original);

        var normalized = TextNormalizer.Normalize(original);
        var text = normalized.Text;
        var tokens = new List<Token>();
        var quoteDepth = 0;
        var i = 0;

        while (i < text.Length)
        {
            var ch = text[i];

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            var kind = Classify(ch, ref quoteDepth);
            if (kind is not null)
            {
                if (kind == PunctKinds.QuoteOpen)
                {
                    // 引号封装（E1-20）：整段（含引号）识别为**一个**"卡名/内嵌文本"过滤 token——
                    // 引号内的标点一律不参与切分（内容原样保留在 Lexeme）。
                    var closeChar = ch switch { '“' => '”', '「' => '」', _ => '"' };
                    var close = text.IndexOf(closeChar, i + 1);
                    var inner = close < 0
                        ? text.Substring(i + 1)
                        : text.Substring(i + 1, close - i - 1);
                    if (!string.IsNullOrWhiteSpace(inner))
                    {
                        var originalStart = normalized.ToOriginal(i);
                        var originalEnd = close < 0 || close + 1 >= text.Length
                            ? normalized.OriginalLength
                            : normalized.ToOriginal(close + 1);
                        tokens.Add(new Token(
                            TokenType.Filter,
                            inner,
                            originalStart,
                            originalEnd - originalStart,
                            new Dictionary<string, string>(StringComparer.Ordinal) { ["dimension"] = "name" }));
                    }

                    quoteDepth = 0;
                    i = close < 0 ? text.Length : close + 1;
                    continue;
                }

                tokens.Add(CreateToken(
                    TokenType.Punct, text, normalized, i, 1,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["kind"] = kind }));
                i++;
                continue;
            }

            // E1-41：带符号数值（`+2` / `-1` / 全角 `＋`／`－`）＝**一个** Num token（符号并入值）。
            // 修前符号单成 Unknown、数值为正 ⇒ `-1 行动花费` 会被读成 `+1`（静默反向）。
            if (IsSignChar(ch) && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                var signStart = i;
                var negative = IsMinusChar(ch);
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                var signed = int.Parse(text[(signStart + 1)..i], CultureInfo.InvariantCulture);
                tokens.Add(CreateToken(
                    TokenType.Num, text, normalized, signStart, i - signStart, null,
                    negative ? -signed : signed));
                continue;
            }

            if (char.IsAsciiDigit(ch))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                tokens.Add(CreateToken(
                    TokenType.Num, text, normalized, start, i - start, null,
                    int.Parse(text[start..i], CultureInfo.InvariantCulture)));
                continue;
            }

            if (_lexicons.TryMatch(text, i, out var entry, out var length))
            {
                tokens.Add(CreateToken(
                    ToTokenType(entry.Category), text, normalized, i, length, entry.Payload,
                    entry.Category == LexiconCategory.Num ? ParseNum(entry) : null));
                i += length;
                continue;
            }

            var unknownStart = i;
            i++;
            while (i < text.Length && !IsTokenStart(text, i))
            {
                i++;
            }

            tokens.Add(CreateToken(TokenType.Unknown, text, normalized, unknownStart, i - unknownStart));
        }

        return new TokenizationResult(normalized, tokens);
    }

    private bool IsTokenStart(string text, int index) =>
        IsPunctChar(text[index])
        || char.IsAsciiDigit(text[index])
        || (IsSignChar(text[index]) && index + 1 < text.Length && char.IsAsciiDigit(text[index + 1]))
        || _lexicons.TryMatch(text, index, out _, out _);

    /// <summary>数值符号（半角/全角加号、半角/全角减号与 Unicode 减号）。</summary>
    private static bool IsSignChar(char ch) => ch is '+' or '-' or '＋' or '－' or '−';

    /// <summary>负号（决定符号位）。</summary>
    private static bool IsMinusChar(char ch) => ch is '-' or '－' or '−';

    private static bool IsPunctChar(char ch) => ch switch
    {
        '。' or '！' or '？' or '；' or '，' or '、' or '：' or '“' or '”' or '「' or '」' or '"' => true,
        _ => false,
    };

    private static string? Classify(char ch, ref int quoteDepth)
    {
        switch (ch)
        {
            case '。':
            case '！':
            case '？':
                return PunctKinds.Hard;
            case '；':
                return PunctKinds.Soft;
            case '，':
                return PunctKinds.Clause;
            case '、':
                return PunctKinds.List;
            case '：':
                return PunctKinds.Colon;
            case '“':
            case '「':
                quoteDepth++;
                return PunctKinds.QuoteOpen;
            case '”':
            case '」':
                if (quoteDepth > 0)
                {
                    quoteDepth--;
                }

                return PunctKinds.QuoteClose;
            case '"':
                if (quoteDepth == 0)
                {
                    quoteDepth++;
                    return PunctKinds.QuoteOpen;
                }

                quoteDepth--;
                return PunctKinds.QuoteClose;
            default:
                return null;
        }
    }

    private static TokenType ToTokenType(LexiconCategory category) => category switch
    {
        LexiconCategory.Trigger => TokenType.Trigger,
        LexiconCategory.Connective => TokenType.Connective,
        LexiconCategory.Pronoun => TokenType.Pronoun,
        LexiconCategory.Action => TokenType.Action,
        LexiconCategory.Quant => TokenType.Quant,
        LexiconCategory.Side => TokenType.Side,
        LexiconCategory.Zone => TokenType.Zone,
        LexiconCategory.Filter => TokenType.Filter,
        LexiconCategory.Cond => TokenType.Cond,
        LexiconCategory.Num => TokenType.Num,
        _ => TokenType.Unknown,
    };

    private static int? ParseNum(LexiconEntry entry) =>
        int.TryParse(entry.Get("value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static Token CreateToken(
        TokenType type,
        string normalizedText,
        NormalizedText normalized,
        int start,
        int length,
        IReadOnlyDictionary<string, string>? payload = null,
        int? intValue = null)
    {
        var originalStart = normalized.ToOriginal(start);
        var originalEnd = start + length >= normalizedText.Length
            ? normalized.OriginalLength
            : normalized.ToOriginal(start + length);
        return new Token(type, normalizedText.Substring(start, length), originalStart, originalEnd - originalStart, payload, intValue);
    }
}
