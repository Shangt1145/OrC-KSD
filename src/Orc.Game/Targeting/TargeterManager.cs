using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game.Targeting;

/// <summary>
/// 目标选择管理器（对局管理器群"第六员"）：全局 FIFO 串行队列（一次只执行一个 targeter；完成/取消/失败后出队下一条）。
/// 组装面＝流程函数（<see cref="RunAsync(Func{ITargeterFlow, Task{TargeterResult}})"/>）：组装方在流程内写分支。
/// 执行＝经桥接交付<b>会话</b>（前端在会话上逐个取选择器）；终局经 <see cref="TargeterResult"/> 表达、不抛。
/// 保留：FIFO 串行、终局门禁、留痕（Q3）。
/// </summary>
public sealed class TargeterManager
{
    private readonly object _sync = new();
    private readonly Queue<Request> _pending = new();
    private bool _loopRunning;

    /// <summary>创建管理器。</summary>
    /// <param name="bridge">前端桥接（一对一；可缺省＝允许无桥接装配，调用时以失败结局暴露）。</param>
    /// <param name="traceSink">留痕目标（注入优先；未注入时降级为内存留痕）。</param>
    public TargeterManager(ITargeterBridge? bridge = null, ITargetingTraceSink? traceSink = null)
    {
        Bridge = bridge;
        TraceSink = traceSink ?? new InMemoryTargetingTrace();
    }

    /// <summary>前端桥接（构造注入；一对一；可缺省）。</summary>
    public ITargeterBridge? Bridge { get; }

    /// <summary>留痕目标。</summary>
    public ITargetingTraceSink TraceSink { get; }

    /// <summary>终局门禁提供器（装配方注入；已结束＝发起即时失败、零副作用）。null＝无门禁。</summary>
    internal Func<bool>? GameEndedProvider { get; set; }

    /// <summary>发起一次 targeter（无参流程）。</summary>
    public Task<TargeterResult> RunAsync(Func<ITargeterFlow, Task<TargeterResult>> flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return Enqueue(flow);
    }

    /// <summary>发起一次 targeter（带参数流程；参数由组装方闭包使用）。</summary>
    public Task<TargeterResult> RunAsync<TParam>(Func<ITargeterFlow, TParam, Task<TargeterResult>> flow, TParam parameter)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return Enqueue(f => flow(f, parameter));
    }

    /// <summary>入队（FIFO 串行；终局门禁：对局已结束＝即时失败、零副作用）。</summary>
    internal Task<TargeterResult> Enqueue(Func<ITargeterFlow, Task<TargeterResult>> flow)
    {
        if (GameEndedProvider?.Invoke() == true)
        {
            return Task.FromResult(TargeterResult.Failed(TargeterFailureReason.GameEnded));
        }

        lock (_sync)
        {
            var request = new Request(flow);
            _pending.Enqueue(request);

            if (!_loopRunning)
            {
                _loopRunning = true;
                _ = RunLoopAsync();
            }

            return request.Completion.Task;
        }
    }

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

            TargeterResult result;
            try
            {
                result = await ExecuteAsync(next.Flow).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TargetingTraceLog.Write(
                    TraceSink,
                    LogLevel.Error,
                    $"targeter 执行链意外异常（兜底）：{ex.Message}。",
                    new[] { $"reason:{TargeterFailureReason.Other}" },
                    new Dictionary<string, object?> { ["exceptionType"] = ex.GetType().FullName });

                result = TargeterResult.Failed(TargeterFailureReason.Other, ex.Message);
            }

            next.Completion.TrySetResult(result);
        }
    }

    private async Task<TargeterResult> ExecuteAsync(Func<ITargeterFlow, Task<TargeterResult>> flow)
    {
        var bridge = Bridge;
        if (bridge is null)
        {
            TargetingTraceLog.Write(
                TraceSink,
                LogLevel.Error,
                "targeter 失败：未装配桥接（允许无桥接装配，调用时以失败结局暴露；不抛）。",
                new[] { $"reason:{TargeterFailureReason.BridgeNotAssembled}" });

            return TargeterResult.Failed(TargeterFailureReason.BridgeNotAssembled);
        }

        var session = new TargeterSession(flow, TraceSink);
        try
        {
            bridge.BeginTargeting(session);
        }
        catch (Exception ex)
        {
            TargetingTraceLog.Write(
                TraceSink,
                LogLevel.Error,
                $"targeter 失败：桥接交付异常（{ex.Message}）。",
                new[] { $"reason:{TargeterFailureReason.Fault}" },
                new Dictionary<string, object?> { ["detail"] = ex.Message });

            return TargeterResult.Failed(TargeterFailureReason.Fault, ex.Message);
        }

        return await session.Completion.ConfigureAwait(false);
    }

    /// <summary>
    /// 卡 → 目标选择管理器解析（卡经归属玩家取管理器；未加载/独立构造/非卡实体＝null）。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    public static TargeterManager? ResolveFor(Orc.Cards.Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card switch
        {
            Hq hq => hq.Owner.TargeterManager,
            CardBase cardBase => cardBase.Owner?.TargeterManager,
            _ => null,
        };
    }

    private sealed class Request
    {
        internal Request(Func<ITargeterFlow, Task<TargeterResult>> flow)
        {
            Flow = flow;
            Completion = new TaskCompletionSource<TargeterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal Func<ITargeterFlow, Task<TargeterResult>> Flow { get; }

        internal TaskCompletionSource<TargeterResult> Completion { get; }
    }
}
