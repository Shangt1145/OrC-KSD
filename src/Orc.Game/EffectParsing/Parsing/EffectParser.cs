using Orc.Game.EffectParsing.Lexicon;

namespace Orc.Game.EffectParsing.Parsing;

/// <summary>
/// 效果解析器（解析段 S10）：卡面文本 → tokenizer → AST → DSL。
/// 单入口 <see cref="Parse"/> 返回「效果数组 + 未解析记录」。
/// </summary>
public sealed class EffectParser
{
    private readonly Tokenizer _tokenizer;
    private readonly Segmenter _segmenter = new();
    private readonly AstBuilder _astBuilder = new();
    private readonly SemanticMapper _mapper;

    /// <summary>创建解析器（映射器注入**内嵌效果递归回调**＝本解析器自身）。</summary>
    /// <exception cref="ArgumentNullException">lexicons 为 null。</exception>
    public EffectParser(LexiconSet lexicons)
    {
        _tokenizer = new Tokenizer(lexicons);
        _mapper = new SemanticMapper(Parse);
    }

    /// <summary>按默认词表目录创建。</summary>
    public static EffectParser CreateDefault(out IReadOnlyList<LexiconLoadFailure> failures)
    {
        var lexicons = LexiconLoader.LoadDirectory(LexiconLoader.DefaultDirectory, out failures);
        return new EffectParser(lexicons);
    }

    /// <summary>解析卡面文本（一卡多效果＝效果数组）。</summary>
    /// <exception cref="ArgumentNullException">cardFaceText 为 null。</exception>
    public ParseResult Parse(string cardFaceText)
    {
        ArgumentNullException.ThrowIfNull(cardFaceText);
        return _mapper.Map(cardFaceText, ParseAst(cardFaceText));
    }

    /// <summary>解析到语法树（供调试/测试）。</summary>
    /// <exception cref="ArgumentNullException">cardFaceText 为 null。</exception>
    public IReadOnlyList<EffectAst> ParseAst(string cardFaceText)
    {
        ArgumentNullException.ThrowIfNull(cardFaceText);
        var tokenization = _tokenizer.Tokenize(cardFaceText);
        var segments = _segmenter.Segment(cardFaceText, tokenization.Tokens);
        return _astBuilder.Build(segments);
    }

    /// <summary>分词（供调试/测试）。</summary>
    /// <exception cref="ArgumentNullException">cardFaceText 为 null。</exception>
    public IReadOnlyList<Token> Tokenize(string cardFaceText)
    {
        ArgumentNullException.ThrowIfNull(cardFaceText);
        return _tokenizer.Tokenize(cardFaceText).Tokens;
    }
}
