using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 产出写入辅助（类型化便捷入口；显式写入语义——业务方自行调用、框架不自动写；写入＝普通数据写入〔重复即覆盖〕）。
/// 辅助不规定固定键名/属性名：目标（如视图 [Mutate] 属性 setter）由业务方经委托指定，键名/结构完全自由。
/// 双模式覆盖：扁平（无/单槽位）＝单引用/引用列表写入；非扁平（多槽位）＝产出结构整体写入（读取时按槽位取强类型引用）。
/// 产出为只读快照——辅助只做"取出并写入"，不提供反向修改语义。
/// </summary>
public static class TargetOutcomeWriter
{
    /// <summary>
    /// 整体写入（扁平/非扁平通用）：把产出结构交给写入目标（如视图 [Mutate] 属性 setter）。
    /// 多槽位（非扁平）场景经此把产出结构整体写入 ctx 载体。
    /// </summary>
    /// <param name="result">targeting 结果（须为成功；否则抛明确异常——业务方应先判 <see cref="TargetingResult.Status"/>）。</param>
    /// <param name="write">写入目标（如 (outcome) =&gt; view.SlotOutcome = outcome）。</param>
    /// <returns>写入的产出（链式读取用）。</returns>
    /// <exception cref="ArgumentNullException">result 或 write 为 null。</exception>
    /// <exception cref="InvalidOperationException">结果非成功（无产出）。</exception>
    public static TargetOutcome WriteOutcome(TargetingResult result, Action<TargetOutcome> write)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(write);

        var outcome = RequireOutcome(result);
        write(outcome);
        return outcome;
    }

    /// <summary>
    /// 扁平单值写入：把产出的单引用写入目标（如视图 [Mutate] 属性 setter；SingleSelect 语义）。
    /// </summary>
    /// <param name="result">targeting 结果（须为成功且扁平单值形态）。</param>
    /// <param name="write">写入目标（如 (r) =&gt; view.Target = r）。</param>
    /// <returns>写入的引用。</returns>
    /// <exception cref="ArgumentNullException">result 或 write 为 null。</exception>
    /// <exception cref="InvalidOperationException">结果非成功；或产出非单值形态（多选形态/多槽位）。</exception>
    public static Ref<Entity> WriteSingle(TargetingResult result, Action<Ref<Entity>> write)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(write);

        var outcome = RequireOutcome(result);
        var single = outcome.Single
            ?? throw new InvalidOperationException(
                "产出非扁平单值形态（SingleSelect 语义），无法按单引用写入；多选形态请用 WriteList、多槽位请用 WriteOutcome。");

        write(single);
        return single;
    }

    /// <summary>
    /// 扁平列表写入：把产出的引用列表写入目标（如视图 [Mutate] 属性 setter；MultiSelect 语义；空选成功＝空列表）。
    /// </summary>
    /// <param name="result">targeting 结果（须为成功且扁平形态）。</param>
    /// <param name="write">写入目标（如 (list) =&gt; view.Targets = list）。</param>
    /// <returns>写入的列表（只读快照）。</returns>
    /// <exception cref="ArgumentNullException">result 或 write 为 null。</exception>
    /// <exception cref="InvalidOperationException">结果非成功；或产出为非扁平形态（多槽位请用 WriteOutcome/按槽位读取）。</exception>
    public static IReadOnlyList<Ref<Entity>> WriteList(TargetingResult result, Action<IReadOnlyList<Ref<Entity>>> write)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(write);

        var outcome = RequireOutcome(result);
        if (!outcome.IsFlat)
        {
            throw new InvalidOperationException("产出为非扁平形态（多槽位）；请用 WriteOutcome 整体写入后按槽位读取。");
        }

        write(outcome.List);
        return outcome.List;
    }

    /// <summary>取出成功产出；非成功结果＝明确异常（辅助为成功路径便捷，调用前应先判 Status）。</summary>
    private static TargetOutcome RequireOutcome(TargetingResult result)
        => result.Status == TargetingStatus.Success && result.Outcome is not null
            ? result.Outcome
            : throw new InvalidOperationException(
                $"结果非成功（Status={result.Status}、Reason={result.Reason}），不存在可写入的产出。");
}
