using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 交互应答器实现（请求级、引擎侧对象；由 <see cref="TargeterManager"/> 创建并随 Begin 交给前端）。
/// 契约落实：
/// ① 终局恰好一次（Complete 或 Cancel 其一）：终局后的任何调用＝幂等忽略（返回 false＋留痕，不破坏队列与后续请求）；
/// ② 配对：Complete/Cancel 回传的请求标识不匹配＝违规处理（拒绝＋留痕＋继续等待）；
/// ③ 内容不合规＝显式拒绝（不构成终局）＋留痕＋请求继续等待（前端可纠正重试或 Cancel）；不合规覆盖：
///    数量（少于 min／超过 max）、引用不在允许集、引用已失效、域判定不通过（如"已离手"）、元素类别与槽位类别不匹配、
///    标识为空/不在声明集、未声明槽位名、提交组为 null、含 null 元素；
/// ④ 域判定回调抛异常＝失败终局（系统原因类别＋留痕；不归拒绝路径——拒绝语义为"内容确定不合规"，异常＝判定无法完成）；
/// ⑤ 统一提交面：类别化元素（引用类＝引用元素；非引用类＝标识元素）；一次提交覆盖全部槽位（含混合请求、按槽位名组织）；
///    引用类便捷面（纯引用字典）等价转统一路径。
/// </summary>
internal sealed class TargetingInteraction : ITargetingResponder
{
    private static readonly HashSet<Ref<Entity>> EmptyReferenceSet = new();

    private readonly object _sync = new();
    private readonly string _requestId;
    private readonly HashSet<Ref<Entity>> _allowedSet;
    private readonly IReadOnlyList<TargetSlot> _slots;
    private readonly TargetingRequestContext? _context;
    private readonly Dictionary<string, HashSet<Ref<Entity>>> _slotAllowedSets;
    private readonly ITargetingTraceSink _trace;
    private readonly TaskCompletionSource<TargetingResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _terminal;

    internal TargetingInteraction(
        string requestId,
        IReadOnlyList<Ref<Entity>> allowedTargets,
        IReadOnlyList<TargetSlot> slots,
        TargetingRequestContext? context,
        ITargetingTraceSink trace)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestId);
        ArgumentNullException.ThrowIfNull(allowedTargets);

        if (slots is null || slots.Count == 0)
        {
            throw new ArgumentException("交互至少要有一个（有效）槽位——未声明槽位时由管理器以缺省槽位承载。", nameof(slots));
        }

        _requestId = requestId;
        _allowedSet = new HashSet<Ref<Entity>>(allowedTargets);
        _slots = slots;
        _context = context;
        _trace = trace;

        // 新引用类槽位（手牌选择／卡牌选择器〔引用集形态〕）的允许集快照（按槽位名索引；校验用）。
        // 请求构造期校验已保证这两类槽位必有引用集绑定；此处仍防御式兜底（缺失＝空集）。
        _slotAllowedSets = new Dictionary<string, HashSet<Ref<Entity>>>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (slot.IsReferenceKind && !IsCollectedKind(slot))
            {
                var references = context is not null && context.TryGetReferences(slot.Name, out var fetched)
                    ? fetched
                    : Array.Empty<Ref<Entity>>();
                _slotAllowedSets[slot.Name] = new HashSet<Ref<Entity>>(references);
            }
        }
    }

    /// <summary>终局任务（成功/取消/失败结局；由后端 await 等待）。</summary>
    internal Task<TargetingResult> Completion => _completion.Task;

    /// <inheritdoc />
    public bool Complete(string requestId, IReadOnlyDictionary<string, IReadOnlyList<Ref<Entity>>> selectionsBySlot)
    {
        // 引用类便捷面：包转为类别化元素 → 统一路径（组为 null／含 null 元素保持并在统一校验中拒绝）。
        var map = new Dictionary<string, IReadOnlyList<TargetSelection>?>(StringComparer.Ordinal);
        if (selectionsBySlot is not null)
        {
            foreach (var pair in selectionsBySlot)
            {
                if (pair.Value is null)
                {
                    map[pair.Key] = null;
                    continue;
                }

                var list = new List<TargetSelection>(pair.Value.Count);
                foreach (var reference in pair.Value)
                {
                    list.Add(TargetSelection.FromReference(reference));
                }

                map[pair.Key] = list;
            }
        }

        return CompleteCore(requestId, map);
    }

    /// <inheritdoc />
    public bool Complete(string requestId, IReadOnlyDictionary<string, IReadOnlyList<TargetSelection>> selectionsBySlot)
    {
        var map = new Dictionary<string, IReadOnlyList<TargetSelection>?>(StringComparer.Ordinal);
        if (selectionsBySlot is not null)
        {
            foreach (var pair in selectionsBySlot)
            {
                map[pair.Key] = pair.Value; // 运行时组为 null＝统一校验拒绝（防御式保留）
            }
        }

        return CompleteCore(requestId, map);
    }

    /// <inheritdoc />
    public bool Cancel(string requestId)
    {
        lock (_sync)
        {
            if (_terminal)
            {
                WriteViolation("终局后调用 Cancel（幂等忽略；终局恰好一次，不构成新终局）。", "Terminal");
                return false;
            }

            if (!string.Equals(requestId, _requestId, StringComparison.Ordinal))
            {
                WriteViolation($"请求标识不匹配（Cancel 拒绝；期望 '{_requestId}'、收到 '{requestId ?? "<null>"}'）。", "RequestIdMismatch");
                return false;
            }

            _terminal = true;
            _completion.TrySetResult(TargetingResult.Cancelled());
            return true;
        }
    }

    /// <summary>统一提交路径（两个公开重载的公共核心）：终局/配对/提交面检查 → 内容校验与产出构造 → 终局。</summary>
    private bool CompleteCore(string requestId, IReadOnlyDictionary<string, IReadOnlyList<TargetSelection>?>? selectionsBySlot)
    {
        lock (_sync)
        {
            if (_terminal)
            {
                WriteViolation("终局后调用 Complete（幂等忽略；终局恰好一次，不构成新终局）。", "Terminal");
                return false;
            }

            if (!string.Equals(requestId, _requestId, StringComparison.Ordinal))
            {
                WriteViolation($"请求标识不匹配（Complete 拒绝；期望 '{_requestId}'、收到 '{requestId ?? "<null>"}'）。", "RequestIdMismatch");
                return false;
            }

            if (selectionsBySlot is null)
            {
                WriteViolation("Complete 提交面为 null（内容不合规）。", "Content");
                return false;
            }

            if (!TryBuildOutcome(selectionsBySlot, out var outcome, out var error, out var faultReason))
            {
                if (faultReason is null)
                {
                    WriteViolation($"内容不合规（Complete 拒绝，请求继续等待）：{error}。", "Content");
                    return false;
                }

                // 域判定回调异常＝失败终局（系统原因；留痕；终局已定、队列继续出队下一条）
                TargetingTraceLog.Write(
                    _trace,
                    LogLevel.Error,
                    $"Targeting 失败：域判定回调异常（{error}）。",
                    new[] { $"reason:{faultReason.Value}", $"requestId:{_requestId}" },
                    TargetingTraceLog.Payload(_requestId, error));

                _terminal = true;
                _completion.TrySetResult(TargetingResult.Failed(faultReason.Value, error));
                return true;
            }

            _terminal = true;
            _completion.TrySetResult(TargetingResult.Success(outcome!));
            return true;
        }
    }

    /// <summary>
    /// 完整内容校验与产出构造（真实校验；不信任前端策略）——逐槽位按类别：
    /// ① 提交键须为声明槽位名（未声明键＝不合规）；缺键＝空组（由数量校验裁决）；组为 null＝不合规；
    /// ② 每槽位独立数量约束（少于 min／超过 max）；
    /// ③ 引用类：元素非 null、须为引用承载、∈ 该槽位允许集（既有引用类＝全局筛选后允许集；
    ///    新引用类＝构造方允许集快照）、仍有效（IsAlive）、域判定（绑定则同步调用——确定不合规＝拒绝；异常＝失败结局）；
    ///    非引用类：元素非 null、须为标识承载、标识非空白、∈ 声明集（选项声明条目／卡牌名单载荷）；
    /// ④ 产出：引用类按槽位名进 Ref 截面、非引用类进标识截面（类别可辨、读面分类别）；保持提交顺序的只读快照；
    ///    扁平（无/单槽位）＝单引用或引用列表（非引用类扁平＝空引用面，走标识读面）。
    /// </summary>
    private bool TryBuildOutcome(
        IReadOnlyDictionary<string, IReadOnlyList<TargetSelection>?> selectionsBySlot,
        out TargetOutcome? outcome,
        out string? error,
        out TargetingEndReason? faultReason)
    {
        outcome = null;
        error = null;
        faultReason = null;

        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in _slots)
        {
            declaredNames.Add(slot.Name);
        }

        foreach (var key in selectionsBySlot.Keys)
        {
            if (key is null || !declaredNames.Contains(key))
            {
                error = $"提交含未声明的槽位名 '{key ?? "<null>"}'";
                return false;
            }
        }

        var bySlot = new Dictionary<string, IReadOnlyList<Ref<Entity>>>(StringComparer.Ordinal);
        var identifiersBySlot = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var kindBySlot = new Dictionary<string, TargetSlotKind>(StringComparer.Ordinal);

        foreach (var slot in _slots)
        {
            kindBySlot[slot.Name] = slot.Kind;

            IReadOnlyList<TargetSelection> submitted;
            if (selectionsBySlot.TryGetValue(slot.Name, out var group))
            {
                if (group is null)
                {
                    error = $"槽位 '{slot.Name}' 的提交组为 null";
                    return false;
                }

                submitted = group;
            }
            else
            {
                submitted = Array.Empty<TargetSelection>();
            }

            if (submitted.Count < slot.MinSelection)
            {
                error = $"槽位 '{slot.Name}' 提交 {submitted.Count} 个、少于 min（{slot.MinSelection}）";
                return false;
            }

            if (submitted.Count > slot.MaxSelection)
            {
                error = $"槽位 '{slot.Name}' 提交 {submitted.Count} 个、超过 max（{slot.MaxSelection}）";
                return false;
            }

            if (slot.IsReferenceKind)
            {
                if (!TryBuildReferenceSelection(slot, submitted, out var references, out error, out faultReason))
                {
                    return false;
                }

                bySlot.Add(slot.Name, references!);
            }
            else
            {
                if (!TryBuildIdentifierSelection(slot, submitted, out var identifiers, out error))
                {
                    return false;
                }

                identifiersBySlot.Add(slot.Name, identifiers!);
            }
        }

        var slotNames = _slots.Select(s => s.Name).ToArray();
        var isFlat = _slots.Count <= 1;
        var flatKind = isFlat
            ? (_slots.Count == 1 ? _slots[0].Kind : TargetSlotKind.SingleSelect)
            : TargetSlotKind.SingleSelect; // 非扁平不使用

        outcome = new TargetOutcome(slotNames, bySlot, identifiersBySlot, kindBySlot, isFlat, flatKind);
        return true;
    }

    /// <summary>引用类槽位的选择校验与快照构造（类别、成员、有效性、域判定）。</summary>
    private bool TryBuildReferenceSelection(
        TargetSlot slot,
        IReadOnlyList<TargetSelection> submitted,
        out IReadOnlyList<Ref<Entity>> references,
        out string? error,
        out TargetingEndReason? faultReason)
    {
        references = Array.Empty<Ref<Entity>>();
        error = null;
        faultReason = null;

        var allowedSet = IsCollectedKind(slot)
            ? _allowedSet
            : (_slotAllowedSets.TryGetValue(slot.Name, out var set) ? set : EmptyReferenceSet);

        var copy = new List<Ref<Entity>>(submitted.Count);
        foreach (var element in submitted)
        {
            if (element is null)
            {
                error = $"槽位 '{slot.Name}' 含 null 元素";
                return false;
            }

            if (element.Reference is not { } reference)
            {
                error = $"槽位 '{slot.Name}' 为引用类，须提交引用元素（收到标识元素）";
                return false;
            }

            if (!allowedSet.Contains(reference))
            {
                error = $"槽位 '{slot.Name}' 的引用 '{reference.Name}' 不在允许集";
                return false;
            }

            if (!reference.IsAlive)
            {
                error = $"槽位 '{slot.Name}' 的引用 '{reference.Name}' 已失效（终局校验附加“仍有效”检查）";
                return false;
            }

            if (_context is not null && _context.TryGetDomainValidator(slot.Name, out var validator))
            {
                bool passed;
                try
                {
                    passed = validator(reference);
                }
                catch (Exception ex)
                {
                    error = $"槽位 '{slot.Name}' 的域判定回调异常：{ex.Message}";
                    faultReason = TargetingEndReason.DomainValidationFault;
                    return false;
                }

                if (!passed)
                {
                    error = $"槽位 '{slot.Name}' 的引用 '{reference.Name}' 未通过域判定（确定不合规，如“已离手”）";
                    return false;
                }
            }

            copy.Add(reference);
        }

        references = copy.ToArray();
        return true;
    }

    /// <summary>非引用类槽位的选择校验与快照构造（标识承载、非空、∈ 声明集）。</summary>
    private bool TryBuildIdentifierSelection(
        TargetSlot slot,
        IReadOnlyList<TargetSelection> submitted,
        out IReadOnlyList<string> identifiers,
        out string? error)
    {
        identifiers = Array.Empty<string>();
        error = null;

        var declaredIdentifiers = DeclaredIdentifierSet(slot);
        var copy = new List<string>(submitted.Count);
        foreach (var element in submitted)
        {
            if (element is null)
            {
                error = $"槽位 '{slot.Name}' 含 null 元素";
                return false;
            }

            if (element.Identifier is not { } identifier)
            {
                error = $"槽位 '{slot.Name}' 为非引用类，须提交标识元素（收到引用元素）";
                return false;
            }

            if (string.IsNullOrWhiteSpace(identifier))
            {
                error = $"槽位 '{slot.Name}' 的标识为空（内容不合规）";
                return false;
            }

            if (!declaredIdentifiers.Contains(identifier))
            {
                error = $"槽位 '{slot.Name}' 的标识 '{identifier}' 不在声明集";
                return false;
            }

            copy.Add(identifier);
        }

        identifiers = copy.ToArray();
        return true;
    }

    /// <summary>槽位声明集（非引用类）：选项槽位＝声明条目标识；卡牌选择器〔名单形态〕＝名单载荷标识。</summary>
    private HashSet<string> DeclaredIdentifierSet(TargetSlot slot)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        switch (slot)
        {
            case OptionSelectSlot optionSlot:
                foreach (var entry in optionSlot.Options)
                {
                    set.Add(entry.Id);
                }

                break;
            case CardPickerSlot picker when picker.Form == CardPickerForm.Listing:
                if (_context is not null && _context.TryGetListings(slot.Name, out var listings))
                {
                    foreach (var listing in listings)
                    {
                        set.Add(listing.Id);
                    }
                }

                break;
        }

        return set;
    }

    /// <summary>既有引用类（前端收集＋筛选链服务对象；允许集＝全局筛选后允许子集）。</summary>
    private static bool IsCollectedKind(TargetSlot slot)
        => slot.Kind is TargetSlotKind.SingleSelect or TargetSlotKind.MultiSelect;

    /// <summary>违规留痕（违规＝Warning 级；关键词含 violation:{类别} 与请求标识，供测试与前端观测）。</summary>
    private void WriteViolation(string message, string kind)
    {
        TargetingTraceLog.Write(
            _trace,
            LogLevel.Warning,
            message,
            new[] { $"violation:{kind}", $"requestId:{_requestId}" },
            TargetingTraceLog.Payload(_requestId));
    }
}
