using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 目标选择管理器（对局管理器群"第六员"）：全局 FIFO 串行队列——一次只执行一个 targeting，完成/取消后出队下一条；防冲突（并发请求＝排队、不拒绝）。
/// 获取：经 Match 公开面读取（同既有模式）；支持独立构造（注入桥接）供测试与脱离对局场景；多 Manager 实例互不干扰（各自队列、各自桥接）。
/// 桥接与留痕：桥接构造注入（可缺省——允许无桥接装配，Targeting 被调用时以失败结局暴露〔未装配〕、不抛）；
/// 留痕对局场景＝引擎既有渠道（事件流），独立构造＝注入优先、未注入时降级为内存留痕（可查询面）。
/// 失败与违规不使队列崩溃（结局经结果对象表达；队列继续出队下一条）；留痕覆盖所有失败与违规路径。
/// </summary>
public sealed class TargeterManager
{
    private readonly object _sync = new();
    private readonly Queue<Request> _pending = new();
    private bool _loopRunning;

    /// <summary>创建管理器。</summary>
    /// <param name="bridge">前端桥接（一对一；可缺省＝允许无桥接装配，调用时以失败结局暴露）。</param>
    /// <param name="traceSink">留痕目标（注入优先；未注入时降级为内存留痕〔可查询面〕）。</param>
    public TargeterManager(ITargeterBridge? bridge = null, ITargetingTraceSink? traceSink = null)
    {
        Bridge = bridge;
        TraceSink = traceSink ?? new InMemoryTargetingTrace();
    }

    /// <summary>前端桥接（构造注入；一对一；可缺省）。</summary>
    public ITargeterBridge? Bridge { get; }

    /// <summary>留痕目标（注入优先；未注入时＝内存留痕〔<see cref="InMemoryTargetingTrace"/>，可查询面〕）。</summary>
    public ITargetingTraceSink TraceSink { get; }

    /// <summary>创建请求对象（便捷入口；与直接构造 <see cref="Targeter"/> 等价）。</summary>
    public Targeter CreateTargeter(TargetFilter? filter = null, IEnumerable<TargetSlot>? slots = null)
        => new(this, filter, slots);

    /// <summary>
    /// 入队（Targeter 发起入口；框架内部）。FIFO＝发起顺序；终局（成功/取消/失败）后出队下一条。
    /// 返回的 Task 永不故障——三态结局经结果对象表达。
    /// </summary>
    internal Task<TargetingResult> Enqueue(Targeter targeter)
    {
        lock (_sync)
        {
            var request = new Request(targeter);
            _pending.Enqueue(request);

            if (!_loopRunning)
            {
                _loopRunning = true;
                _ = RunLoopAsync();
            }

            return request.Completion.Task;
        }
    }

    /// <summary>串行执行循环：一次只执行一个 targeting；每个请求完成后唤醒等待方并出队下一条。</summary>
    private async Task RunLoopAsync()
    {
        while (true)
        {
            Request next;
            lock (_sync)
            {
                if (_pending.Count == 0)
                {
                    _loopRunning = false;
                    return;
                }

                next = _pending.Dequeue();
            }

            TargetingResult result;
            try
            {
                result = await ExecuteAsync(next.Targeter);
            }
            catch (Exception ex)
            {
                // 兜底（防御）：执行链意外异常不得破坏队列——失败结局（其他/未知）＋留痕、队列继续。
                TargetingTraceLog.Write(
                    TraceSink,
                    LogLevel.Error,
                    $"targeting 执行链意外异常（兜底处理）：{ex.Message}。",
                    new[] { $"reason:{TargetingEndReason.Other}" },
                    new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().FullName });

                result = TargetingResult.Failed(TargetingEndReason.Other, ex.Message);
            }

            next.Completion.TrySetResult(result);
        }
    }

    /// <summary>
    /// 单个请求的执行：出队执行 → 经桥接「候选收集」入口取得完整引用列表（执行时收集、保证新鲜度）→ 规范化（null/非引用剔除；
    /// 失效引用保留——有效性判定归筛选）→ 粗筛（批量）→ 细筛（逐项）→ 最终允许集为空＝失败（无可用候选、不进交互）→
    /// 经桥接「交互」入口 Begin → await 终局（Complete/Cancel/失败）→ 统一结果对象。全程不抛（异常＝统一失败模式、留痕、队列继续）。
    /// </summary>
    private async Task<TargetingResult> ExecuteAsync(Targeter targeter)
    {
        var bridge = Bridge;
        if (bridge is null)
        {
            TargetingTraceLog.Write(
                TraceSink,
                LogLevel.Error,
                "Targeting 失败：未装配桥接（允许无桥接装配，调用时以失败结局暴露；不抛）。",
                new[] { $"reason:{TargetingEndReason.BridgeNotAssembled}" });

            return TargetingResult.Failed(TargetingEndReason.BridgeNotAssembled);
        }

        var requestId = Guid.NewGuid().ToString("N");

        // ---- ① 候选收集（每次 targeting 恰一次；出队执行时调用——候选新鲜度）----
        IReadOnlyList<object?> submitted;
        try
        {
            submitted = await bridge.CollectCandidatesAsync(new TargetingCollectionContext(requestId));
        }
        catch (Exception ex)
        {
            TargetingTraceLog.Write(
                TraceSink,
                LogLevel.Error,
                $"Targeting 失败：候选收集失败（{ex.Message}）。",
                new[] { $"reason:{TargetingEndReason.CandidateCollectionFailed}", $"requestId:{requestId}" },
                TargetingTraceLog.Payload(requestId, ex.Message));

            return TargetingResult.Failed(TargetingEndReason.CandidateCollectionFailed, ex.Message);
        }

        // ---- ② 规范化（收集后、粗筛前）：null 与非引用元素剔除单项继续（保持提交原样、不强制去重；失效引用保留）----
        var normalized = new List<Ref<Entity>>();
        if (submitted is null)
        {
            TargetingTraceLog.Write(
                TraceSink,
                LogLevel.Warning,
                "候选收集返回 null（按空列表处理；最小防御、不崩溃）。",
                new[] { $"requestId:{requestId}" },
                TargetingTraceLog.Payload(requestId));
        }
        else
        {
            foreach (var item in submitted)
            {
                if (item is Ref<Entity> reference)
                {
                    normalized.Add(reference);
                }
            }
        }

        // ---- ③ 粗筛（批量式：完整列表 → 允许子集；缺省＝全通过）----
        IReadOnlyList<Ref<Entity>> coarseResult;
        var coarse = targeter.Filter?.CoarseFilter;
        if (coarse is null)
        {
            coarseResult = normalized;
        }
        else
        {
            IReadOnlyList<Ref<Entity>> output;
            try
            {
                output = coarse(normalized);
            }
            catch (Exception ex)
            {
                return FailFilter(requestId, $"粗筛回调异常：{ex.Message}", ex.Message);
            }

            if (output is null)
            {
                return FailFilter(requestId, "粗筛回调返回 null（视为筛选失败）。", detail: null);
            }

            // 交叉过滤：粗筛结果中不属于输入的项被剔除（保持返回顺序；前端置黑语义不受影响——未返回项由前端置黑）。
            var inputSet = new HashSet<Ref<Entity>>(normalized);
            var crossed = new List<Ref<Entity>>();
            var outsideCount = 0;
            foreach (var item in output)
            {
                if (item is null)
                {
                    continue;
                }

                if (inputSet.Contains(item))
                {
                    crossed.Add(item);
                }
                else
                {
                    outsideCount++;
                }
            }

            if (outsideCount > 0)
            {
                TargetingTraceLog.Write(
                    TraceSink,
                    LogLevel.Warning,
                    $"粗筛结果含输入之外的引用（已剔除 {outsideCount} 项）。",
                    new[] { $"requestId:{requestId}" },
                    TargetingTraceLog.Payload(requestId));
            }

            coarseResult = crossed;
        }

        // ---- ④ 细筛（逐项谓词：单引用回调、在粗筛结果上逐项执行；缺省＝全通过）----
        IReadOnlyList<Ref<Entity>> allowed;
        var fine = targeter.Filter?.FineFilter;
        if (fine is null)
        {
            allowed = coarseResult;
        }
        else
        {
            var list = new List<Ref<Entity>>(coarseResult.Count);
            try
            {
                foreach (var item in coarseResult)
                {
                    if (fine(item))
                    {
                        list.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                return FailFilter(requestId, $"细筛回调异常：{ex.Message}", ex.Message);
            }

            allowed = list;
        }

        // ---- ⑤ 空可用集判定（筛选完成后最终允许集为空；粗筛为空必然导致、不单独判定）----
        if (allowed.Count == 0)
        {
            TargetingTraceLog.Write(
                TraceSink,
                LogLevel.Warning,
                "Targeting 失败：无可用候选（最终允许集为空；不进入前端交互）。",
                new[] { $"reason:{TargetingEndReason.NoAvailableCandidates}", $"requestId:{requestId}" },
                TargetingTraceLog.Payload(requestId));

            return TargetingResult.Failed(TargetingEndReason.NoAvailableCandidates);
        }

        // ---- ⑥ 交互（Begin）：允许子集＋槽位描述＋请求标识；await 终局（Complete/Cancel）----
        var effectiveSlots = BuildEffectiveSlots(targeter.Slots);
        var slotDescriptions = effectiveSlots
            .Select(s => new TargetSlotDescription(s.Name, s.Kind, s.MinSelection, s.MaxSelection))
            .ToArray();
        var description = new TargetingRequestDescription(requestId, allowed.ToArray(), slotDescriptions);
        var interaction = new TargetingInteraction(requestId, allowed, effectiveSlots, TraceSink);

        try
        {
            bridge.BeginInteraction(description, interaction);
        }
        catch (Exception ex)
        {
            TargetingTraceLog.Write(
                TraceSink,
                LogLevel.Error,
                $"Targeting 失败：桥接交互异常（{ex.Message}）。",
                new[] { $"reason:{TargetingEndReason.InteractionFault}", $"requestId:{requestId}" },
                TargetingTraceLog.Payload(requestId, ex.Message));

            return TargetingResult.Failed(TargetingEndReason.InteractionFault, ex.Message);
        }

        return await interaction.Completion;
    }

    /// <summary>筛选失败统一出口（FilterFault ＋ 留痕）。</summary>
    private TargetingResult FailFilter(string requestId, string message, string? detail)
    {
        TargetingTraceLog.Write(
            TraceSink,
            LogLevel.Error,
            $"Targeting 失败：筛选回调异常（{message}）。",
            new[] { $"reason:{TargetingEndReason.FilterFault}", $"requestId:{requestId}" },
            TargetingTraceLog.Payload(requestId, detail));

        return TargetingResult.Failed(TargetingEndReason.FilterFault, detail);
    }

    /// <summary>有效槽位：声明的槽位；未声明＝缺省槽位（单一选择语义 1..1）——扁平产出＝缺省槽位承载。</summary>
    private static IReadOnlyList<TargetSlot> BuildEffectiveSlots(IReadOnlyList<TargetSlot> declared)
        => declared.Count > 0
            ? declared
            : new TargetSlot[] { new SingleSelectSlot() };

    /// <summary>队列请求（请求对象＋终局任务）。</summary>
    private sealed class Request
    {
        internal Request(Targeter targeter)
        {
            Targeter = targeter;
            Completion = new TaskCompletionSource<TargetingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal Targeter Targeter { get; }

        internal TaskCompletionSource<TargetingResult> Completion { get; }
    }
}
