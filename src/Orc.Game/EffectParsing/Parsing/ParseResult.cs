using Orc.Game.EffectParsing.Dsl;

namespace Orc.Game.EffectParsing.Parsing;

/// <summary>未解析记录（R8 显式失败：原文出处 + 原因；**不产出占位效果**）。</summary>
/// <param name="Start">原文起始偏移。</param>
/// <param name="Length">原文长度。</param>
/// <param name="RawText">原文片段。</param>
/// <param name="Reason">失败原因。</param>
public sealed record UnresolvedRecord(int Start, int Length, string RawText, string Reason);

/// <summary>解析结果：效果数组 + 未解析记录（同一调用成对返回）。</summary>
public sealed class ParseResult
{
    /// <summary>创建解析结果。</summary>
    public ParseResult(IReadOnlyList<DslEffectInstance> effects, IReadOnlyList<UnresolvedRecord> unresolved)
    {
        Effects = effects;
        Unresolved = unresolved;
    }

    /// <summary>解析出的效果（一卡多效果＝数组）。</summary>
    public IReadOnlyList<DslEffectInstance> Effects { get; }

    /// <summary>未解析记录。</summary>
    public IReadOnlyList<UnresolvedRecord> Unresolved { get; }
}
