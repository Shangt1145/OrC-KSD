using Orc.Core;
using Orc.Game;
using Orc.Game.Board;
using Orc.Game.Cards;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 2B 验收③④（部署词条与加入路径）：部署词条效果按登记序触发（A→B→C 可观测）、
/// 无组件/空 handler 跳过（条件触发语义、部署不因此失败）、单个效果异常隔离（记录并继续、不阻断部署链）、
/// 触发上下文（被部署单位＋目标槽位）；加入路径（不扣费/不走部署词条/共用单位化/仅发 unit.joined）、
/// 加入拒绝（槽位非空/重复单位化）。
/// </summary>
public class PlayChainDeploymentTests
{
    [Fact]
    public async Task Deploy_Triggers_Logic_Entries_In_Registration_Order()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var unit = await PlayChainTestKit.InstantiateLoadedAsync<UnitCard>(match, player);
        var line = match.Battlefield.PlayerASupportLine;
        var order = new List<string>();
        var contexts = new List<DeploymentLogicContext>();

        unit.AddData(new DeploymentLogicData()
            .Add("A", (context, ct) => { order.Add("A"); contexts.Add(context); return Task.CompletedTask; })
            .Add("B", (context, ct) => { order.Add("B"); contexts.Add(context); return Task.CompletedTask; })
            .Add("C", (context, ct) => { order.Add("C"); contexts.Add(context); return Task.CompletedTask; }));

        var result = await match.PlayManager.PlayUnitAsync(unit, line[1]);

        // 按登记顺序 A→B→C 触发（顺序可观测）；触发上下文＝被部署单位与目标槽位。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Equal(new[] { "A", "B", "C" }, order);
        Assert.All(contexts, context =>
        {
            Assert.Same(unit, context.Unit);
            Assert.Same(line[1], context.Target);
        });
    }

    [Fact]
    public async Task Deploy_Skips_When_No_Logic_Component()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var unit = await PlayChainTestKit.InstantiateLoadedAsync<UnitCard>(match, player);
        var line = match.Battlefield.PlayerASupportLine;

        // 无部署逻辑组件＝跳过词条效果、仍继续单位化与扣费（部署不因此失败）。
        var result = await match.PlayManager.PlayUnitAsync(unit, line[1]);

        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Same(unit, line[1].Occupant);
        Assert.Equal(0, player.Points);
    }

    [Fact]
    public async Task Deploy_Skips_Empty_Handlers_And_Triggers_Valid_Ones()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var unit = await PlayChainTestKit.InstantiateLoadedAsync<UnitCard>(match, player);
        var line = match.Battlefield.PlayerASupportLine;
        var fired = new List<string>();

        unit.AddData(new DeploymentLogicData()
            .Add("空条目甲", null)
            .Add("有效条目", (context, ct) => { fired.Add("有效条目"); return Task.CompletedTask; })
            .Add("空条目乙", null));

        var result = await match.PlayManager.PlayUnitAsync(unit, line[1]);

        // handler 非空检查：空 handler 条目＝无效、跳过；仅有效条目触发。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Equal(new[] { "有效条目" }, fired);
    }

    [Fact]
    public async Task Deploy_Isolates_Single_Effect_Exception_And_Continues()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var unit = await PlayChainTestKit.InstantiateLoadedAsync<UnitCard>(match, player);
        var line = match.Battlefield.PlayerASupportLine;
        var order = new List<string>();

        unit.AddData(new DeploymentLogicData()
            .Add("A", (context, ct) => { order.Add("A"); return Task.CompletedTask; })
            .Add("B", (context, ct) => throw new InvalidOperationException("部署效果爆炸"))
            .Add("C", (context, ct) => { order.Add("C"); return Task.CompletedTask; }));
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await match.PlayManager.PlayUnitAsync(unit, line[1]);

        // 单个部署效果异常＝沿用引擎异常隔离（记录并继续）：不阻断部署链与后续效果。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Equal(new[] { "A", "C" }, order);
        Assert.Same(unit, line[1].Occupant);
        Assert.Equal(
            new[] { GameUpdates.CardPlayed, Updates.CardPlaced, GameUpdates.UnitDeployed }, // W3-A3：含放置驱动信号
            recorder.Types);
        Assert.Contains(
            match.Engine.RootStream.Entries,
            entry => entry.Source == "部署逻辑" && entry.Keywords.Contains("exception:InvalidOperationException"));
    }

    [Fact]
    public async Task Join_Unitizes_Without_Fee_Or_Deployment_Logic()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var player = match.Players[0];
        var unit = (UnitCard)match.CardLibrary.Instantiate(PlayChainTestKit.UnitCheapId); // 不加载（来源不问、归属不要求）
        var front = match.Battlefield.FrontLine[0];
        var fired = new List<string>();
        unit.AddData(new DeploymentLogicData().Add("部署词条", (context, ct) => { fired.Add("部署词条"); return Task.CompletedTask; }));
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await match.PlayManager.JoinUnitAsync(unit, front);

        // 加入路径：加入触发器 → 共用单位化；不扣费、不走部署词条；发 card.placed ×1（W3-A3：放置驱动信号）＋ unit.joined ×1（不发 card.played / unit.deployed）。
        Assert.Equal(PlayResultStatus.Success, result.Status);
        Assert.Same(unit, front.Occupant);
        Assert.Equal(1, unit.GetData<UnitStateData>().OperateCost); // 共用单位化：组件挂载（实时值＝对战组件值）
        Assert.Equal(1, player.Points); // 不扣费
        Assert.Empty(fired); // 不走部署词条
        Assert.Equal(new[] { Updates.CardPlaced, GameUpdates.UnitJoined }, recorder.Types);
        var payload = recorder.Updates[1].Payload!;
        Assert.Same(unit, payload[GameUpdates.PayloadUnit]);
        Assert.Same(front, payload[GameUpdates.PayloadPosition]);
    }

    [Fact]
    public async Task Join_Rejects_Occupied_Slot()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var unit = (UnitCard)match.CardLibrary.Instantiate(PlayChainTestKit.UnitCheapId);
        var occupied = match.Battlefield.FrontLine[1];
        occupied.Place(new object());
        using var recorder = new UpdateRecorder(match.Engine);

        var result = await match.PlayManager.JoinUnitAsync(unit, occupied);

        // 非空＝拒绝、可观测；零副作用。
        Assert.Equal(PlayResultStatus.Failed, result.Status);
        Assert.Equal(PlayFailureReason.TargetSlotOccupied, result.FailureReason);
        Assert.Empty(recorder.Updates);
        Assert.False(unit.TryGetData<UnitStateData>(out _));
    }

    [Fact]
    public async Task Join_Rejects_Already_Unitized()
    {
        var match = PlayChainTestKit.CreatePlayMatch();
        await match.Initialize();
        var unit = (UnitCard)match.CardLibrary.Instantiate(PlayChainTestKit.UnitCheapId);
        var front = match.Battlefield.FrontLine;

        Assert.Equal(PlayResultStatus.Success, (await match.PlayManager.JoinUnitAsync(unit, front[0])).Status);
        using var recorder = new UpdateRecorder(match.Engine);
        var again = await match.PlayManager.JoinUnitAsync(unit, front[1]);

        Assert.Equal(PlayResultStatus.Failed, again.Status);
        Assert.Equal(PlayFailureReason.UnitAlreadyUnitized, again.FailureReason);
        Assert.True(front[1].IsEmpty);
        Assert.Empty(recorder.Updates);
    }
}
