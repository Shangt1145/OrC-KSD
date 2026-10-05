using Orc.Cards;
using Orc.Core;
using Orc.Game.Cards;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// S-C8/C9 游戏层接线验收：`CardEffectRegistry.DeclarePrefab` → 装载链实例化动态效果并装载；
/// 未注册预制体＝隔离记录、不阻断加载。
/// </summary>
public class PrefabEffectLoadingTests
{
    private const string DemoId = "u_prefab_demo";
    private const string Hook = "it.prefab.hook";

    private static CardDefinitionEntry DemoDefinition => new(
        DemoId,
        new CardDefinition(
            "预制体演示兵", deployCost: 1, operateCost: 1, attack: 5, defense: 4,
            unitTypes: new[] { UnitType.Infantry }, faction: Faction.Germany, rarity: Rarity.Standard));

    private static EffectSnapshot PassiveSnapshot(string handlerKey) => new(
        new EffectPrefab(
            "prefab.dyn",
            new TriggerPrefab(
                "t.main", "it.main", TriggerKind.Passive,
                hooks: new[] { Hook },
                events: new[] { new EventPrefab("onHook", assemblyKey: handlerKey) })));

    [Fact]
    public async Task 声明预制体的卡_加载时实例化动态效果并装载()
    {
        var registry = new CardEffectRegistry();
        registry.DeclarePrefab(DemoId, new[] { "prefab.dyn" });

        var match = CommandTestKit.CreateCommandMatch(
            effectRegistry: registry, extraDefinitions: new[] { DemoDefinition });
        await match.Initialize();

        var trace = new List<string>();
        match.Engine.Prefabs.RegisterHandler("it.handler", (Func<CardEventView, Context, CancellationToken, Task>)((v, c, ct) =>
        {
            trace.Add("fired");
            return Task.CompletedTask;
        }));
        match.Engine.Prefabs.RegisterPrefab(PassiveSnapshot("it.handler"));

        var demo = (UnitCard)match.CardLibrary.Instantiate(DemoId);
        await demo.LoadAsync(match.Players[0]);

        // 装载链实例化动态效果并入容器
        var effect = Assert.IsType<DynamicPassiveEffect>(Assert.Single(demo.Effects));
        Assert.True(effect.IsMounted);

        // 动态触发器挂载到总线；审查链可见
        Assert.Contains(match.Engine.Bus.EnumerateHooks(), h => h.Hook == Hook);
        Assert.Contains(match.Engine.Orchestration.BuildAuditChain().Nodes, n => n.StableKey == "it.main");

        await match.Engine.Emit(Hook);
        Assert.Equal(new[] { "fired" }, trace);
    }

    [Fact]
    public async Task 未注册预制体_隔离记录且不阻断加载()
    {
        var registry = new CardEffectRegistry();
        registry.DeclarePrefab(DemoId, new[] { "prefab.missing" });

        var match = CommandTestKit.CreateCommandMatch(
            effectRegistry: registry, extraDefinitions: new[] { DemoDefinition });
        await match.Initialize();

        var demo = (UnitCard)match.CardLibrary.Instantiate(DemoId);
        await demo.LoadAsync(match.Players[0]); // 不抛

        Assert.Empty(demo.Effects);
        Assert.Contains(
            match.Engine.RootStream.Entries,
            e => e.Message.Contains("prefab.missing", StringComparison.Ordinal));
    }

    [Fact]
    public void 预制体声明_重复拒绝()
    {
        var registry = new CardEffectRegistry();
        registry.DeclarePrefab(DemoId, new[] { "a" });

        Assert.Throws<InvalidOperationException>(() => registry.DeclarePrefab(DemoId, new[] { "b" }));
        Assert.Throws<ArgumentException>(() => registry.DeclarePrefab("x", new[] { "a", "a" }));
    }
}
