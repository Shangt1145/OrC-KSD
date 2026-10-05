using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>
/// 移动复验判定器（J2；单位移动验证点专属——逐点专属判定器之一）：
/// 执行前兜底复验的验证承载（原 <c>CommandManager.RevalidateMove</c> 逐字迁移）。
/// 规则：终局后拒绝；操作角色引用（单位/原槽位/目标槽位）有效性与在场状态、当前行动方、可动标记、
/// 被压制、行动费（有效值）、位置一致性、仅推进（支援线→前线空槽）、前线无存活敌方（后置项 C）。
/// 输入：refs（触发数据第一层引用收集——本验证点：单位/原槽位/目标槽位位于前位；尾部可携带触发者卡引用、不参与复验）
/// ＋subject（被判定对象＝操作单位引用——显式、Ref 形态）。
/// 装配期注入对局级只读设施（对局加载时加载、生命随对局、仅只读使用）：
/// 生命周期（终局门禁）、当前行动方提供器、战场（布局读取）、前线存活敌方查询（转发——与可用性侧单源）。
/// 无状态：不持有跨调用可变状态；改写＝moding（移动复验改写——与攻击复验相互独立）。
/// </summary>
internal sealed class MoveRevalidationJudicator : ValidationJudicator
{
    private readonly MatchLifecycle? _lifecycle;
    private readonly Func<Player?> _currentPlayerProvider;
    private readonly Battlefield _battlefield;
    private readonly Func<Player, bool> _hasLivingEnemyOnFrontLine;

    /// <summary>创建移动复验判定器（装配期注入对局级只读设施引用）。</summary>
    /// <exception cref="ArgumentNullException">currentPlayerProvider / battlefield / hasLivingEnemyOnFrontLine 为 null。</exception>
    internal MoveRevalidationJudicator(
        MatchLifecycle? lifecycle,
        Func<Player?> currentPlayerProvider,
        Battlefield battlefield,
        Func<Player, bool> hasLivingEnemyOnFrontLine)
    {
        ArgumentNullException.ThrowIfNull(currentPlayerProvider);
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(hasLivingEnemyOnFrontLine);

        _lifecycle = lifecycle;
        _currentPlayerProvider = currentPlayerProvider;
        _battlefield = battlefield;
        _hasLivingEnemyOnFrontLine = hasLivingEnemyOnFrontLine;
    }

    /// <inheritdoc />
    protected override ValidationVerdict ValidateLogic(IReadOnlyList<Ref<Entity>> refs, Ref<Entity>? subject)
    {
        // X1：refs＝触发数据第一层引用收集（插入序）——操作角色引用（Unit/OldPosition/NewPosition）位于前位，
        // 效果引发情形尾部可携带「触发者卡牌」引用（TriggerCard；不参与复验——去重后计数允许多余项）。
        if (refs.Count < 3)
        {
            return ValidationVerdict.Invalid();
        }

        if (_lifecycle?.IsEnded == true)
        {
            return ValidationVerdict.Invalid(); // 终局后移动流程拒绝（含触发器级对外入口；零副作用、状态不推进）
        }

        if (subject is null || !subject.IsAlive || subject.Value is not UnitCard unit)
        {
            return ValidationVerdict.Invalid();
        }

        if (!refs[1].IsAlive || refs[1].Value is not Slot oldSlot)
        {
            return ValidationVerdict.Invalid();
        }

        if (!refs[2].IsAlive || refs[2].Value is not Slot newSlot)
        {
            return ValidationVerdict.Invalid();
        }

        var state = unit.GetData<UnitStateData>();
        var command = unit.GetData<CommandData>();
        var owner = unit.Owner;
        var current = _currentPlayerProvider();

        if (current is null || owner is null || !ReferenceEquals(owner, current))
        {
            return ValidationVerdict.Invalid();
        }

        if (state.IsDestroyed || !command.CanMove)
        {
            return ValidationVerdict.Invalid();
        }

        // A2：被压制（不能移动或攻击——复验与可用性同源）
        if (KeywordRules.HasKeyword(unit, KeywordIds.Suppressed))
        {
            return ValidationVerdict.Invalid();
        }

        // W2b：行动费复验读「有效值」（与可用性/扣费同源——读取面统一）
        if (owner.Points < unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost))
        {
            return ValidationVerdict.Invalid();
        }

        if (!ReferenceEquals(state.Position, oldSlot))
        {
            return ValidationVerdict.Invalid();
        }

        if (!_battlefield.GetSupportLine(owner).Contains(oldSlot))
        {
            return ValidationVerdict.Invalid(); // 仅推进：源位置须为支援线
        }

        if (!_battlefield.FrontLine.Contains(newSlot) || !newSlot.IsEmpty)
        {
            return ValidationVerdict.Invalid(); // 目标须为前线空槽
        }

        if (_hasLivingEnemyOnFrontLine(owner))
        {
            return ValidationVerdict.Invalid(); // 推进前置复验（后置项 C；防御性双保险）：前线存在存活敌方单位＝拒绝
        }

        return ValidationVerdict.Valid;
    }
}
