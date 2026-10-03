using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>targeting 结局三态。</summary>
public enum TargetingStatus
{
    /// <summary>成功（携带产出〔只读快照〕）。</summary>
    Success,

    /// <summary>取消（前端主动 Cancel；无产出、携带原因）。</summary>
    Cancelled,

    /// <summary>失败（系统原因；无产出、携带原因，见 <see cref="TargetingEndReason"/>）。</summary>
    Failed,
}

/// <summary>
/// 结局原因（类别化；成功＝null）：
/// 取消＝<see cref="PlayerCancelled"/>；失败＝系统原因类别（无可用候选、候选收集失败、桥接交互异常、筛选回调异常、未装配、其他/未知兜底）。
/// 类别命名以本枚举为契约；不支持自由文本判断（调用方需要程序化判断与提示分流）。
/// </summary>
public enum TargetingEndReason
{
    /// <summary>玩家/前端主动取消（取消结局专用）。</summary>
    PlayerCancelled,

    /// <summary>无可用候选（筛选完成后最终允许集为空——粗筛为空必然导致、不单独判定；无细筛时即粗筛结果为空）。</summary>
    NoAvailableCandidates,

    /// <summary>候选收集失败（桥接收集入口异常／提交组装失败）。</summary>
    CandidateCollectionFailed,

    /// <summary>桥接交互异常（Begin 交付异常）。</summary>
    InteractionFault,

    /// <summary>筛选回调异常（粗筛/细筛任一回调抛出）。</summary>
    FilterFault,

    /// <summary>未装配桥接（允许无桥接装配，调用时以失败结局暴露；归"未装配/其他配置"类）。</summary>
    BridgeNotAssembled,

    /// <summary>其他/未知（兜底）。</summary>
    Other,
}

/// <summary>
/// 统一结果对象（不抛；成功标记＋原因类别，能区分三态）：成功携带产出〔只读快照〕；取消/失败无产出、携带原因。
/// 结果对象不是游戏状态修改通道：产出为只读，消费方读取/拷贝使用，不提供反向修改语义。
/// </summary>
public sealed class TargetingResult
{
    private TargetingResult(TargetingStatus status, TargetOutcome? outcome, TargetingEndReason? reason, string? detail)
    {
        Status = status;
        Outcome = outcome;
        Reason = reason;
        Detail = detail;
    }

    /// <summary>结局三态。</summary>
    public TargetingStatus Status { get; }

    /// <summary>产出（仅成功时非 null；只读快照）。</summary>
    public TargetOutcome? Outcome { get; }

    /// <summary>结局原因（成功＝null；取消＝<see cref="TargetingEndReason.PlayerCancelled"/>；失败＝系统原因类别）。</summary>
    public TargetingEndReason? Reason { get; }

    /// <summary>诊断细节（可选补充文本；程序化判断请使用 <see cref="Reason"/>）。</summary>
    public string? Detail { get; }

    /// <summary>创建成功结果（框架内部）。</summary>
    internal static TargetingResult Success(TargetOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new TargetingResult(TargetingStatus.Success, outcome, reason: null, detail: null);
    }

    /// <summary>创建取消结果（框架内部）。</summary>
    internal static TargetingResult Cancelled()
        => new(TargetingStatus.Cancelled, outcome: null, TargetingEndReason.PlayerCancelled, detail: null);

    /// <summary>创建失败结果（框架内部）。</summary>
    internal static TargetingResult Failed(TargetingEndReason reason, string? detail = null)
        => new(TargetingStatus.Failed, outcome: null, reason, detail);
}

/// <summary>
/// 产出（成功结局的只读快照）：扁平（无槽位/单槽位）＝单引用或引用列表；非扁平（多槽位）＝按槽位名组织的结构化结果。
/// 列表顺序＝前端提交原样（无额外排序承诺）；元素＝引擎引用类型（<see cref="Ref{T}"/>，同一 targeting 内元素类型统一）；
/// 多槽位＝统一候选池（同一列表经两级筛选；槽位仅约束选择数量/结构）；默认允许同一引用被多槽位同时选中。
/// </summary>
public sealed class TargetOutcome
{
    private readonly Dictionary<string, IReadOnlyList<Ref<Entity>>> _bySlot;
    private readonly IReadOnlyList<string> _slotNames;

    internal TargetOutcome(
        IReadOnlyList<string> slotNames,
        IReadOnlyDictionary<string, IReadOnlyList<Ref<Entity>>> bySlot,
        bool isFlat,
        TargetSlotKind flatKind)
    {
        _slotNames = slotNames;
        _bySlot = new Dictionary<string, IReadOnlyList<Ref<Entity>>>(StringComparer.Ordinal);
        foreach (var pair in bySlot)
        {
            _bySlot[pair.Key] = pair.Value;
        }

        IsFlat = isFlat;

        var flat = isFlat ? _bySlot[slotNames[0]] : Array.Empty<Ref<Entity>>();
        List = flat;
        Single = isFlat && flatKind == TargetSlotKind.SingleSelect && flat.Count == 1 ? flat[0] : null;
    }

    /// <summary>是否扁平产出（无槽位/单槽位声明）：扁平适用视图 [Mutate] 属性写入；非扁平（多槽位）适用按槽位读取/强类型写入。</summary>
    public bool IsFlat { get; }

    /// <summary>槽位名列表（声明序；未声明槽位/匿名槽位以缺省名 <see cref="TargetSlot.DefaultName"/> 承载）。</summary>
    public IReadOnlyList<string> SlotNames => _slotNames;

    /// <summary>
    /// 扁平单值读面（SingleSelect 语义）：扁平形态且单值选择时＝该引用；其余（多选形态/非扁平）＝null。
    /// 元素为引擎引用类型；只读快照（消费方读取/拷贝使用）。
    /// </summary>
    public Ref<Entity>? Single { get; }

    /// <summary>
    /// 扁平列表读面：扁平时＝该槽位的完整选择序列（单值形态＝单元素列表；多选形态＝选择列表〔min=0 空选成功＝空列表〕）；非扁平时＝空列表（请使用 <see cref="GetSelection"/>）。
    /// </summary>
    public IReadOnlyList<Ref<Entity>> List { get; }

    /// <summary>
    /// 按槽位名取出强类型引用（能力为需求级）：多槽位按声明名；扁平形态同样稳定可用（缺省槽位＝<see cref="TargetSlot.DefaultName"/>）。
    /// </summary>
    /// <param name="slotName">槽位名（须为声明槽位名之一；缺省槽位＝缺省名）。</param>
    /// <returns>该槽位的选择序列（只读快照；保持提交顺序）。</returns>
    /// <exception cref="KeyNotFoundException">未声明的槽位名。</exception>
    public IReadOnlyList<Ref<Entity>> GetSelection(string slotName)
    {
        ArgumentNullException.ThrowIfNull(slotName);

        if (_bySlot.TryGetValue(slotName, out var selection))
        {
            return selection;
        }

        throw new KeyNotFoundException($"产出中不存在槽位名 '{slotName}'（声明槽位：{string.Join(", ", _slotNames)}）。");
    }
}
