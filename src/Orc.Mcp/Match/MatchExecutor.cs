using System.Text.Json.Nodes;
using Orc.Game;
using Orc.Mcp.Contracts;

namespace Orc.Mcp.Match;

/// <summary>
/// 一次对局动作的结局（执行器内部形态）。
/// </summary>
/// <param name="Status">结局：<c>completed</c>/<c>targeting</c>/<c>not-ready</c>/<c>busy</c>/<c>failed</c>。</param>
/// <param name="Payload">引擎原生结果的 JSON 形态（completed）。</param>
/// <param name="Pending">待决选择请求（targeting/busy）。</param>
/// <param name="FailureCode">失败码（受控集合）。</param>
/// <param name="FailureMessage">失败说明。</param>
/// <param name="TimedOut">本轮应答是否因超时被按取消处理。</param>
public sealed record ActionOutcome(
    string Status,
    JsonNode? Payload = null,
    PendingSelection? Pending = null,
    string? FailureCode = null,
    string? FailureMessage = null,
    bool TimedOut = false)
{
    public const string StatusCompleted = "completed";
    public const string StatusTargeting = "targeting";
    public const string StatusNotReady = "not-ready";
    public const string StatusBusy = "busy";
    public const string StatusFailed = "failed";

    public bool IsTargeting => Status == StatusTargeting;

    public static ActionOutcome Completed(JsonNode? payload = null) => new(StatusCompleted, payload);

    public static ActionOutcome Targeting(PendingSelection pending) => new(StatusTargeting, Pending: pending);

    public static ActionOutcome NotReady()
        => new(StatusNotReady, FailureCode: McpCodes.NotReady, FailureMessage: "沙盒对局尚未创建（请先调用 sandbox.create）。");

    public static ActionOutcome Busy(PendingSelection? pending)
        => new(StatusBusy, Pending: pending, FailureCode: McpCodes.PendingTargeting,
            FailureMessage: "存在未完成的选择请求（请先调用 target.submit 或 target.cancel 应答）。");

    public static ActionOutcome NoPending()
        => new(StatusFailed, FailureCode: McpCodes.InvalidArguments, FailureMessage: "当前没有待决的选择请求。");

    public static ActionOutcome Failed(string code, string message) => new(StatusFailed, FailureCode: code, FailureMessage: message);
}

/// <summary>
/// 对局执行器：承载**唯一活跃对局**，并把一切对局调用串行化到单一写者
/// （引擎全域为无锁单线程语义；"单线程"实际靠**不并发**而非**同线程**）。
/// <para>
/// 动作生命周期门禁（避免"持锁等待应答"导致的死锁）：
/// ①动作调用取锁 → 启动动作 → 等到"完成**或**进入待决" → 释放锁；
/// ②进入待决后动作 Task 仍挂在后台、执行器标记为忙：此时只允许 <c>target.*</c> 与只读工具（由服务层门禁）；
/// ③<see cref="SubmitAsync"/> 取锁 → 驱动会话 → 等到"动作完成**或**再次待决" → 释放锁（多步选择天然支持）；
/// ④待决窗口内动作调用栈已展开（挂在 <c>TaskCompletionSource</c> 上）⇒ 引擎实际空闲，只读调用不会与之并发。
/// </para>
/// </summary>
public sealed class MatchExecutor
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Orc.Game.Match? _match;
    private Task<ActionOutcome>? _active;

    public MatchExecutor(TargeterBridgeAdapter bridge)
    {
        Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    /// <summary>目标选择桥接（装配进 <c>Match</c> 构造）。</summary>
    public TargeterBridgeAdapter Bridge { get; }

    /// <summary>当前活跃对局（无＝null）。</summary>
    public Orc.Game.Match? Match => _match;

    /// <summary>是否存在活跃对局。</summary>
    public bool HasMatch => _match is not null;

    /// <summary>是否存在未完成动作（待决中）。</summary>
    public bool IsBusy => _active is { IsCompleted: false };

    /// <summary>装配活跃对局（唯一活跃对局约束：已有则拒绝）。</summary>
    public void Attach(Orc.Game.Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        if (_match is not null)
        {
            throw new InvalidOperationException("已存在活跃对局（同一进程只允许一个活跃对局——targeter 桥接无法区分多局归属）。");
        }

        _match = match;
        _active = null;
        Bridge.Reset();
    }

    /// <summary>摘除活跃对局（销毁）。返回被摘除的对局（无＝null）。</summary>
    public Orc.Game.Match? Detach()
    {
        var match = _match;
        _match = null;
        _active = null;
        Bridge.Reset();
        return match;
    }

    /// <summary>运行一次对局动作（取锁、启动、等到"完成或待决"）。</summary>
    public async Task<ActionOutcome> RunAsync(Func<Orc.Game.Match, Task<ActionOutcome>> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_match is null)
        {
            return ActionOutcome.NotReady();
        }

        if (IsBusy)
        {
            return ActionOutcome.Busy(Bridge.CurrentPending);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var match = _match;
            if (match is null)
            {
                return ActionOutcome.NotReady();
            }

            if (IsBusy)
            {
                return ActionOutcome.Busy(Bridge.CurrentPending);
            }

            Bridge.Reset();
            var task = Guard(action, match, ct);
            _active = task;

            return await WaitAsync(task).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>应答当前待决请求并推进动作（取锁、驱动会话、等到"完成或再次待决"）。</summary>
    public async Task<ActionOutcome> SubmitAsync(string sessionId, IReadOnlyList<int> picks, bool cancel, CancellationToken ct)
    {
        if (_active is null || _active.IsCompleted)
        {
            return ActionOutcome.NoPending();
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var active = _active;
            if (active is null || active.IsCompleted)
            {
                return ActionOutcome.NoPending();
            }

            if (!Bridge.Submit(sessionId, picks, cancel))
            {
                return ActionOutcome.Failed(McpCodes.InvalidArguments, "会话标识不符或已无待决请求（可能已超时按取消处理）。");
            }

            var outcome = await WaitAsync(active).ConfigureAwait(false);
            return outcome.IsTargeting ? outcome : outcome with { TimedOut = Bridge.LastSubmissionTimedOut };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>等待动作"完成或进入待决"（不忙转：每轮等待一个新的状态变更信号）。</summary>
    private async Task<ActionOutcome> WaitAsync(Task<ActionOutcome> task)
    {
        while (true)
        {
            if (task.IsCompleted)
            {
                return await task.ConfigureAwait(false);
            }

            if (Bridge.HasPending)
            {
                return ActionOutcome.Targeting(Bridge.CurrentPending!);
            }

            var change = Bridge.ChangeTask;
            var first = await Task.WhenAny(task, change).ConfigureAwait(false);
            if (ReferenceEquals(first, task))
            {
                return await task.ConfigureAwait(false);
            }
        }
    }

    private static async Task<ActionOutcome> Guard(
        Func<Orc.Game.Match, Task<ActionOutcome>> action,
        Orc.Game.Match match,
        CancellationToken ct)
    {
        try
        {
            return await action(match).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ActionOutcome.Failed(McpCodes.Cancelled, "动作被取消（调用方取消令牌）。");
        }
        catch (InvalidOperationException ex)
        {
            return ActionOutcome.Failed(McpCodes.NotReady, $"引擎拒绝（门禁/状态不符）：{ex.Message}");
        }
        catch (KeyNotFoundException ex)
        {
            return ActionOutcome.Failed(McpCodes.EngineRejected, $"引擎拒绝（引用不存在）：{ex.Message}");
        }
        catch (Exception ex)
        {
            return ActionOutcome.Failed(McpCodes.EngineRejected, $"引擎异常：{ex.GetType().Name}: {ex.Message}");
        }
    }
}
