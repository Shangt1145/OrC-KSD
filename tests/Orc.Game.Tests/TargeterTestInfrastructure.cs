using Orc.Core;
using Orc.Game.Targeting;

namespace Orc.Game.Tests;

/// <summary>
/// mock 前端桥接（测试脚本化应答；落位＝tests/Orc.Game.Tests 内）：
/// 两个阶段入口（候选收集＋交互）均被观测与脚本化；能力面＝可观测"收到 Begin/请求描述"、脚本化触发 Complete（每槽位 refs）/Cancel、
/// 可控收集失败（同步抛出或 faulted Task）与交互异常、等待信号（驱动串行/排队断言）。
/// </summary>
internal sealed class MockTargeterBridge : ITargeterBridge
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _collectSignal = new(0);
    private readonly SemaphoreSlim _beginSignal = new(0);
    private readonly List<TargetingCollectionContext> _collectCalls = new();
    private readonly List<TargetingRequestDescription> _begins = new();
    private readonly List<ITargetingResponder> _responders = new();
    private readonly List<string> _events = new();

    /// <summary>收集脚本（null＝返回空列表）。可抛异常（同步抛出/返回 faulted Task）＝收集失败路径。</summary>
    public Func<TargetingCollectionContext, Task<IReadOnlyList<object?>>>? CollectScript { get; set; }

    /// <summary>交互脚本（null＝静默登记、不自动应答——由测试手动驱动应答器）。可抛异常＝交互异常路径。</summary>
    public Action<TargetingRequestDescription, ITargetingResponder>? InteractionScript { get; set; }

    /// <summary>已收到的收集调用（记录序快照）。</summary>
    public IReadOnlyList<TargetingCollectionContext> CollectCalls
    {
        get
        {
            lock (_sync)
            {
                return _collectCalls.ToArray();
            }
        }
    }

    /// <summary>已收到的 Begin 请求描述（记录序快照）。</summary>
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

    /// <summary>已交给前端的应答器（记录序快照；与 <see cref="Begins"/> 一一对应）。</summary>
    public IReadOnlyList<ITargetingResponder> Responders
    {
        get
        {
            lock (_sync)
            {
                return _responders.ToArray();
            }
        }
    }

    /// <summary>桥接事件日志（"collect:{id}" / "begin:{id}"；记录序——时序断言用）。</summary>
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
    public Task<IReadOnlyList<object?>> CollectCandidatesAsync(TargetingCollectionContext context)
    {
        lock (_sync)
        {
            _collectCalls.Add(context);
            _events.Add($"collect:{context.RequestId}");
        }

        _collectSignal.Release();

        return CollectScript is null
            ? Task.FromResult<IReadOnlyList<object?>>(Array.Empty<object?>())
            : CollectScript(context);
    }

    /// <inheritdoc />
    public void BeginInteraction(TargetingRequestDescription description, ITargetingResponder responder)
    {
        lock (_sync)
        {
            _begins.Add(description);
            _responders.Add(responder);
            _events.Add($"begin:{description.RequestId}");
        }

        _beginSignal.Release();
        InteractionScript?.Invoke(description, responder);
    }

    /// <summary>等待下一次 Begin（含超时保护；返回最近一次的描述与应答器）。</summary>
    public async Task<(TargetingRequestDescription Description, ITargetingResponder Responder)> WaitForNextBeginAsync(int timeoutMs = 5000)
    {
        if (!await _beginSignal.WaitAsync(timeoutMs))
        {
            throw new TimeoutException("等待桥接 Begin 超时（交互未按预期发生）。");
        }

        lock (_sync)
        {
            return (_begins[^1], _responders[^1]);
        }
    }

    /// <summary>等待下一次收集调用（含超时保护；返回最近一次的上下文）。</summary>
    public async Task<TargetingCollectionContext> WaitForNextCollectAsync(int timeoutMs = 5000)
    {
        if (!await _collectSignal.WaitAsync(timeoutMs))
        {
            throw new TimeoutException("等待桥接收集调用超时。");
        }

        lock (_sync)
        {
            return _collectCalls[^1];
        }
    }
}

/// <summary>Targeter 测试工具（数据构造与常用脚本）。</summary>
internal static class TargeterTestKit
{
    /// <summary>候选列表（弱类型容器；可混入 null/非引用模拟脏数据）。</summary>
    public static IReadOnlyList<object?> Candidates(params object?[] items) => items;

    /// <summary>按槽位组织的提交（单槽位一组引用）。</summary>
    public static Dictionary<string, IReadOnlyList<Ref<Entity>>> Selection(string slotName, params Ref<Entity>[] refs)
        => new() { [slotName] = refs };

    /// <summary>首个（有效）槽位名——未声明槽位时＝缺省槽位名。</summary>
    public static string PrimarySlot(TargetingRequestDescription description) => description.Slots[0].Name;

    /// <summary>自动应答脚本：立即选第一个允许项完成（单槽位形态）。</summary>
    public static Action<TargetingRequestDescription, ITargetingResponder> AutoCompleteWithFirstAllowed()
        => (description, responder) =>
            responder.Complete(description.RequestId, Selection(PrimarySlot(description), description.AllowedTargets[0]));

    /// <summary>从内存留痕断言辅助：是否含带指定关键词的条目。</summary>
    public static bool HasKeyword(InMemoryTargetingTrace trace, string keyword)
        => trace.Entries.Any(e => e.Keywords.Contains(keyword, StringComparer.Ordinal));
}
