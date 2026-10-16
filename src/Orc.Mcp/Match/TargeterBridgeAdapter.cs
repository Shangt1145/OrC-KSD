using Orc.Core;
using Orc.Game.Targeting;

namespace Orc.Mcp.Match;

/// <summary>
/// 待决选择请求的候选条目（**会话内序号**引用——候选由引擎给定，外部不得自造）。
/// </summary>
/// <param name="Index">序号（1 起；agent 提交时使用）。</param>
/// <param name="Kind">类别（<c>slot</c>/<c>formation-gap</c>/<c>unit</c>/<c>card</c>/<c>hq</c>/<c>entity</c>）。</param>
/// <param name="ReferenceKey">该候选的引擎引用键（N 格式 GUID；懒分配——便于观察面/cross-check）。</param>
/// <param name="DisplayName">可读名（实体名；失效引用＝null）。</param>
/// <param name="IsAlive">引用是否仍存活。</param>
public sealed record PendingCandidate(
    int Index,
    string Kind,
    string ReferenceKey,
    string? DisplayName,
    bool IsAlive);

/// <summary>
/// 待决选择请求（动作挂起、等待 agent 应答期间的协议形态）。
/// </summary>
/// <param name="SessionId">会话标识（应答工具据此路由）。</param>
/// <param name="SelectorName">选择器类型名（<c>SelectorNames.*</c>）。</param>
/// <param name="Mode">交互模式（<c>Click</c>/<c>Drag</c>）。</param>
/// <param name="Min">最少选取数。</param>
/// <param name="Max">最多选取数。</param>
/// <param name="Candidates">引用类候选（会话内序号 + 引用键）。</param>
/// <param name="Identifiers">标识类候选（非引用类：选项/卡牌名单）。</param>
public sealed record PendingSelection(
    string SessionId,
    string SelectorName,
    string Mode,
    int Min,
    int Max,
    IReadOnlyList<PendingCandidate> Candidates,
    IReadOnlyList<string> Identifiers);

/// <summary>
/// 目标选择桥接适配器（<see cref="ITargeterBridge"/> 实现）。
/// <para>
/// 契约要点：<see cref="BeginTargeting"/> 是**同步方法且不得阻塞**——本实现接住会话、立即返回，
/// 由内部驱动循环（<see cref="DriveAsync"/>）逐次 <c>NextAsync</c> → 发布待决请求 → 等待 agent 提交 → <c>Submit</c>，
/// 直到 <c>NextAsync</c> 返回 null（会话结束、引擎侧 <c>Result</c> 就绪）。
/// 超时按**取消**处理（提交 <see cref="CancelEvent"/>；结果仍走引擎既有 Cancelled 三态）。
/// </para>
/// <para>
/// 与执行器的握手：任何状态变更（进入待决 / 会话结束）都经 <see cref="ChangeTask"/> 唤醒——
/// 每次变更**替换**为新信号并置位旧信号，故等待方不会忙转、也不会漏掉变更。
/// </para>
/// </summary>
public sealed class TargeterBridgeAdapter : ITargeterBridge
{
    private readonly TimeSpan _timeout;
    private readonly object _sync = new();
    private readonly Func<Ref<Entity>, string> _keyOf;

    private ITargeterSession? _session;
    private string? _sessionId;
    private PendingSelection? _pending;
    private TaskCompletionSource<bool> _change = NewSignal();
    private TaskCompletionSource<SelectorEvent>? _submit;
    private IReadOnlyList<Ref<Entity>> _currentCandidates = Array.Empty<Ref<Entity>>();
    private bool _sessionCompleted;
    private bool _timedOut;
    private Exception? _fault;

    public TargeterBridgeAdapter(TimeSpan timeout, Func<Ref<Entity>, string> keyOf)
    {
        _timeout = timeout;
        _keyOf = keyOf;
    }

    /// <summary>当前会话 id（无会话＝null）。</summary>
    public string? SessionId
    {
        get
        {
            lock (_sync)
            {
                return _sessionId;
            }
        }
    }

    /// <summary>当前是否处于"等待 agent 应答"状态。</summary>
    public bool HasPending
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null && _submit is not null;
            }
        }
    }

    /// <summary>当前待决请求（无＝null）。</summary>
    public PendingSelection? CurrentPending
    {
        get
        {
            lock (_sync)
            {
                return _pending;
            }
        }
    }

    /// <summary>会话是否已结束（诊断面用）。</summary>
    public bool SessionCompleted
    {
        get
        {
            lock (_sync)
            {
                return _sessionCompleted;
            }
        }
    }

    /// <summary>上一轮应答是否因超时被按取消处理。</summary>
    public bool LastSubmissionTimedOut
    {
        get
        {
            lock (_sync)
            {
                return _timedOut;
            }
        }
    }

    /// <summary>会话内部异常（若有；由执行器观察后上报）。</summary>
    public Exception? Fault
    {
        get
        {
            lock (_sync)
            {
                return _fault;
            }
        }
    }

    /// <summary>下一次状态变更信号（未置位；变更发生时被置位并替换为新信号）。</summary>
    public Task ChangeTask
    {
        get
        {
            lock (_sync)
            {
                return _change.Task;
            }
        }
    }

    /// <summary>清除上一轮会话状态（每次启动新动作前调用）。</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _session = null;
            _sessionId = null;
            _pending = null;
            _submit = null;
            _currentCandidates = Array.Empty<Ref<Entity>>();
            _sessionCompleted = false;
            _timedOut = false;
            _fault = null;
            _change = NewSignal();
        }
    }

    /// <inheritdoc />
    public void BeginTargeting(ITargeterSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        string sessionId;
        lock (_sync)
        {
            _session = session;
            _sessionId = sessionId = Guid.NewGuid().ToString("N");
            _pending = null;
            _submit = null;
            _sessionCompleted = false;
            _timedOut = false;
            _fault = null;
        }

        // 同步返回（契约要求不得阻塞）；驱动循环异步推进。
        _ = DriveAsync(session, sessionId);
    }

    /// <summary>
    /// 应答当前待决请求（由 <c>target.submit</c> / <c>target.cancel</c> 调用）。
    /// </summary>
    /// <returns>受理＝true；无待决请求或会话不符＝false。</returns>
    public bool Submit(string sessionId, IReadOnlyList<int> picks, bool cancel)
    {
        TaskCompletionSource<SelectorEvent>? tcs;
        IReadOnlyList<Ref<Entity>> candidates;

        lock (_sync)
        {
            if (_pending is null || _submit is null || !string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                return false;
            }

            tcs = _submit;
            candidates = _currentCandidates;
            _submit = null;
        }

        SelectorEvent evt;
        if (cancel)
        {
            evt = new CancelEvent();
        }
        else
        {
            var references = new List<Ref<Entity>>(picks.Count);
            foreach (var index in picks)
            {
                if (index < 1 || index > candidates.Count)
                {
                    // 序号越界：交回引擎判定（InvalidSelection 三态），不在此处静默丢弃。
                    return tcs.TrySetResult(new PickEvent());
                }

                references.Add(candidates[index - 1]);
            }

            evt = new PickEvent(references);
        }

        return tcs.TrySetResult(evt);
    }

    private async Task DriveAsync(ITargeterSession session, string sessionId)
    {
        try
        {
            while (true)
            {
                var selector = await session.NextAsync().ConfigureAwait(false);
                if (selector is null)
                {
                    break; // 会话结束：引擎侧 Result 就绪
                }

                var presentation = selector.Presentation;
                var submitTcs = new TaskCompletionSource<SelectorEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = BuildPending(sessionId, presentation);

                lock (_sync)
                {
                    _currentCandidates = presentation.Candidates;
                    _pending = pending;
                    _submit = submitTcs;
                }

                SignalChange(); // 唤醒执行器：动作已进入待决

                var timedOut = await Task.WhenAny(submitTcs.Task, Task.Delay(_timeout)).ConfigureAwait(false) != submitTcs.Task;

                SelectorEvent evt;
                if (timedOut)
                {
                    lock (_sync)
                    {
                        _timedOut = true;
                        _submit = null;
                        _pending = null;
                    }

                    evt = new CancelEvent(); // 超时＝取消（R2c）
                }
                else
                {
                    evt = submitTcs.Task.Result;
                    lock (_sync)
                    {
                        _pending = null;
                    }
                }

                selector.Submit(evt);
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _fault = ex;
            }
        }
        finally
        {
            lock (_sync)
            {
                _sessionCompleted = true;
                _pending = null;
                _submit = null;
            }

            SignalChange(); // 唤醒执行器：会话结束
        }
    }

    private PendingSelection BuildPending(string sessionId, SelectorPresentation presentation)
    {
        var candidates = new List<PendingCandidate>(presentation.Candidates.Count);
        for (var i = 0; i < presentation.Candidates.Count; i++)
        {
            var reference = presentation.Candidates[i];
            var alive = reference.IsAlive;
            var value = alive ? reference.Value : null;
            candidates.Add(new PendingCandidate(
                i + 1,
                KindOf(value),
                _keyOf(reference),
                alive ? reference.Name : null,
                alive));
        }

        return new PendingSelection(
            sessionId,
            presentation.SelectorName,
            presentation.Mode.ToString(),
            presentation.Min,
            presentation.Max,
            candidates,
            presentation.Identifiers ?? Array.Empty<string>());
    }

    /// <summary>候选类别归类（呈现用；不影响引擎判定）。</summary>
    private static string KindOf(object? value)
        => value switch
        {
            null => "unknown",
            Orc.Game.Board.Slot => "slot",
            Orc.Game.Board.FormationGap => "formation-gap",
            Orc.Game.Players.Hq => "hq",
            Orc.Game.Cards.UnitCard => "unit",
            Orc.Cards.Card => "card",
            _ => "entity",
        };

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>置位当前信号并替换为新信号（保证等待方既被唤醒、又不会忙转）。</summary>
    private void SignalChange()
    {
        TaskCompletionSource<bool> previous;
        lock (_sync)
        {
            previous = _change;
            _change = NewSignal();
        }

        previous.TrySetResult(true);
    }
}
