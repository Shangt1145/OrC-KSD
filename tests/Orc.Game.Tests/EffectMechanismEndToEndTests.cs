using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Cards.Data;
using Orc.Game.EffectParsing;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Parsing;
using Orc.Game.EffectParsing.Templates;
using Orc.Game.Players;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// S3（老兵与隐蔽·模板效果补全）——**运行级条件求值**验收（需求 Q&A-1a／Q&A-3 层 3）：
/// ① 回合归属判定面（<see cref="TurnRules"/>）随当前行动方切换断言真/假；
/// ② V1「在场上的第三回合开始时，升为老兵」——构造"在场回合数=2/3"场景，断言条件求值假/真（升级落定）；
/// ③ C5「揭示：若是友方回合，获得 +2 攻击力」——揭示时点求值：友方回合真／敌方回合假；
///    并验证**自指守卫**（同场其它单位被揭示不误触发）。
/// </summary>
public class EffectMechanismEndToEndTests
{
    private const string GuardsId = "s3_guards";          // 2/1；BecomesVeteran（S3 E2E 基础形态）
    private const string GuardsVetId = "s3_guards_vet";   // 2/2；VeteranOf（老兵版）
    private const string ScoutId = "s3_scout";            // 2/5 步兵＋covert（C5 宿主）

    /// <summary>自含数据体 JSON 集 → 真实加载路径 → 定义集（每测例独立临时目录；加载后即删除——定义已构造）。</summary>
    private static IReadOnlyList<CardDefinitionEntry> LoadDefinitions()
    {
        var jsons = new[]
        {
            CardJson(GuardsId, "近卫测试兵", "Soviet", 1, 1, 2, 1, "infantry", $"BecomesVeteran:{GuardsVetId}"),
            CardJson(GuardsVetId, "近卫测试兵", "Soviet", 1, 1, 2, 2, "infantry", $"VeteranOf:{GuardsId}"),
            CardJson(ScoutId, "测试侦察兵", "Germany", 1, 1, 2, 5, "infantry", "covert"),
        };

        var directory = Path.Combine(Path.GetTempPath(), "orc-s3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (var i = 0; i < jsons.Length; i++)
            {
                File.WriteAllText(Path.Combine(directory, $"card{i:D2}.card.json"), jsons[i]);
            }

            var loaded = CardDataLoader.LoadDirectory(directory);
            Assert.Empty(loaded.Failures);
            return loaded.Definitions;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CardJson(
        string id, string name, string faction, int kredits, int operationCost, int attack, int defense,
        string unitType, params string[] attributes)
    {
        var attributeList = attributes.Length == 0
            ? "[]"
            : "[" + string.Join(", ", attributes.Select(a => $"\"{a}\"")) + "]";
        return $$"""
        {
          "schemaVersion": 1,
          "id": "{{id}}",
          "name": "{{name}}",
          "components": [
            { "component": "typeCategory", "type": ["{{unitType}}"] },
            { "component": "factionCost", "faction": "{{faction}}", "kredits": {{kredits}} },
            { "component": "battleStats", "operationCost": {{operationCost}}, "attack": {{attack}}, "defense": {{defense}} },
            { "component": "tagData", "rarity": "Standard" },
            { "component": "keywords", "attributes": {{attributeList}} }
          ]
        }
        """;
    }

    private static int AttackOf(UnitCard unit) => unit.Modifiers.GetEffectiveValue(CardStatFields.Attack);

    private static int DefenseOf(UnitCard unit) => unit.Modifiers.GetEffectiveValue(CardStatFields.Defense);

    /// <summary>解析→编译→挂卡（效果声明注册＋预制体登记＋对局装配）——同 EffectRuntimeEndToEndMoreTests 装配模式。</summary>
    private static async Task<(Match Match, CardEffectRegistry Registry)> AssembleAsync(
        string cardFaceText, string hostCardId)
    {
        var parser = EffectParser.CreateDefault(out var lexFailures);
        Assert.Empty(lexFailures);
        var parse = parser.Parse(cardFaceText);
        Assert.Empty(parse.Unresolved);

        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        Assert.Empty(templates.Failures);
        var ops = OpTemplateCatalog.LoadDirectory(OpTemplateCatalog.DefaultDirectory, out var opFailures);
        Assert.Empty(opFailures);
        var compiler = new EffectCompiler(templates.Templates, ops);
        var snapshot = compiler.Compile(Assert.Single(parse.Effects), "effect.mechanism.e2e." + hostCardId);

        var registry = new CardEffectRegistry();
        registry.DeclarePrefab(hostCardId, new[] { snapshot.Root.Id });

        var match = CommandTestKit.CreateCommandMatch(
            effectRegistry: registry,
            extraDefinitions: LoadDefinitions());
        match.Engine.Prefabs.RegisterPrefab(snapshot);
        await match.Initialize();
        return (match, registry);
    }

    // ---------- ① 回合归属判定面（读面正确性——随当前行动方切换） ----------

    [Fact]
    public async Task TurnRules_Owner_Turn_Tracks_Current_Player()
    {
        var match = CommandTestKit.CreateCommandMatch();
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        var unitA = await CommandTestKit.PrepareOnSupportAsync(match, playerA, CommandTestKit.InfantryId, 1);
        var unitB = await CommandTestKit.PrepareOnSupportAsync(match, playerB, CommandTestKit.InfantryId, 1);

        // 对局开始（A 回合——先手）：A 单位＝友方回合；B 单位＝敌方回合。
        Assert.True(TurnRules.IsOwnerTurn(unitA));
        Assert.False(TurnRules.IsOpponentTurn(unitA));
        Assert.False(TurnRules.IsOwnerTurn(unitB));
        Assert.True(TurnRules.IsOpponentTurn(unitB));

        // 推进到 B 的回合：反转。
        await match.EndTurn();
        Assert.False(TurnRules.IsOwnerTurn(unitA));
        Assert.True(TurnRules.IsOpponentTurn(unitA));
        Assert.True(TurnRules.IsOwnerTurn(unitB));
        Assert.False(TurnRules.IsOpponentTurn(unitB));

        // 再回到 A 的回合。
        await match.EndTurn();
        Assert.True(TurnRules.IsOwnerTurn(unitA));

        // 降级：null／非卡＝false、不抛错。
        Assert.False(TurnRules.IsOwnerTurn(null));
        Assert.False(TurnRules.IsOpponentTurn(null));
    }

    // ---------- ② V1：在场上的第三回合开始时升为老兵（条件求值假/真） ----------

    [Fact]
    public async Task V1_InPlay_Turn_Listener_Upgrades_At_Third_Turn()
    {
        var (match, _) = await AssembleAsync("在场上的第三回合开始时，升为老兵。", GuardsId);
        var playerA = match.Players[0];

        using var recorder = new UpdateRecorder(match.Engine);
        var unit = await CommandTestKit.PrepareOnFrontAsync(match, playerA, GuardsId, 0);
        Assert.Equal(1, unit.TurnsInPlay); // 入场即第 1 回合（既有读取面）
        Assert.False(VeteranRules.IsVeteran(unit));

        await match.EndTurn(); // 2（B）——非归属玩家回合开始，计数不增
        await match.EndTurn(); // 3（A）——在场第 2 回合开始：条件 2 >= 3 **假** ⇒ 不升级
        Assert.Equal(2, unit.TurnsInPlay);
        Assert.False(VeteranRules.IsVeteran(unit));

        await match.EndTurn(); // 4（B）
        await match.EndTurn(); // 5（A）——在场第 3 回合开始：条件 3 >= 3 **真** ⇒ 升级落定
        Assert.Equal(3, unit.TurnsInPlay);
        Assert.True(VeteranRules.IsVeteran(unit));
        Assert.Equal(2, AttackOf(unit));   // 2/1 → 2/2（老兵版本）
        Assert.Equal(2, DefenseOf(unit));
        Assert.Equal(1, recorder.Updates.Count(u => u.Type == GameUpdates.UnitUpgraded
            && ReferenceEquals(u.Payload![GameUpdates.PayloadUnit], unit)));
    }

    // ---------- ③ C5：揭示时点条件求值（友方回合真／敌方回合假；自指守卫） ----------

    [Fact]
    public async Task C5_Reveal_Condition_Holds_Only_In_Owner_Turn()
    {
        var (match, _) = await AssembleAsync("揭示：若是友方回合，获得 +2 攻击力。", ScoutId);
        var playerA = match.Players[0];

        using var recorder = new UpdateRecorder(match.Engine);
        var hostA = await CommandTestKit.PrepareOnSupportAsync(match, playerA, ScoutId, 1);
        var hostB = await CommandTestKit.PrepareOnSupportAsync(match, playerA, ScoutId, 2);
        Assert.True(CovertRules.IsCovert(hostA));
        Assert.Equal(2, AttackOf(hostA));

        // ① A 的回合（友方回合）揭示 hostA ⇒ 条件真 ⇒ +2；hostB 作为同信号观察者被**自指守卫**挡下。
        Assert.Equal(RevealOutcome.Revealed, await CovertRules.RevealAsync(hostA));
        Assert.Equal(4, AttackOf(hostA));
        Assert.Equal(2, AttackOf(hostB)); // 别人被揭示不触发（被揭示者==宿主 守卫）

        // ② 推进到 B 的回合（敌方回合）揭示 hostB ⇒ 条件假 ⇒ 无增益；hostA 不受他人揭示影响。
        await match.EndTurn();
        Assert.Equal(RevealOutcome.Revealed, await CovertRules.RevealAsync(hostB));
        Assert.Equal(2, AttackOf(hostB));
        Assert.Equal(4, AttackOf(hostA));

        // 揭示信号：hostA／hostB 各恰一次。
        Assert.Equal(1, recorder.Updates.Count(u => u.Type == GameUpdates.UnitRevealed
            && ReferenceEquals(u.Payload![GameUpdates.PayloadUnit], hostA)));
        Assert.Equal(1, recorder.Updates.Count(u => u.Type == GameUpdates.UnitRevealed
            && ReferenceEquals(u.Payload![GameUpdates.PayloadUnit], hostB)));
    }
}
