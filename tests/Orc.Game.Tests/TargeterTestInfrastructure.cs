using Orc.Core;
using Orc.Game.Targeting;

namespace Orc.Game.Tests;

// ---------------------------------------------------------------------------
// 测试侧仿真层：把新契约（逐个取选择器 + 语义事件提交）包装成旧测试惯用的
// "请求描述 + 应答器（Complete/Cancel）"形态，使既有用例改动最小。
// 说明：仿真只覆盖"单选择器/逐步交互"场景；多槽位一次性提交等旧语义需重写用例。
// ---------------------------------------------------------------------------

/// <summary>伪槽位描述（测试侧仿真；由选择器呈现数据映射）。</summary>
internal sealed class FakeSlotDescription
{
    internal FakeSlotDescription(
        string name,
        TargetSlotKind kind,
        TargetSlotPresentation presentation,
        int min,
        int max,
        IReadOnlyList<Ref<Entity>>? allowedReferences,
        object? parameter,
        bool hasParameter)
    {
        Name = name;
        Kind = kind;
        Presentation = presentation;
        Min = min;
        Max = max;
        AllowedReferences = allowedReferences;
        Parameter = parameter;
        HasParameter = hasParameter;

    }

    /// <summary>槽位名（＝选择器类型名）。</summary>
    public string Name { get; }

    /// <summary>槽位种类（按选择器类型名映射）。</summary>
    public TargetSlotKind Kind { get; }

    /// <summary>呈现形态（按选择器类型名映射）。</summary>
    public TargetSlotPresentation Presentation { get; }

    /// <summary>至少须选到的个数。</summary>
    public int Min { get; }

    /// <summary>至多可选的个数。</summary>
    public int Max { get; }

    /// <summary>允许引用集（非引用类＝null）。</summary>
    public IReadOnlyList<Ref<Entity>>? AllowedReferences { get; }

    /// <summary>槽位参数（＝选择器呈现参数）。</summary>
    public object? Parameter { get; }

    /// <summary>是否携带参数。</summary>
    public bool HasParameter { get; }

    /// <summary>选项条目（选项槽位；仿真仅按标识补齐）。</summary>
    public IReadOnlyList<OptionEntry>? Options { get; internal set; }

    /// <summary>卡牌名单（卡牌选择器·名单形态；仿真仅按标识补齐）。</summary>
    public IReadOnlyList<CardListing>? CardListings { get; internal set; }
}

/// <summary>伪请求描述（测试侧仿真）。</summary>
internal sealed class TargetingRequestDescription
{
    internal TargetingRequestDescription(
        string requestId,
        IReadOnlyList<Ref<Entity>> allowedTargets,
        IReadOnlyList<FakeSlotDescription> slots)
    {
        RequestId = requestId;
        AllowedTargets = allowedTargets;
        Slots = slots;
    }

    /// <summary>请求标识（＝选择器实例 Id）。</summary>
    public string RequestId { get; }

    /// <summary>允许目标（＝候选；非引用类＝空）。</summary>
    public IReadOnlyList<Ref<Entity>> AllowedTargets { get; }

    /// <summary>槽位描述（单个——每选择器一个）。</summary>
    public IReadOnlyList<FakeSlotDescription> Slots { get; }
}

/// <summary>伪提交元素（测试侧仿真；对应旧类别化提交面）。</summary>
internal sealed class TargetSelection
{
    private TargetSelection(Ref<Entity>? reference, string? identifier)
    {
        Reference = reference;
        Identifier = identifier;
    }

    /// <summary>引用元素。</summary>
    public Ref<Entity>? Reference { get; }

    /// <summary>标识元素。</summary>
    public string? Identifier { get; }

    /// <summary>创建引用元素。</summary>
    public static TargetSelection FromReference(Ref<Entity>? reference) => new(reference, null);

    /// <summary>创建标识元素。</summary>
    public static TargetSelection FromIdentifier(string? identifier) => new(null, identifier);
}

/// <summary>伪应答器（测试侧仿真）：把 Complete/Cancel 映射为语义事件提交。</summary>
internal sealed class FakeResponder
{
    private readonly ISelectorInstance _selector;

    internal FakeResponder(ISelectorInstance selector)
    {
        _selector = selector;
    }

    /// <summary>关联的选择器实例。</summary>
    public ISelectorInstance Selector => _selector;

    /// <summary>提交（引用面）。</summary>
    public bool Complete(string requestId, IReadOnlyDictionary<string, IReadOnlyList<Ref<Entity>>> selectionsBySlot)
    {
        var references = new List<Ref<Entity>>();
        if (selectionsBySlot is not null)
        {
            foreach (var pair in selectionsBySlot)
            {
                if (pair.Value is not null)
                {
                    references.AddRange(pair.Value);
                }
            }
        }

        return Submit(references, Array.Empty<string>());
    }

    /// <summary>提交（类别化面）。</summary>
    public bool Complete(string requestId, IReadOnlyDictionary<string, IReadOnlyList<TargetSelection>> selectionsBySlot)
    {
        var references = new List<Ref<Entity>>();
        var identifiers = new List<string>();
        if (selectionsBySlot is not null)
        {
            foreach (var pair in selectionsBySlot)
            {
                if (pair.Value is null)
                {
                    continue;
                }

                foreach (var element in pair.Value)
                {
                    if (element?.Reference is { } reference)
                    {
                        references.Add(reference);
                    }
                    else if (element?.Identifier is { } identifier)
                    {
                        identifiers.Add(identifier);
                    }
                }
            }
        }

        return Submit(references, identifiers);
    }

    /// <summary>取消。</summary>
    public bool Cancel(string requestId) => _selector.Submit(new CancelEvent());

    private bool Submit(IReadOnlyList<Ref<Entity>> references, IReadOnlyList<string> identifiers)
    {
        if (_selector.Mode == SelectorInteractionMode.Drag)
        {
            return _selector.Submit(new DropEvent(references.Count > 0 ? references[0] : null));
        }

        return _selector.Submit(new PickEvent(references, identifiers));
    }
}

/// <summary>
/// mock 前端桥接（测试脚本化应答）：实现 <see cref="ITargeterBridge"/>——后台拉取会话的选择器，
/// 为每个选择器构造（伪描述 + 伪应答器）并交给 <see cref="InteractionScript"/> 或等待测试驱动。
/// </summary>
internal sealed class MockTargeterBridge : ITargeterBridge
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _beginSignal = new(0);
    private readonly SemaphoreSlim _selectorSignal = new(0);
    private readonly Queue<ISelectorInstance> _selectors = new();
    private readonly List<TargetingRequestDescription> _begins = new();
    private readonly List<FakeResponder> _responders = new();
    private readonly List<string> _events = new();

    /// <summary>交互脚本（null＝静默登记、由测试手动驱动应答器）。</summary>
    public Action<TargetingRequestDescription, FakeResponder>? InteractionScript { get; set; }

    /// <summary>无人应答兜底（毫秒；超时＝自动取消——避免旧用例在新契约下永久挂起）。</summary>
    public int SelectorTimeoutMs { get; set; } = 2000;

    /// <summary>收集脚本（新契约不再有候选收集——Q19＝a；保留属性仅为兼容旧测试书写）。</summary>
    public Func<object, Task<IReadOnlyList<object?>>>? CollectScript { get; set; }

    /// <summary>收集调用记录（新契约恒为空）。</summary>
    public IReadOnlyList<object> CollectCalls => Array.Empty<object>();

    /// <summary>已收到的 Begin（伪描述；记录序）。</summary>
    public IReadOnlyList<TargetingRequestDescription> Begins
    {
        get
        {
            lock (_sync)
            {
                return _begins.ToArray();
            }
        }
    }

    /// <summary>已交给前端的应答器（记录序；与 <see cref="Begins"/> 一一对应）。</summary>
    public IReadOnlyList<FakeResponder> Responders
    {
        get
        {
            lock (_sync)
            {
                return _responders.ToArray();
            }
        }
    }

    /// <summary>桥接事件日志（"begin:{id}"；记录序）。</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void BeginTargeting(ITargeterSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _ = PumpAsync(session);
    }

    private async Task PumpAsync(ITargeterSession session)
    {
        try
        {
            while (true)
            {
                var selector = await session.NextAsync().ConfigureAwait(false);
                if (selector is null)
                {
                    return;
                }

                var presentation = selector.Presentation;
                var (kind, mark) = MapKind(presentation.SelectorName);

                // 仿真标签：还原业务槽位名（旧断言依据）与呈现参数。
                var slotName = presentation.SelectorName;
                var parameter = presentation.Parameter;
                if (presentation.Parameter is LegacyTag legacyTag)
                {
                    slotName = legacyTag.Name;
                    parameter = legacyTag.Value;
                }

                var isNonReference = kind == TargetSlotKind.OptionSelect || presentation.SelectorName == SelectorNames.CardPicker;
                var slot = new FakeSlotDescription(
                    slotName,
                    kind,
                    mark,
                    presentation.Min,
                    presentation.Max,
                    isNonReference ? null : presentation.Candidates,
                    parameter,
                    parameter is not null);

                if (presentation.Identifiers is { Count: > 0 } identifiers)
                {
                    if (kind == TargetSlotKind.OptionSelect)
                    {
                        slot.Options = identifiers.Select(id => new OptionEntry(id, id)).ToArray();
                    }
                    else
                    {
                        slot.CardListings = identifiers.Select(id => new CardListing(id, id)).ToArray();
                    }
                }

                var description = new TargetingRequestDescription(selector.Id, presentation.Candidates, new[] { slot });
                var responder = new FakeResponder(selector);

                lock (_sync)
                {
                    _begins.Add(description);
                    _responders.Add(responder);
                    _events.Add($"begin:{selector.Id}");
                }

                lock (_sync)
                {
                    _selectors.Enqueue(selector);
                }

                _selectorSignal.Release();
                _beginSignal.Release();
                _ = AutoCancelAsync(selector);
                InteractionScript?.Invoke(description, responder);
            }
        }
        catch
        {
            // 拉取异常静默：终局经会话 Result 表达。
        }
    }

    private static (TargetSlotKind Kind, TargetSlotPresentation Mark) MapKind(string selectorName) => selectorName switch
    {
        SelectorNames.Hand => (TargetSlotKind.HandSelect, TargetSlotPresentation.HandSelect),
        SelectorNames.Mulligan => (TargetSlotKind.MulliganSelect, TargetSlotPresentation.MulliganSelect),
        SelectorNames.Option => (TargetSlotKind.OptionSelect, TargetSlotPresentation.OptionList),
        SelectorNames.CardPicker => (TargetSlotKind.CardPicker, TargetSlotPresentation.CardArray),
        SelectorNames.CardPickerReference => (TargetSlotKind.CardPicker, TargetSlotPresentation.CardArray),
        SelectorNames.FieldUnit => (TargetSlotKind.SingleSelect, TargetSlotPresentation.TargetPoints),
        SelectorNames.UnitHandDrag => (TargetSlotKind.SingleSelect, TargetSlotPresentation.TargetPoints),
        _ => (TargetSlotKind.SingleSelect, TargetSlotPresentation.TargetPoints),
    };

    private async Task AutoCancelAsync(ISelectorInstance selector)
    {
        await Task.Delay(SelectorTimeoutMs).ConfigureAwait(false);
        if (!selector.IsCompleted)
        {
            selector.Submit(new CancelEvent());
        }
    }

    /// <summary>等待下一个选择器（真契约写法；含超时保护）。</summary>
    public async Task<ISelectorInstance> WaitForNextSelectorAsync(int timeoutMs = 5000)
    {
        if (!await _selectorSignal.WaitAsync(timeoutMs).ConfigureAwait(false))
        {
            throw new TimeoutException("等待选择器超时（交互未按预期发生）。");
        }

        lock (_sync)
        {
            return _selectors.Dequeue();
        }
    }

    /// <summary>等待下一次 Begin（含超时保护；返回最近一次的描述与应答器）。</summary>
    public async Task<(TargetingRequestDescription Description, FakeResponder Responder)> WaitForNextBeginAsync(int timeoutMs = 5000)
    {
        if (!await _beginSignal.WaitAsync(timeoutMs).ConfigureAwait(false))
        {
            throw new TimeoutException("等待桥接 Begin 超时（交互未按预期发生）。");
        }

        lock (_sync)
        {
            return (_begins[^1], _responders[^1]);
        }
    }

    /// <summary>等待下一次收集调用（新契约不再有候选收集——Q19＝a）。</summary>
    public Task<object> WaitForNextCollectAsync(int timeoutMs = 5000)
        => throw new InvalidOperationException("新契约不再有候选收集（Q19＝a）；该用例需重写。");
}

/// <summary>Targeter 测试工具（数据构造与常用脚本）。</summary>
internal static class TargeterTestKit
{
    /// <summary>候选列表（弱类型容器；可混入 null/非引用模拟脏数据）。</summary>
    public static IReadOnlyList<object?> Candidates(params object?[] items) => items;

    /// <summary>按槽位组织的提交（单槽位一组引用）。</summary>
    public static Dictionary<string, IReadOnlyList<Ref<Entity>>> Selection(string slotName, params Ref<Entity>[] refs)
        => new() { [slotName] = refs };

    /// <summary>按槽位组织的统一提交（单槽位一组引用元素）。</summary>
    public static Dictionary<string, IReadOnlyList<TargetSelection>> ReferenceSelection(string slotName, params Ref<Entity>[] refs)
        => new() { [slotName] = refs.Select(TargetSelection.FromReference).ToArray() };

    /// <summary>按槽位组织的统一提交（单槽位一组标识元素）。</summary>
    public static Dictionary<string, IReadOnlyList<TargetSelection>> IdentifierSelection(string slotName, params string[] identifiers)
        => new() { [slotName] = identifiers.Select(TargetSelection.FromIdentifier).ToArray() };

    /// <summary>首个（有效）槽位名。</summary>
    public static string PrimarySlot(TargetingRequestDescription description) => description.Slots[0].Name;

    /// <summary>自动应答脚本：立即选第一个允许项完成（单槽位形态）。</summary>
    public static Action<TargetingRequestDescription, FakeResponder> AutoCompleteWithFirstAllowed()
        => (description, responder) =>
            responder.Complete(description.RequestId, Selection(PrimarySlot(description), description.AllowedTargets[0]));

    /// <summary>从内存留痕断言辅助：是否含带指定关键词的条目。</summary>
    public static bool HasKeyword(InMemoryTargetingTrace trace, string keyword)
        => trace.Entries.Any(e => e.Keywords.Contains(keyword, StringComparer.Ordinal));
}
