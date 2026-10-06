namespace Orc.Game.EffectParsing.Parsing;

/// <summary>12 类 token（解析段 S7，A 阶段定稿）。</summary>
public enum TokenType
{
    /// <summary>触发词（具名触发 / 触发后缀）。</summary>
    Trigger,

    /// <summary>标点。</summary>
    Punct,

    /// <summary>连接/逻辑词。</summary>
    Connective,

    /// <summary>代词/回指。</summary>
    Pronoun,

    /// <summary>动作词。</summary>
    Action,

    /// <summary>量词/选靶。</summary>
    Quant,

    /// <summary>阵营词。</summary>
    Side,

    /// <summary>区域/落点词。</summary>
    Zone,

    /// <summary>过滤词。</summary>
    Filter,

    /// <summary>条件词。</summary>
    Cond,

    /// <summary>数词。</summary>
    Num,

    /// <summary>未知词（累积成段）。</summary>
    Unknown,
}

/// <summary>标点子型（<see cref="TokenType.Punct"/> 的 <c>kind</c> 载荷取值）。</summary>
public static class PunctKinds
{
    /// <summary>硬边界：<c>。！？</c>。</summary>
    public const string Hard = "hard";

    /// <summary>软边界：<c>；</c>。</summary>
    public const string Soft = "soft";

    /// <summary>子句分隔：<c>，</c>。</summary>
    public const string Clause = "clause";

    /// <summary>并列分隔：<c>、</c>（触发短语内不断句，正文内可断子句）。</summary>
    public const string List = "list";

    /// <summary>触发界定：<c>：</c>。</summary>
    public const string Colon = "colon";

    /// <summary>引号开。</summary>
    public const string QuoteOpen = "quoteOpen";

    /// <summary>引号闭。</summary>
    public const string QuoteClose = "quoteClose";
}

/// <summary>触发词载荷键（<see cref="TokenType.Trigger"/>）。</summary>
public static class TriggerRoles
{
    /// <summary>具名触发（部署/亡计/动员/抉择）。</summary>
    public const string Named = "named";

    /// <summary>触发后缀（时/后）。</summary>
    public const string Suffix = "suffix";
}

/// <summary>token：类型 + 原文字面 + **原文偏移** + 归类载荷。</summary>
public sealed class Token
{
    /// <summary>创建 token。</summary>
    /// <exception cref="ArgumentException">lexeme 空白。</exception>
    public Token(
        TokenType type,
        string lexeme,
        int start,
        int length,
        IReadOnlyDictionary<string, string>? payload = null,
        int? intValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lexeme);
        Type = type;
        Lexeme = lexeme;
        Start = start;
        Length = length;
        Payload = payload ?? new Dictionary<string, string>(StringComparer.Ordinal);
        IntValue = intValue;
    }

    /// <summary>类型。</summary>
    public TokenType Type { get; }

    /// <summary>原文字面。</summary>
    public string Lexeme { get; }

    /// <summary>原文起始偏移。</summary>
    public int Start { get; }

    /// <summary>原文长度。</summary>
    public int Length { get; }

    /// <summary>原文结束偏移（不含）。</summary>
    public int End => Start + Length;

    /// <summary>归类载荷。</summary>
    public IReadOnlyDictionary<string, string> Payload { get; }

    /// <summary>数词值（<see cref="TokenType.Num"/>）。</summary>
    public int? IntValue { get; }

    /// <summary>取载荷键（缺省 null）。</summary>
    public string? Get(string key) => Payload.TryGetValue(key, out var value) ? value : null;

    /// <summary>是否某子型标点。</summary>
    public bool IsPunct(string kind) => Type == TokenType.Punct && string.Equals(Get("kind"), kind, StringComparison.Ordinal);
}
