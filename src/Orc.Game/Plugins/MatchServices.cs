using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Effects;
using Orc.Game.Players;

namespace Orc.Game.Plugins;

/// <summary>
/// 对局只读服务面实现（游戏层内部件）：以**延后求值**访问器承载——
/// 装配期即构造、协作者（当前行动方/敌方/效果门面）尚未就绪时访问返回 null（不抛错）。
/// </summary>
internal sealed class MatchServices : IMatchServices
{
    private readonly Func<Player?> _currentPlayer;
    private readonly Func<Player, Player?> _enemyOf;
    private readonly Func<EffectRuntime?> _effectRuntime;
    private readonly Func<UnitCard, bool> _isUnitGuarded;
    private readonly Func<Hq, bool> _isHqGuarded;
    private readonly Func<UnitCard, Ref<Entity>, bool> _isAttackTargetLegal;

    internal MatchServices(
        Battlefield battlefield,
        MatchLifecycle lifecycle,
        Func<Player?> currentPlayer,
        Func<Player, Player?> enemyOf,
        Func<EffectRuntime?> effectRuntime,
        Func<UnitCard, bool> isUnitGuarded,
        Func<Hq, bool> isHqGuarded,
        Func<UnitCard, Ref<Entity>, bool> isAttackTargetLegal)
    {
        Battlefield = battlefield;
        Lifecycle = lifecycle;
        _currentPlayer = currentPlayer;
        _enemyOf = enemyOf;
        _effectRuntime = effectRuntime;
        _isUnitGuarded = isUnitGuarded;
        _isHqGuarded = isHqGuarded;
        _isAttackTargetLegal = isAttackTargetLegal;
    }

    /// <inheritdoc />
    public Battlefield Battlefield { get; }

    /// <inheritdoc />
    public MatchLifecycle Lifecycle { get; }

    /// <inheritdoc />
    public Player? CurrentPlayer => _currentPlayer();

    /// <inheritdoc />
    public Player? EnemyOf(Player owner) => _enemyOf(owner);

    /// <inheritdoc />
    public EffectRuntime? EffectRuntime => _effectRuntime();

    /// <inheritdoc />
    public bool IsUnitGuarded(UnitCard unit) => _isUnitGuarded(unit);

    /// <inheritdoc />
    public bool IsHqGuarded(Hq hq) => _isHqGuarded(hq);

    /// <inheritdoc />
    public bool IsAttackTargetLegal(UnitCard attacker, Ref<Entity> targetRef) => _isAttackTargetLegal(attacker, targetRef);
}
