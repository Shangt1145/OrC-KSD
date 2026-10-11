using Orc.Core;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>Value-only UI input state. Null reader means headless engine operation.
/// UI restrictions can only narrow native legality; game rules never come from this state.</summary>
public sealed record ActionInteractionState(bool AllowsActions, IReadOnlySet<Guid> BusyEntities,
    Guid? ContinuationActorId = null);

public delegate bool ActionInputRule(Player player, Entity? actor, Entity? target);

/// <summary>Stateless admission judicator. Pulls fresh frontend state on every query/commit.
/// Registered through the usual judicator registry, with the same moding surface.</summary>
internal sealed class ActionInputJudicator : Judicator<ActionInputRule>
{
    private readonly Func<Player, ActionInteractionState?> _read;
    internal ActionInputJudicator(Func<Player, ActionInteractionState?> read) => _read = read;
    protected override Func<object[]?, object[]?> Adapt(ActionInputRule handler)
        => args => new object[] { handler(Unpack<Player>(args, 0), Unpack<Entity?>(args, 1), Unpack<Entity?>(args, 2)) };
    public override object[]? Invoke(object[]? args) => Adapt(Evaluate)(args);
    private bool Evaluate(Player player, Entity? actor, Entity? target)
    {
        var state = _read(player);
        return state is null || (state.AllowsActions || actor is not null && actor.Id == state.ContinuationActorId)
            && (actor is null || !state.BusyEntities.Contains(actor.Id));
    }
}
