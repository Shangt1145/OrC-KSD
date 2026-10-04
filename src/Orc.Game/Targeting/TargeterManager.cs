using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 目标选择管理器（对局管理器群"第六员"）：全局 FIFO 串行队列——一次只执行一个 targeting，完成/取消后出队下一条；防冲突（并发请求＝排队、不拒绝）。
/// 获取：经 Match 公开面读取（同既有模式）；支持独立构造（注入桥接）供测试与脱离对局场景；多 Manager 实例互不干扰（各自队列、各自桥接）。
/// 桥接与留痕：桥接构造注入（可缺省——允许无桥接装配，Targeting 被调用时以失败结局暴露〔未装配〕、不抛）；
/// 留痕对局场景＝引擎既有渠道（事件流），独立构造＝注入优先、未注入时降级为内存留痕（可查询面）。
/// 失败与违规不使队列崩溃（结局经结果对象表达；队列继续出队下一条）；留痕覆盖所有失败与违规路径。
/// 槽位类别（三类）与收集/筛选链协作：既有普通引用类（单选/多选——前端收集＋筛选取允许集）／
/// 新引用类（手牌选择/卡牌选择器〔引用集形态〕——构造方允许集、不经收集与筛选链）／非引用类（选项/卡牌名单——
/// 声明·载荷集合、不经收集与筛选链）；组合为框架一般能力（凡类别允许的槽位数组组合皆可；名字互不重复）；
/// 含收集需求的请求收集恰一次（产物仅服务既有引用类）；纯新槽位请求不要求桥接收集调用。
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

    /// <summary>
    /// 终局门禁提供器（后置项 B 加性；装配方〔对局〕注入——读取对局是否已结束）：
    /// 已结束＝发起（新入队）即时失败（<see cref="TargetingEndReason.GameEnded"/>）、零副作用（不进队列、不调桥接）。
    /// null＝无门禁（独立构造场景，行为不变）。
    /// </summary>
    internal Func<bool>? GameEndedProvider { get; set; }

    /// <summary>创建请求对象（便捷入口；与直接构造 <see cref="Targeter"/> 等价）。</summary>
    public Targeter CreateTargeter(TargetFilter? filter = null, IEnumerable<TargetSlot>? slots = null, TargetingRequestContext? context = null)
        => new(this, filter, slots, context);

    /// <summary>
    /// 入队（Targeter 发起入口；框架内部）。FIFO＝发起顺序；终局（成功/取消/失败）后出队下一条。
    /// 返回的 Task 永不故障——三态结局经结果对象表达。
    /// 终局门禁（后置项 B）：对局已结束＝即时失败（不进队列、不调桥接、零副作用）。
    /// </summary>
    internal Task<TargetingResult> Enqueue(Targeter targeter)
    {
        if (GameEndedProvider?.Invoke() == true)
        {
            return Task.FromResult(TargetingResult.Failed(TargetingEndReason.GameEnded));
        }

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
    /// 单个请求的执行：出队执行 →【收集需求分支】含既有引用类槽位（单选/多选）＝经桥接「候选收集」入口取得完整引用列表
    /// （执行时收集、保证新鲜度）→ 规范化（null/非引用剔除；失效引用保留——有效性判定归筛选）→ 粗筛（批量）→ 细筛（逐项）；
    /// 全由「无收集需求槽位」（新引用类／非引用类）组成时＝不要求桥接收集调用、收集/规范化/筛选链不适用（筛选器缺省/忽略）→
    /// 【空可用集判定】既有引用类＝筛选后最终允许集为空、新引用类＝构造方允许集快照为空（任一必须非空槽位空＝整个请求失败、
    /// 不进交互；非引用类为空＝构造期错误〔前置〕）→ 经桥接「交互」入口 Begin（描述分类承载：既有引用＝筛后允许子集／
    /// 新引用＝构造方允许集快照／非引用＝声明·载荷集合）→ await 终局（Complete/Cancel/失败）→ 统一结果对象。
    /// 全程不抛（异常＝统一失败模式、留痕、队列继续）。
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
        var effectiveSlots = BuildEffectiveSlots(targeter.Slots);

        // 收集需求判定：仅有既有引用类槽位（单选/多选）需要前端收集；全由"无收集需求槽位"（新引用类/非引用类）
        // 组成时不要求桥接收集调用（复用交互链路＝复用后段：Begin→等待→提交→终局校验）。
        var needsCollection = effectiveSlots.Any(s => s.Kind is TargetSlotKind.SingleSelect or TargetSlotKind.MultiSelect);

        IReadOnlyList<Ref<Entity>> allowed;
        if (needsCollection)
        {
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
            IReadOnlyList<Ref<Entity>> fineResult;
            var fine = targeter.Filter?.FineFilter;
            if (fine is null)
            {
                fineResult = coarseResult;
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

                fineResult = list;
            }

            // ---- ⑤ 空可用集判定（筛选完成后最终允许集为空；粗筛为空必然导致、不单独判定）----
            if (fineResult.Count == 0)
            {
                TargetingTraceLog.Write(
                    TraceSink,
                    LogLevel.Warning,
                    "Targeting 失败：无可用候选（最终允许集为空；不进入前端交互）。",
                    new[] { $"reason:{TargetingEndReason.NoAvailableCandidates}", $"requestId:{requestId}" },
                    TargetingTraceLog.Payload(requestId));

                return TargetingResult.Failed(TargetingEndReason.NoAvailableCandidates);
            }

            allowed = fineResult;
        }
        else
        {
            // 无收集需求：不调用桥接收集（筛选器缺省/忽略——无收集产物可筛）
            allowed = Array.Empty<Ref<Entity>>();
        }

        // ---- ⑤+ 新引用类槽位的运行时可用集合判定（构造方允许集快照；空＝失败、不进交互——
        //      "任一必须非空槽位空＝整个请求失败"；min=0 的空集边角对齐既有 MultiSelect 语义、不新增特例）----
        var context = targeter.Context;
        foreach (var slot in effectiveSlots)
        {
            if (!slot.IsReferenceKind || slot.Kind is TargetSlotKind.SingleSelect or TargetSlotKind.MultiSelect)
            {
                continue; // 非引用类不适用；既有引用类已由 ⑤ 覆盖
            }

            var references = context is not null && context.TryGetReferences(slot.Name, out var fetched)
                ? fetched
                : Array.Empty<Ref<Entity>>();

            if (references.Count == 0)
            {
                TargetingTraceLog.Write(
                    TraceSink,
                    LogLevel.Warning,
                    $"Targeting 失败：槽位 '{slot.Name}' 无可用候选（构造方允许集为空；不进入前端交互）。",
                    new[] { $"reason:{TargetingEndReason.NoAvailableCandidates}", $"requestId:{requestId}" },
                    TargetingTraceLog.Payload(requestId));

                return TargetingResult.Failed(
                    TargetingEndReason.NoAvailableCandidates,
                    $"槽位 '{slot.Name}' 的可用集合为空（不进交互）。");
            }
        }

        // ---- ⑥ 交互（Begin）：允许子集＋槽位描述（分类承载）＋请求标识；await 终局（Complete/Cancel）----
        var slotDescriptions = effectiveSlots
            .Select(s => BuildSlotDescription(s, context))
            .ToArray();
        var description = new TargetingRequestDescription(requestId, allowed.ToArray(), slotDescriptions);
        var interaction = new TargetingInteraction(requestId, allowed, effectiveSlots, context, TraceSink);

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

    /// <summary>
    /// 槽位描述构建（可选数据分类承载、可分别断言）：选项槽位＝声明条目（标识＋文本）；
    /// 手牌选择＝构造方允许集快照；卡牌选择器〔名单形态〕＝名单载荷（标识＋可读名称）、〔引用集形态〕＝允许集快照；
    /// 既有引用类＝null（经请求描述 AllowedTargets 交付）；呈现形态标注按槽位类型。
    /// </summary>
    private static TargetSlotDescription BuildSlotDescription(TargetSlot slot, TargetingRequestContext? context)
    {
        IReadOnlyList<Ref<Entity>>? allowedReferences = null;
        IReadOnlyList<OptionEntry>? options = null;
        IReadOnlyList<CardListing>? cardListings = null;

        switch (slot)
        {
            case OptionSelectSlot optionSlot:
                options = optionSlot.Options;
                break;
            case HandSelectSlot:
                if (context is not null && context.TryGetReferences(slot.Name, out var handReferences))
                {
                    allowedReferences = handReferences;
                }

                break;
            case CardPickerSlot picker when picker.Form == CardPickerForm.Listing:
                if (context is not null && context.TryGetListings(slot.Name, out var listings))
                {
                    cardListings = listings;
                }

                break;
            case CardPickerSlot:
                if (context is not null && context.TryGetReferences(slot.Name, out var pickerReferences))
                {
                    allowedReferences = pickerReferences;
                }

                break;
        }

        return new TargetSlotDescription(
            slot.Name,
            slot.Kind,
            slot.MinSelection,
            slot.MaxSelection,
            slot.Presentation,
            allowedReferences,
            options,
            cardListings);
    }

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
