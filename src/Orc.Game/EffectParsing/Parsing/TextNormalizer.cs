using System.Text;

namespace Orc.Game.EffectParsing.Parsing;

/// <summary>
/// 归一文本 + **归一 → 原文偏移映射**（解析段 S7，B-D4）：
/// token 的 span 一律换算回原文，故未解析记录能给出原文出处。
/// </summary>
public sealed class NormalizedText
{
    private readonly int[] _map;

    internal NormalizedText(string text, int[] map, int originalLength)
    {
        Text = text;
        _map = map;
        OriginalLength = originalLength;
    }

    /// <summary>归一文本。</summary>
    public string Text { get; }

    /// <summary>原文长度。</summary>
    public int OriginalLength { get; }

    /// <summary>把归一偏移换算为原文偏移（末位＝原文长度）。</summary>
    public int ToOriginal(int normalizedIndex) =>
        normalizedIndex >= _map.Length ? OriginalLength : _map[normalizedIndex];
}

/// <summary>
/// 文本归一（解析段预处理，I13/P1.1）：
/// 修复换行（<c>\r\n</c>/<c>\r</c> → <c>\n</c>）、全角/半角标点归一、
/// ASCII <c>.</c> 仅当**后随汉字**时视作句号。**不改动汉字**。
/// </summary>
public static class TextNormalizer
{
    /// <summary>归一化。</summary>
    /// <exception cref="ArgumentNullException">original 为 null。</exception>
    public static NormalizedText Normalize(string original)
    {
        ArgumentNullException.ThrowIfNull(original);

        var builder = new StringBuilder(original.Length);
        var map = new List<int>(original.Length);

        var i = 0;
        while (i < original.Length)
        {
            var ch = original[i];
            if (ch == '\r')
            {
                builder.Append('\n');
                map.Add(i);
                i += i + 1 < original.Length && original[i + 1] == '\n' ? 2 : 1;
                continue;
            }

            var mapped = ch switch
            {
                ',' => '，',
                ';' => '；',
                ':' => '：',
                '!' => '！',
                '?' => '？',
                '.' => IsCjk(NextOrNull(original, i)) ? '。' : '.',
                _ => ch,
            };

            builder.Append(mapped);
            map.Add(i);
            i++;
        }

        return new NormalizedText(builder.ToString(), map.ToArray(), original.Length);
    }

    /// <summary>是否标点归一的目标字符。</summary>
    private static char NextOrNull(string text, int index) =>
        index + 1 < text.Length ? text[index + 1] : '\0';

    private static bool IsCjk(char ch) => ch >= '\u4e00' && ch <= '\u9fff';
}
