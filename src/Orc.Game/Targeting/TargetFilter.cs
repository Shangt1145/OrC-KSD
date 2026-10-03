using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 目标筛选器（一个对象含两级；两级均可缺省〔缺省＝该级全通过〕；构造 Targeter 时提供，可只给其一）：
/// 粗筛＝批量式（输入完整候选列表 → 输出其中"允许"的引用子集；不在输入中的返回项被剔除）；
/// 细筛＝逐项谓词（单引用参数回调；在粗筛结果上逐项执行）。
/// 回调由业务方编写、可自由捕获/访问游戏状态（粗筛与细筛均适用）；回调异常＝统一失败模式（失败结局、不抛、队列继续）。
/// 两级回调只见规范化（收集后、粗筛前）完成的引擎引用（<see cref="Ref{T}"/>），业务方无需面对弱类型。
/// </summary>
public sealed class TargetFilter
{
    /// <summary>创建筛选器（两级均可缺省）。</summary>
    /// <param name="coarseFilter">粗筛（批量式：完整列表 → 允许子集；null＝全通过）。</param>
    /// <param name="fineFilter">细筛（逐项谓词：单引用 → 通过与否；null＝全通过）。</param>
    public TargetFilter(
        Func<IReadOnlyList<Ref<Entity>>, IReadOnlyList<Ref<Entity>>>? coarseFilter = null,
        Func<Ref<Entity>, bool>? fineFilter = null)
    {
        CoarseFilter = coarseFilter;
        FineFilter = fineFilter;
    }

    /// <summary>粗筛回调（null＝全通过）：输入＝规范化后的完整候选列表（执行时收集、保持提交原样），输出＝其中"允许"的引用子集。</summary>
    public Func<IReadOnlyList<Ref<Entity>>, IReadOnlyList<Ref<Entity>>>? CoarseFilter { get; }

    /// <summary>细筛回调（null＝全通过）：在粗筛结果上逐项执行（单引用参数）。</summary>
    public Func<Ref<Entity>, bool>? FineFilter { get; }
}
