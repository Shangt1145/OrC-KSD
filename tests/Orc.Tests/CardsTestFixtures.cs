#pragma warning disable CS8618 // 视图属性值由框架在绑定时提供；声明期不做初始化（视图类不应写属性初始化器）。

using Orc.Cards;
using Orc.Core;

namespace Orc.Tests;

/// <summary>
/// 场景 2『攻击时回血』效果：被动效果——OnMount 中把「回血 handler」经 Inject 注入攻击流程的 Resolve band
/// （位置语义＝攻击结算完成时：同 band 内以 priority=100 排于默认「伤害结算」事件之后、Finalize band 之前）。
/// 同时记录钩子次数/次序信号/上下文可获得性等观察点（供 D 组断言）。
/// </summary>
public sealed class AttackHealEffect : PassiveEffect
{
    private readonly LogicEngine _engine;
    private readonly int _amount;

    public int MountCount;
    public int UnmountCount;
    public bool MountSawTriggerOnBus;    // OnMount 时主触发器已挂载（挂载先行）
    public bool UnmountSawTriggerOnBus;  // OnUnmount 时主触发器仍挂载（卸载后随）
    public bool SawPlacedAtMount;        // OnMount 时单位初始化已完成（顺序契约②）
    public int? MountHp;                 // OnMount 时数据组件已可用
    public bool ExclusiveCleanupRan;     // 作者专属清理（OnUnmount）执行
    public string? UnmountHostName;      // OnUnmount 时宿主可访问（上下文可获得性）
    public int? TargetHpAtHealTime;      // 回血执行时目标 HP（伤害先行的状态观测）
    public int HealCount;

    private TriggerRegistration? _lastInjection;

    public AttackHealEffect(LogicEngine engine, int amount, string name = "攻击时回血")
        : base(name)
    {
        _engine = engine;
        _amount = amount;
    }

    protected override void OnMount()
    {
        MountCount++;
        MountSawTriggerOnBus = _engine.Bus.GetSubscribers(Updates.EffectRemoved).Contains(Name);
        SawPlacedAtMount = Host.IsPlaced;
        MountHp = Host.GetData<HealthData>().Hp;
        _lastInjection = Inject(
            _engine.AttackFlow.Trigger, "回血", AttackFlowBands.Resolve, OnAttackResolve, priority: 100);
    }

    protected override void OnUnmount()
    {
        UnmountCount++;
        ExclusiveCleanupRan = true;
        UnmountSawTriggerOnBus = _engine.Bus.GetSubscribers(Updates.EffectRemoved).Contains(Name);
        UnmountHostName = Host.Name;
        _lastInjection = null; // 专属清理：清除引用登记
    }

    public bool InjectionRefCleared => _lastInjection is null;

    private Task OnAttackResolve(AttackFlowView view, Context ctx, CancellationToken ct)
    {
        if (!ReferenceEquals(view.Source, Host))
        {
            return Task.CompletedTask; // 只响应本宿主发起的攻击（多单位隔离）
        }

        HealCount++;
        TargetHpAtHealTime = view.Target.GetData<HealthData>().Hp; // 伤害已生效的状态观测
        Host.GetData<HealthData>().Hp += _amount;
        _engine.RootStream.WriteLog(
            "测试效果/攻击时回血",
            $"回血 {_amount}（回血时目标 HP＝{TargetHpAtHealTime}）。",
            LogLevel.Info,
            new[] { "test-heal", Host.Name });
        return Task.CompletedTask;
    }
}

/// <summary>记录型被动效果：OnMount/OnUnmount 向共享 trace 追加 mark（装载/清理顺序确定性用）。</summary>
public sealed class RecordingPassiveEffect : PassiveEffect
{
    private readonly string _mark;
    private readonly List<string> _trace;

    public RecordingPassiveEffect(string name, string mark, List<string> trace)
        : base(name)
    {
        _mark = mark;
        _trace = trace;
    }

    protected override void OnMount() => _trace.Add($"{_mark}:mount");

    protected override void OnUnmount() => _trace.Add($"{_mark}:unmount");
}

/// <summary>装载失败效果：OnMount 故意抛出（钩子失败隔离用）。</summary>
public sealed class FailingMountEffect : PassiveEffect
{
    public FailingMountEffect(string name = "装载炸弹")
        : base(name)
    {
    }

    protected override void OnMount() => throw new InvalidOperationException("OnMount 故意爆炸");
}

/// <summary>清理失败效果：OnUnmount 故意抛出（钩子失败隔离用）。</summary>
public sealed class FailingUnmountEffect : PassiveEffect
{
    public FailingUnmountEffect(string name = "卸载炸弹")
        : base(name)
    {
    }

    protected override void OnUnmount() => throw new InvalidOperationException("OnUnmount 故意爆炸");
}

/// <summary>法术视图（施放用例）：施法者/目标/伤害量。</summary>
[ContextView]
public class SpellView
{
    [Read]
    public virtual Card Source { get; set; }

    [Read]
    public virtual Card Target { get; set; }

    [Mutate]
    public virtual int Amount { get; set; }
}

/// <summary>伤害法术（主动效果施放用例）：施放事件 → 对目标调用伤害结算流程（复用同一伤害结算）。</summary>
public sealed class DamageSpell : ActiveEffect<SpellView>
{
    private readonly DamageFlow _damageFlow;

    public int CastCount;
    public int MountCount;   // 主动效果不应被调用（恒 0）
    public int UnmountCount; // 主动效果不应被调用（恒 0）

    public DamageSpell(DamageFlow damageFlow, string name = "火球术")
        : base(name)
    {
        _damageFlow = damageFlow;
        CastTrigger.Register("施放", OnCast);
    }

    protected override void OnMount() => MountCount++;

    protected override void OnUnmount() => UnmountCount++;

    private Task OnCast(SpellView view, Context ctx, CancellationToken ct)
    {
        CastCount++;
        return _damageFlow.ResolveAsync(view.Source, view.Target, view.Amount, ct);
    }
}

/// <summary>S4 测试共享工具。</summary>
internal static class S4TestHelpers
{
    /// <summary>放置驱动：发射 card.placed 更新（载荷携带卡牌对象引用）。</summary>
    internal static Task Place(LogicEngine engine, Card card)
        => engine.Emit(Updates.CardPlaced, new Dictionary<string, object?> { [PayloadKeys.Card] = card });

    /// <summary>低层移除驱动：发射 effect.removed 更新（载荷携带卡牌＋效果两者）。</summary>
    internal static Task RemoveViaUpdate(LogicEngine engine, Card card, Effect effect)
        => engine.Emit(
            Updates.EffectRemoved,
            new Dictionary<string, object?> { [PayloadKeys.Card] = card, [PayloadKeys.Effect] = effect });

    /// <summary>总流条目过滤（按 keywords 全含匹配）。</summary>
    internal static LogEntry[] EntriesWith(LogicEngine engine, params string[] keywords)
        => engine.RootStream.Entries.Where(e => keywords.All(k => e.Keywords.Contains(k))).ToArray();

    /// <summary>总流条目中首个匹配的下标（未命中＝-1）。</summary>
    internal static int IndexOfEntry(LogicEngine engine, Func<LogEntry, bool> predicate)
    {
        var entries = engine.RootStream.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (predicate(entries[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
