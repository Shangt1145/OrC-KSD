using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 交互应答器实现（请求级、引擎侧对象；由 <see cref="TargeterManager"/> 创建并随 Begin 交给前端）。
/// 契约落实：
/// ① 终局恰好一次（Complete 或 Cancel 其一）：终局后的任何调用＝幂等忽略（返回 false＋留痕，不破坏队列与后续请求）；
/// ② 配对：Complete/Cancel 回传的请求标识不匹配＝违规处理（拒绝＋留痕＋继续等待）；
/// ③ 内容不合规（数量少于 min/超过 max、单槽位多引用、引用不在允许集、引用已失效、未声明槽位名）＝显式拒绝（不构成终局）＋留痕＋请求继续等待（前端可纠正重试或 Cancel）。
/// </summary>
internal sealed class TargetingInteraction : ITargetingResponder
{
    private readonly object _sync = new();
    private readonly string _requestId;
    private readonly HashSet<Ref<Entity>> _allowedSet;
    private readonly IReadOnlyList<TargetSlot> _slots;
    private readonly ITargetingTraceSink _trace;
    private readonly TaskCompletionSource<TargetingResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _terminal;

    internal TargetingInteraction(
        string requestId,
        IReadOnlyList<Ref<Entity>> allowedTargets,
        IReadOnlyList<TargetSlot> slots,
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
        _trace = trace;
    }

    /// <summary>终局任务（成功/取消结局；由后端 await 等待）。</summary>
    internal Task<TargetingResult> Completion => _completion.Task;

    /// <inheritdoc />
    public bool Complete(string requestId, IReadOnlyDictionary<string, IReadOnlyList<Ref<Entity>>> selectionsBySlot)
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

            if (!TryBuildOutcome(selectionsBySlot, out var outcome, out var error))
            {
                WriteViolation($"内容不合规（Complete 拒绝，请求继续等待）：{error}。", "Content");
                return false;
            }

            _terminal = true;
            _completion.TrySetResult(TargetingResult.Success(outcome!));
            return true;
        }
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

    /// <summary>
    /// 完整内容校验与产出构造（真实校验；不信任前端策略）：
    /// ① 提交键须为声明槽位名（未声明键＝不合规）；缺键＝空组（由数量校验裁决）；
    /// ② 每槽位独立数量约束（少于 min／超过 max／单槽位多引用）；③ 每引用：非 null、∈ 允许集、仍有效（IsAlive）。
    /// 产出：保持提交顺序的只读快照；扁平（无/单槽位）＝单引用或列表；多槽位＝按槽位名组织的结构化结果。
    /// </summary>
    private bool TryBuildOutcome(
        IReadOnlyDictionary<string, IReadOnlyList<Ref<Entity>>> selectionsBySlot,
        out TargetOutcome? outcome,
        out string? error)
    {
        outcome = null;
        error = null;

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
        foreach (var slot in _slots)
        {
            IReadOnlyList<Ref<Entity>> submitted;
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
                submitted = Array.Empty<Ref<Entity>>();
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

            var copy = new List<Ref<Entity>>(submitted.Count);
            foreach (var reference in submitted)
            {
                if (reference is null)
                {
                    error = $"槽位 '{slot.Name}' 含 null 引用";
                    return false;
                }

                if (!_allowedSet.Contains(reference))
                {
                    error = $"槽位 '{slot.Name}' 的引用 '{reference.Name}' 不在允许集";
                    return false;
                }

                if (!reference.IsAlive)
                {
                    error = $"槽位 '{slot.Name}' 的引用 '{reference.Name}' 已失效（终局校验附加“仍有效”检查）";
                    return false;
                }

                copy.Add(reference);
            }

            bySlot.Add(slot.Name, copy.ToArray());
        }

        var slotNames = _slots.Select(s => s.Name).ToArray();
        var isFlat = _slots.Count <= 1;
        var flatKind = isFlat
            ? (_slots.Count == 1 ? _slots[0].Kind : TargetSlotKind.SingleSelect)
            : TargetSlotKind.SingleSelect; // 非扁平不使用

        outcome = new TargetOutcome(slotNames, bySlot, isFlat, flatKind);
        return true;
    }

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
