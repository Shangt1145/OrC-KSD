namespace Orc.Game.EffectParsing.Parsing;

/// <summary>切分出的效果单元（解析段 S8/I13）：原文 span + 是否自成链首 + 所属 token。</summary>
/// <param name="Start">原文起始偏移。</param>
/// <param name="Length">原文长度。</param>
/// <param name="HardBoundary">
/// true＝本单元**自成链首**（前面是句号/文首）；false＝本单元**承接上一个软边界**（分号后仍受前面控制，需继承）。
/// </param>
/// <param name="Tokens">该单元的 token（不含边界标点）。</param>
public sealed record SegmentedEffect(int Start, int Length, bool HardBoundary, IReadOnlyList<Token> Tokens)
{
    /// <summary>原文结束偏移（不含）。</summary>
    public int End => Start + Length;
}

/// <summary>
/// 效果切分（解析段 S8/I13）：
/// ① 把"整行只有词条/数值"的**词条行**从效果切分流中排除（P1.2——判定语义与
/// <see cref="KeywordLineScanner"/> 共用同一实现；词条行**不产效果单元**、其**声明产出**见该类——
/// 本类不再"静默丢弃"声明面）；② 按 `。！？`（硬）/`；`（软）切单元，
/// **引号内不切**（按引号深度跳过，A-D3）。
/// </summary>
public sealed class Segmenter
{
    /// <summary>切分。</summary>
    /// <exception cref="ArgumentNullException">original 或 tokens 为 null。</exception>
    public IReadOnlyList<SegmentedEffect> Segment(string original, IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(tokens);

        var kept = DropKeywordOnlyLines(original, tokens);
        var results = new List<SegmentedEffect>();
        var buffer = new List<Token>();
        var quoteDepth = 0;
        var previousWasSoft = false;

        void Close()
        {
            if (buffer.Count == 0)
            {
                return;
            }

            var start = buffer[0].Start;
            var end = buffer[^1].End;
            results.Add(new SegmentedEffect(start, end - start, !previousWasSoft, buffer.ToArray()));
            buffer.Clear();
        }

        foreach (var token in kept)
        {
            if (token.Type == TokenType.Punct)
            {
                var kind = token.Get("kind");
                if (kind == PunctKinds.QuoteOpen)
                {
                    quoteDepth++;
                }
                else if (kind == PunctKinds.QuoteClose)
                {
                    if (quoteDepth > 0)
                    {
                        quoteDepth--;
                    }
                }
                else if (quoteDepth == 0 && (kind == PunctKinds.Hard || kind == PunctKinds.Soft))
                {
                    Close();
                    previousWasSoft = kind == PunctKinds.Soft;
                    continue;
                }
            }

            buffer.Add(token);
        }

        Close();
        return results;
    }

    /// <summary>
    /// 把"整行只有词条/数值"的行排除出切分流（判定＝<see cref="KeywordLineScanner.IsKeywordOnlyLine"/>；
    /// 行分组＝<see cref="KeywordLineScanner.GroupByLine"/>——同一实现，杜绝两处口径漂移）。
    /// 过滤形态保持既有语义：按输入 token 序过滤掉判定成立行的 token。
    /// </summary>
    private static List<Token> DropKeywordOnlyLines(string original, IReadOnlyList<Token> tokens)
    {
        var byLine = KeywordLineScanner.GroupByLine(original, tokens);
        var droppedLines = byLine
            .Where(pair => KeywordLineScanner.IsKeywordOnlyLine(pair.Value))
            .Select(pair => pair.Key)
            .ToHashSet();

        if (droppedLines.Count == 0)
        {
            return tokens.ToList();
        }

        var tokenLine = new Dictionary<Token, int>();
        foreach (var pair in byLine)
        {
            foreach (var token in pair.Value)
            {
                tokenLine[token] = pair.Key;
            }
        }

        return tokens.Where(token => !droppedLines.Contains(tokenLine[token])).ToList();
    }
}
