using Orc.Game.Board;
using Orc.Game.Cards;

namespace Orc.Game.Commanding;

/// <summary>Normal movement destinations, shared by availability and execution revalidation.</summary>
internal static class CommandMovementRules
{
    public static bool IsRetreat(Battlefield board, UnitCard unit, Slot? source)
        => source is not null && board.FrontLine.Contains(source)
            && KeywordProgramRuntime.Resolve(unit, "move.retreat", 0) != 0;

    public static bool CanStart(Battlefield board, UnitCard unit, Slot? source)
        => unit.Owner is { } owner && source is not null
            && (board.GetSupportLine(owner).Contains(source) || IsRetreat(board, unit, source));

    public static IEnumerable<Slot> Destinations(Battlefield board, UnitCard unit, Slot? source)
        => IsRetreat(board, unit, source) ? board.GetSupportLine(unit.Owner!) : board.FrontLine;
}
