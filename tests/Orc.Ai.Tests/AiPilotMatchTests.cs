using Orc.Game;
using Xunit;

namespace Orc.Ai.Tests;

[Collection("Orc.Ai")]
public sealed class AiPilotMatchTests
{
    [Fact]
    public async Task Ai_PlaysFullMatch_AndWinsByHqZero()
    {
        using var host = await AiTestHost.StartAsync();

        var steps = await host.RunAsync();

        Assert.Equal(MatchState.Ended, host.Match.State);
        Assert.Equal(MatchEndReason.HqZero, host.Match.EndReason);
        Assert.Equal(AiTestHost.Seat, host.Match.Winner?.Index);
        Assert.Equal(0, host.Match.Players[AiTestHost.OpponentSeat].HqHealth);
        Assert.True(steps < 2000, $"对局未在步数上限内结束（steps={steps}）。");
    }

    [Fact]
    public async Task Ai_DoesNothing_AfterMatchEnds()
    {
        using var host = await AiTestHost.StartAsync();
        await host.RunAsync();

        var step = await host.Pilot.StepAsync();

        Assert.False(step.Performed);
        Assert.Equal("idle", step.Kind);
    }

    [Fact]
    public async Task SameSeed_ProducesSameFinalTurn()
    {
        using var first = await AiTestHost.StartAsync(seed: 424242);
        await first.RunAsync();

        using var second = await AiTestHost.StartAsync(seed: 424242);
        await second.RunAsync();

        Assert.Equal(first.Match.TurnNumber, second.Match.TurnNumber);
        Assert.Equal(first.Match.Winner?.Index, second.Match.Winner?.Index);
        Assert.Equal(first.Match.Players[AiTestHost.OpponentSeat].HqHealth,
            second.Match.Players[AiTestHost.OpponentSeat].HqHealth);
    }
}
