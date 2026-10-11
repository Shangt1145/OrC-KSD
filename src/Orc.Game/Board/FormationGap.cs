using Orc.Core;

namespace Orc.Game.Board;

/// <summary>An insertion boundary in an occupied formation, including its HQ.
/// Identity belongs to one exact line snapshot. It is never a physical empty slot.</summary>
public sealed class FormationGap : Entity
{
    internal FormationGap(BattleLine line, long generation, int ordinal, object?[] positions,
        Entity? left, Entity? right) : base($"Formation gap {ordinal}")
    {
        Line = line; Generation = generation; Ordinal = ordinal; Positions = positions;
        Left = left; Right = right;
    }
    public BattleLine Line { get; }
    public int Ordinal { get; }
    public Entity? Left { get; }
    public Entity? Right { get; }
    public bool IsCurrent => Life.IsAlive && Line.IsCurrent(this);
    internal long Generation { get; }
    internal object?[] Positions { get; }
}
