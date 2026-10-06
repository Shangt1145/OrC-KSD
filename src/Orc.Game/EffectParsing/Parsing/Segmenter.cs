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
/// ① 丢弃"整行只有词条/数值"的行（P1.2）；② 按 `。！？`（硬）/`；`（软）切单元，
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

    /// <summary>丢弃整行只有词条/数值的行。</summary>
    private static List<Token> DropKeywordOnlyLines(string original, IReadOnlyList<Token> tokens)
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

        var droppedLines = byLine.Where(pair => IsKeywordOnlyLine(pair.Value)).Select(pair => pair.Key).ToHashSet();

        return droppedLines.Count == 0
            ? tokens.ToList()
            : tokens.Where(token => !droppedLines.Contains(LineOf(lineStarts, token.Start))).ToList();
    }

    private static bool IsKeywordOnlyLine(IReadOnlyList<Token> lineTokens)
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
