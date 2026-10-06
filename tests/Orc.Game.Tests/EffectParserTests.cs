using Orc.Cards;
using Orc.Core;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Lexicon;
using Orc.Script;
using Orc.Game.EffectParsing.Parsing;
using Orc.Game.EffectParsing.Templates;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 效果解析器·解析段验收（S6–S10）：词表加载、分词（12 类 / 引号 / 未知累积）、
/// 切分（硬/软边界、纯词条行）、AST（触发识别 / 原文 span / 多事件）、语义映射（子集内产出 / 超子集显式失败）、
/// 以及"卡面文本 → DSL → 带 csx 预制体 → 反序列化"端到端。
/// </summary>
public class EffectParserTests
{
    private static readonly (EffectParser Parser, IReadOnlyList<LexiconLoadFailure> Failures) Setup = Create();

    private static EffectParser Parser => Setup.Parser;

    private static IReadOnlyList<LexiconLoadFailure> LexiconFailures => Setup.Failures;

    private static readonly OpTemplateCatalog Ops =
        OpTemplateCatalog.LoadDirectory(OpTemplateCatalog.DefaultDirectory, out _);

    private static (EffectParser, IReadOnlyList<LexiconLoadFailure>) Create()
    {
        var parser = EffectParser.CreateDefault(out var failures);
        return (parser, failures);
    }

    [Fact]
    public void Lexicons_Load_Without_Failures()
    {
        Assert.Empty(LexiconFailures);
    }

    [Fact]
    public void Action_Lexicon_Keys_Are_Known_Verbs()
    {
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "damage", "attack", "destroy", "draw", "gain", "lose", "move", "pin", "silence", "addToHand", "shuffleIn",
        };
        _ = known;
        var lexicons = LexiconLoader.LoadDirectory(LexiconLoader.DefaultDirectory, out var failures);

        Assert.Empty(failures);
        foreach (var entry in lexicons.Entries.Where(item => item.Category == LexiconCategory.Action))
        {
            Assert.True(known.Contains(entry.Get("key") ?? string.Empty), $"未知动作键：{entry.Literal} → {entry.Get("key")}");
        }
    }

    [Fact]
    public void Tokenizer_Classifies_And_Keeps_Original_Offsets()
    {
        const string text = "部署：对一个敌方单位造成2点伤害。";
        var tokens = Parser.Tokenize(text);

        Assert.Equal(TokenType.Trigger, tokens[0].Type);
        Assert.Contains(tokens, token => token.Type == TokenType.Quant);
        Assert.Contains(tokens, token => token.Type == TokenType.Side);
        Assert.Contains(tokens, token => token.Type == TokenType.Action);
        Assert.Contains(tokens, token => token.Type == TokenType.Num && token.IntValue == 2);
        Assert.Contains(tokens, token => token.Type == TokenType.Punct && token.Get("kind") == PunctKinds.Colon);

        // span 指向原文：切片必须与 token 字面一致
        foreach (var token in tokens.Where(item => item.Type != TokenType.Unknown))
        {
            Assert.Equal(token.Lexeme, text.Substring(token.Start, token.Length));
        }
    }

    [Fact]
    public void Tokenizer_Accumulates_Unknown_Run()
    {
        var tokens = Parser.Tokenize("部署：造成2点伤害。");
        var unknown = Assert.Single(tokens, token => token.Type == TokenType.Unknown);

        Assert.Equal("点伤害", unknown.Lexeme);
    }

    [Fact]
    public void Segmenter_Splits_Hard_And_Soft_And_Drops_Keyword_Lines()
    {
        const string text = "闪击\n部署：造成1点伤害；抽一张牌。亡计：造成2点伤害。";
        var asts = Parser.ParseAst(text);

        // 词条行"闪击"被丢弃 ⇒ 不产生额外单元；硬/软边界 ⇒ 三个单元（部署；软；亡计）
        Assert.Equal(3, asts.Count);
        Assert.Equal(BoundaryKind.Hard, asts[0].Boundary);
        Assert.Equal(BoundaryKind.Soft, asts[1].Boundary);
        Assert.Same(asts[0], asts[1].InheritsFrom);
        Assert.Equal(BoundaryKind.Hard, asts[2].Boundary);
    }

    [Fact]
    public void Ast_Recognizes_Named_Listen_And_Implicit_Triggers()
    {
        Assert.Equal(TriggerSyntaxKind.Named, Parser.ParseAst("部署：造成1点伤害。")[0].Trigger!.Kind);

        var listen = Parser.ParseAst("友方单位被消灭时，抽一张牌。")[0].Trigger!;
        Assert.Equal(TriggerSyntaxKind.Listen, listen.Kind);
        Assert.Equal("友方单位被消灭时", listen.RawText);

        Assert.Null(Parser.ParseAst("造成1点伤害。")[0].Trigger);
    }

    [Fact]
    public void Ast_Keeps_Multi_Event_Phrase_And_Original_Span()
    {
        const string text = "部署、移动、攻击时：造成2点伤害。";
        var ast = Parser.ParseAst(text)[0];

        Assert.Equal(TriggerSyntaxKind.Named, ast.Trigger!.Kind);
        Assert.Equal(3, ast.Trigger.Events.Count);
        Assert.Equal(0, ast.Span.Start);
        Assert.Equal("部署、移动、攻击时：造成2点伤害", text.Substring(ast.Span.Start, ast.Span.Length));
    }

    [Fact]
    public void Parse_Maps_Damage_With_Selector()
    {
        var result = Parser.Parse("部署：对一个敌方单位造成2点伤害。");

        Assert.Empty(result.Unresolved);
        var dsl = Assert.Single(result.Effects);
        Assert.Equal("deploy_basic", dsl.Template);

        var op = Assert.Single(dsl.Fills["on_deploy"].Ops);
        Assert.Equal("damage", op.Op);
        Assert.Equal(2, op.Amount);
        Assert.Equal("one", op.Target!.Sel);
        Assert.Equal("enemy", op.Target.Side);
    }

    [Fact]
    public void Parse_Maps_Draw_Grant_And_Move()
    {
        var draw = Assert.Single(Parser.Parse("部署：抽两张牌。").Effects).Fills["on_deploy"].Ops[0];
        Assert.Equal("draw", draw.Op);
        Assert.Equal(2, draw.Count);

        var grant = Assert.Single(Parser.Parse("部署：获得闪击。").Effects).Fills["on_deploy"].Ops[0];
        Assert.Equal("grant", grant.Op);
        Assert.Equal("blitz", grant.Keyword);

        var move = Assert.Single(Parser.Parse("部署：移动至前线。").Effects).Fills["on_deploy"].Ops[0];
        Assert.Equal("move", move.Op);
        Assert.Equal("frontline", move.Zone);
    }

    [Fact]
    public void Parse_Maps_Destroy_To_Game_Death_Chain()
    {
        var result = Parser.Parse("部署：消灭一个敌方单位。");

        Assert.Empty(result.Unresolved);
        var op = Assert.Single(Assert.Single(result.Effects).Fills["on_deploy"].Ops);
        Assert.Equal("destroy", op.Op);
        Assert.Equal("enemy", op.Target!.Side);
    }

    [Fact]
    public void Parse_Maps_Listen_Trigger_To_Listener_Template()
    {
        var result = Parser.Parse("友方单位被消灭时，抽一张牌。");

        Assert.Empty(result.Unresolved);
        var dsl = Assert.Single(result.Effects);
        Assert.Equal("death_basic", dsl.Template);
        var op = Assert.Single(dsl.Fills["on_event"].Ops);
        Assert.Equal("draw", op.Op);
        Assert.Equal(1, op.Count);
        // A：监听短语里的"友方"→ 归属过滤（真实 csx）
        Assert.Equal(DslCondition.OwnerSame, op.Condition!.Kind);

        var enemy = Assert.Single(Parser.Parse("敌方单位被消灭时，抽一张牌。").Effects);
        Assert.Equal(DslCondition.OwnerDifferent, Assert.Single(enemy.Fills["on_event"].Ops).Condition!.Kind);
    }

    [Fact]
    public void Parse_Maps_Condition_Clause_As_Placeholder()
    {
        var result = Parser.Parse("部署：如果手牌不少于2张，抽一张牌。");

        Assert.Empty(result.Unresolved);
        var op = Assert.Single(Assert.Single(result.Effects).Fills["on_deploy"].Ops);
        Assert.Equal("draw", op.Op);
        Assert.Equal(DslCondition.RawKind, op.Condition!.Kind);
        Assert.Contains("不少于", op.Condition.Raw);
    }

    private static Type ResolveViewType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return typeof(CardEventView);
    }

    [Fact]
    public void Parse_Maps_Deathrattle_Event_To_Death_Listener_Template()
    {
        var result = Parser.Parse("亡计：抽一张牌。");

        Assert.Empty(result.Unresolved);
        var dsl = Assert.Single(result.Effects);
        Assert.Equal("death_basic", dsl.Template);
        Assert.Equal("draw", Assert.Single(dsl.Fills["on_event"].Ops).Op);
    }

    [Fact]
    public void Parse_Keeps_Unparsable_Op_As_NeedsCsx()
    {
        // 句式成立（有触发、有动作词），但**op 无法解析**（动作键 attack 无对应 op）⇒ 原文保留 + 标记需要 csx 实现
        var result = Parser.Parse("部署：攻击一个敌方单位。");

        Assert.Empty(result.Unresolved);
        var op = Assert.Single(Assert.Single(result.Effects).Fills["on_deploy"].Ops);
        Assert.Equal(DslOpRegistry.NeedsCsxOpName, op.Op);
        Assert.Contains("攻击一个敌方单位", op.Script);
    }

    [Fact]
    public void Parse_Reports_Unresolved_When_No_Action_Word()
    {
        // 无任何动作词 ⇒ 属**句式**失败（不是 op 失败）⇒ 不产 mustCsx，计未解析
        var result = Parser.Parse("部署：变成晴天。");

        Assert.Empty(result.Effects);
        Assert.Contains(result.Unresolved, record => record.Reason.Contains("无法识别动作短语", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Maps_Quoted_Effect_As_Nested()
    {
        var result = Parser.Parse("部署：获得：“亡计：抽一张牌。”");

        Assert.Empty(result.Unresolved);
        var op = Assert.Single(Assert.Single(result.Effects).Fills["on_deploy"].Ops);
        Assert.Equal(DslOpRegistry.NestedOpName, op.Op);

        var nested = Assert.Single(op.Nested!);
        Assert.Equal("death_basic", nested.Template);
        Assert.Equal("draw", Assert.Single(nested.Fills["on_event"].Ops).Op);
    }

    [Fact]
    public void Parse_Maps_Attack_Listener_To_Inject_Template()
    {
        var result = Parser.Parse("友方单位攻击后，抽一张牌。");

        Assert.Empty(result.Unresolved);
        var dsl = Assert.Single(result.Effects);
        Assert.Equal("attack_basic", dsl.Template);
        Assert.Equal("draw", Assert.Single(dsl.Fills["on_event"].Ops).Op);
    }

    [Fact]
    public void Parse_Reports_Unresolved_For_Out_Of_Subset()
    {
        // 未纳入模板的监听事件（被压制）⇒ 句式层显式失败
        var listen = Parser.Parse("友方单位被压制时，抽一张牌。");
        Assert.Empty(listen.Effects);
        Assert.Contains(listen.Unresolved, record => record.Reason.Contains("监听型触发", StringComparison.Ordinal));

        // 纯未知串（无任何已识别 token）⇒ 句式层失败
        var garbage = Parser.Parse("。啊哦。");
        Assert.Empty(garbage.Effects);
    }

    [Fact]
    public void Parse_Splits_Multi_Event_Into_Effects_When_Possible()
    {
        var result = Parser.Parse("部署、移动、攻击时：造成2点伤害。");

        Assert.Single(result.Effects);
        Assert.Equal(2, result.Unresolved.Count);
    }

    [Fact]
    public void Parse_Point_Slot_Gain_And_Lose_Map_To_Slot_Ops()
    {
        var gain = Assert.Single(Parser.Parse("部署：获得 1 个指挥点槽。").Effects);
        var gainOp = Assert.Single(gain.Fills["on_deploy"].Ops);
        Assert.Equal("gainSlot", gainOp.Op);
        Assert.Equal(1, gainOp.Amount);

        var lose = Assert.Single(Parser.Parse("部署：失去 2 个指挥点槽。").Effects);
        var loseOp = Assert.Single(lose.Fills["on_deploy"].Ops);
        Assert.Equal("loseSlot", loseOp.Op);
        Assert.Equal(2, loseOp.Amount);

        var gainPoint = Assert.Single(Parser.Parse("部署：获得 1 个指挥点。").Effects);
        var gainPointOp = Assert.Single(gainPoint.Fills["on_deploy"].Ops);
        Assert.Equal("gainPoint", gainPointOp.Op);
        Assert.Equal(1, gainPointOp.Amount);

        var losePoint = Assert.Single(Parser.Parse("部署：失去 3 个指挥点。").Effects);
        var losePointOp = Assert.Single(losePoint.Fills["on_deploy"].Ops);
        Assert.Equal("losePoint", losePointOp.Op);
        Assert.Equal(3, losePointOp.Amount);
    }

    [Fact]
    public void Generated_Csx_Compiles()
    {
        var evaluator = new CSharpScriptEvaluator();
        var samples = new[]
        {
            "部署：对一个敌方单位造成2点伤害。",
            "部署：对所有敌方单位造成1点伤害。",
            "部署：抽两张牌。",
            "部署：获得闪击。",
            "部署：移动至前线。",
            "部署：消灭一个敌方单位。",
            "友方单位被消灭时，抽一张牌。",
            "敌方单位被消灭时，抽一张牌。",
            "部署：如果手牌不少于2张，抽一张牌。",
            "部署：压制所有敌方单位。",
            "部署：抑制 1 个单位。",
            "友方回合结束时，抽一张牌。",
            "友方使用指令后，抽一张牌。",
            "将 3 张“先锋部队”加入手中。",
            "将 1 张“先锋部队”洗入卡组。",
            "指向 1 个单位，使其移至前线。",
            "部署：获得 +2+2。",
            "部署：获得 +1 攻击力。",
            "部署：使对手手牌获得 +3 花费。",
            "亡计：抽一张牌。",
            "动员：抽一张牌。",
            "部署：攻击一个敌方单位。",
            "友方单位攻击后，抽一张牌。",
            "部署：获得：“亡计：抽一张牌。”",
            "友方回合开始时，抽一张牌。",
            "友方单位被摧毁时，抽一张牌。",
            "部署：获得 1 个指挥点槽。",
            "部署：失去 1 个指挥点槽。",
            "部署：获得 1 个指挥点。",
            "部署：失去 1 个指挥点。",
            "友方总部获得防御力时，获得 +1 攻击力。",
            "友方弃牌时，抽一张牌。",
            "友方爆牌时，抽一张牌。",
            "友方获得卡牌时，抽一张牌。",
            "友方总部受到伤害时，获得 +1 攻击力。",
            "本单位行动后，抽一张牌。",
        };

        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        var compiler = new EffectCompiler(templates.Templates, Ops);

        var index = 0;
        foreach (var text in samples)
        {
            var parse = Parser.Parse(text);
            Assert.NotEmpty(parse.Effects);

            foreach (var dsl in parse.Effects)
            {
                var snapshot = compiler.Compile(dsl, $"effect.compile.{index++}");
                foreach (var node in new[] { snapshot.Root.MainTrigger }.Concat(snapshot.Root.OtherTriggers))
                {
                    var viewType = ResolveViewType(node.ViewTypeName);
                    foreach (var ev in node.Events)
                    {
                        var result = evaluator.Evaluate(
                            new ScriptRequest(ev.CsxSource!, ev.EntryName, viewType));

                        Assert.True(
                            result.Success,
                            $"'{text}' 生成的 csx 编译失败（{result.ErrorCategory}）：{result.Error}{Environment.NewLine}{ev.CsxSource}");
                    }
                }
            }
        }

        Assert.True(index >= samples.Length, "语料未全部产出效果。");
    }

    [Fact]
    public void Parse_Listener_Condition_Renders_Owner_Guard_In_Csx()
    {
        var dsl = Assert.Single(Parser.Parse("友方单位被消灭时，抽一张牌。").Effects);
        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        var compiler = new EffectCompiler(templates.Templates, Ops);
        var snapshot = PrefabJson.Deserialize(compiler.CompileToJson(dsl, "effect.listen.demo"));

        var csx = Assert.Single(snapshot.Root.MainTrigger.Events).CsxSource!;
        Assert.Contains("var self = view.Host as Card;", csx, StringComparison.Ordinal);
        Assert.Contains("if (self is Orc.Game.Cards.CardBase actorSelf", csx, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_End_To_End_Compiles_To_Prefab_And_Deserializes()
    {
        var parse = Parser.Parse("部署：对一个敌方单位造成2点伤害。");
        var dsl = Assert.Single(parse.Effects);

        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        Assert.Empty(templates.Failures);

        var compiler = new EffectCompiler(templates.Templates, Ops);
        var json = compiler.CompileToJson(dsl, "effect.parsed.demo");
        var snapshot = PrefabJson.Deserialize(json);

        Assert.Equal("effect.parsed.demo", snapshot.Root.Id);
        var ev = Assert.Single(snapshot.Root.MainTrigger.Events);
        Assert.True(ev.IsInline);
        Assert.Contains("amount=2", ev.CsxSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Writes_Markdown_Coverage_Report()
    {
        var corpus = new[]
        {
            "部署：对一个敌方单位造成2点伤害。",
            "部署：抽两张牌。",
            "部署：获得闪击。",
            "部署：移动至前线。",
            "部署：对所有敌方单位造成1点伤害。",
            "部署：消灭一个敌方单位。",
            "友方单位被消灭时，抽一张牌。",
            "部署、移动、攻击时：造成2点伤害。",
            "闪击\n部署：对一个敌方单位造成3点伤害。",
            "部署：造成1点伤害。亡计：造成2点伤害。",
        };

        var directory = Path.Combine(AppContext.BaseDirectory, "EffectParsing", "report");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "coverage.md");

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# 效果解析器覆盖率报告").AppendLine();
        builder.AppendLine("| 原文 | 效果数 | 未解析数 | 备注 |").AppendLine("|---|---|---|---|");
        var ok = 0;
        foreach (var text in corpus)
        {
            var result = Parser.Parse(text);
            if (result.Effects.Count > 0)
            {
                ok++;
            }

            var note = result.Unresolved.Count == 0
                ? string.Empty
                : string.Join(" / ", result.Unresolved.Select(record => record.Reason).Distinct());
            builder.Append("| ").Append(text.Replace("\n", "\\n", StringComparison.Ordinal))
                .Append(" | ").Append(result.Effects.Count)
                .Append(" | ").Append(result.Unresolved.Count)
                .Append(" | ").Append(note).AppendLine(" |");
        }

        builder.AppendLine()
            .Append("覆盖率：").Append(ok).Append('/').Append(corpus.Length);
        File.WriteAllText(reportPath, builder.ToString());

        Assert.True(File.Exists(reportPath));
        var content = File.ReadAllText(reportPath);
        Assert.Contains("覆盖率", content, StringComparison.Ordinal);
        Assert.Contains("覆盖率：10/10", content, StringComparison.Ordinal);
    }
}
