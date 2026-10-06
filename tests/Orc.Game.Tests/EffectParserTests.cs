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
            "state", // E1-41：`具有`（状态/属性描述）
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
        // 模板可能带程序集限定（", Orc.Game"）——扫描程序集时用裸名。
        var bareName = name.Split(',')[0].Trim();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(bareName, throwOnError: false);
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
            // E1-39：交战存活信号 + 玩家面归属守卫 + "抽 N 张牌" 句式修正
            "本单位交战并存活后，抽一张牌。",
            "本单位对战并存活后，获得 +1+1。",
            "友方额外获得 1 个指挥点槽时，抽一张牌。",
            "友方失去 1 个指挥点槽时，抽一张牌。",
            "友方抽 1 张牌时，获得 +1 攻击力。",
            "敌方额外抽 1 张牌时，对其总部造成 4 点伤害。",
            // E1-41：`具有`（状态/属性描述）＋ 期限 ＋ 自指 ＋ 多宾语
            "所有友方步兵具有 +1 攻击力。",
            "使 1 个友方步兵具有 +2 攻击力，直到回合结束。",
            "使所有友方单位具有 +1 攻击力和闪击，直到回合结束。",
            "具有 +1 攻击力。",
            "友方步兵具有闪击。",
            "使 1 个友方单位具有 -1 行动花费。",
            "所有敌方空军具有 -2 行动花费。",
            "每有 1 个相邻单位，具有 +2 攻击力。",
            "本回合，所有友方空军具有 +1 攻击力。",
            // E1-50：主体归属面（友方/敌方单位造成/消灭）
            "友方单位造成 1 点对战伤害时，抽 1 张牌。",
            "友方单位消灭 1 个敌方单位时，抽 1 张牌。",
            // E1-47：来源侧伤害 / 击杀者（载荷自指守卫）
            "本单位造成伤害时，抽一张牌。",
            "本单位对敌方总部造成伤害后，获得 +1 攻击力。",
            "本单位消灭 1 个单位后，具有奋战。",
            // E1-57：真实数值条件 ＋ 目标阈值
            "若友方单位数不小于 3，获得 +1+2。",
            "消灭 1 个花费不大于 3 的单位。",
            "部署：若友方总部防御力不小于 4，获得 +1+1。",
            // E1-53：反制触发
            "友方反制触发时，抽 1 张牌。",
            "触发敌方反制时，获得 +2+2。",
            // E1-54：事件卡属性过滤（tag／卡类型／阵营／卡名）
            "使用海军牌时，抽 1 张牌。",
            "友方使用英国指令时，抽 1 张牌。",
            "友方使用“计划”时，抽 1 张牌。",
            // E1-42：事件卡词条过滤（合取守卫）
            "友方使用情报牌时，获得 +1+1。",
            "使用情报牌时，抽 1 张牌。",
            "敌方使用情报牌时，抽 1 张牌。",
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
        Assert.Contains("if ((view.Card ?? view.Unit) is Orc.Game.Cards.CardBase actorEvent", csx, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Maps_Damage_Dealt_And_Killed_With_Payload_Self_Guard()
    {
        // E1-47：`本单位造成伤害时` ⇒ damage_dealt_basic ＋ **载荷自指守卫**（载荷 Unit＝施动方）
        var dealt = Assert.Single(Parser.Parse("本单位造成伤害时，使友方总部获得同等防御力。").Effects);
        Assert.Equal("damage_dealt_basic", dealt.Template);
        var dealtCondition = Assert.Single(dealt.Fills["on_event"].Ops).Condition!;
        Assert.Equal(DslCondition.PayloadSelf, dealtCondition.Kind);
        Assert.Equal("Unit", dealtCondition.Raw);

        // `本单位对敌方总部造成伤害后` ⇒ 自指 ＋ **受方是总部**（合取）
        var hq = Assert.Single(Parser.Parse("本单位对敌方总部造成伤害后，获得 +1 攻击力。").Effects);
        var hqCondition = Assert.Single(hq.Fills["on_event"].Ops).Condition!;
        Assert.Equal(DslCondition.AllKind, hqCondition.Kind);
        Assert.Equal(DslCondition.PayloadSelf, hqCondition.All![0].Kind);
        Assert.Equal(DslCondition.PayloadIsHq, hqCondition.All![1].Kind);
        Assert.Equal("Card", hqCondition.All![1].Raw);

        // `本单位消灭 1 个单位时` ⇒ killed_basic（hook card.died）＋ **击杀者自指守卫**
        var killed = Assert.Single(Parser.Parse("本单位消灭 1 个单位后，具有奋战。").Effects);
        Assert.Equal("killed_basic", killed.Template);
        var killedCondition = Assert.Single(killed.Fills["on_event"].Ops).Condition!;
        Assert.Equal(DslCondition.PayloadSelf, killedCondition.Kind);
        Assert.Equal("Killer", killedCondition.Raw);

        // E1-50：主体＝**友方/敌方单位** ⇒ **载荷字段归属面**守卫（同主/异主）
        var friendlyDealt = Assert.Single(Parser.Parse("友方单位造成 1 点对战伤害时，对敌方总部造成 2 点伤害。").Effects);
        Assert.Equal("damage_dealt_basic", friendlyDealt.Template);
        var friendlyCondition = Assert.Single(friendlyDealt.Fills["on_event"].Ops).Condition!;
        Assert.Equal(DslCondition.PayloadOwnerSame, friendlyCondition.Kind);
        Assert.Equal("Unit", friendlyCondition.Raw);

        var friendlyKilled = Assert.Single(Parser.Parse("友方单位消灭 1 个敌方单位时，抽 1 张牌。").Effects);
        var friendlyKilledCondition = Assert.Single(friendlyKilled.Fills["on_event"].Ops).Condition!;
        Assert.Equal(DslCondition.PayloadOwnerSame, friendlyKilledCondition.Kind);
        Assert.Equal("Killer", friendlyKilledCondition.Raw);

        var enemyKilled = Assert.Single(Parser.Parse("敌方单位消灭 1 个单位时，抽 1 张牌。").Effects);
        Assert.Equal(
            DslCondition.PayloadOwnerDifferent,
            Assert.Single(enemyKilled.Fills["on_event"].Ops).Condition!.Kind);

        // **主体不可辨识** ⇒ 不映射（宁可未解析，也不泛触发）
        var unrecognized = Parser.Parse("目标单位造成伤害时，本单位获得 +1+1。");
        Assert.Empty(unrecognized.Effects);
        Assert.Contains(
            unrecognized.Unresolved,
            record => record.Reason.Contains("监听型触发", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Payload_Owner_Guard_Renders_Real_Csx()
    {
        var dsl = Assert.Single(Parser.Parse("友方单位造成 1 点对战伤害时，抽 1 张牌。").Effects);
        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        var compiler = new EffectCompiler(templates.Templates, Ops);
        var csx = Assert.Single(compiler.Compile(dsl, "effect.owner.demo").Root.MainTrigger.Events).CsxSource!;

        Assert.Contains("(view.Unit as Orc.Game.Cards.CardBase)?.Owner is { } actorEventOwner", csx, StringComparison.Ordinal);
        Assert.Contains("object.ReferenceEquals(actorSelf.Owner, actorEventOwner)", csx, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Maps_EventCard_Attribute_Filters_With_Owner_Conjunction()
    {
        // E1-42/E1-54：`使用<属性>牌时` ⇒ played_basic ＋ **事件卡属性守卫**（真实 csx）。
        var friendly = Assert.Single(Parser.Parse("友方使用情报牌时，获得 +1+1。").Effects);
        Assert.Equal("played_basic", friendly.Template);
        var friendlyOp = Assert.Single(friendly.Fills["on_event"].Ops);
        var conjunction = friendlyOp.Condition!;
        Assert.Equal(DslCondition.AllKind, conjunction.Kind);
        Assert.Equal(2, conjunction.All!.Count);
        Assert.Equal(DslCondition.OwnerSame, conjunction.All![0].Kind);
        Assert.Equal(DslCondition.EventCardFilter, conjunction.All![1].Kind);
        Assert.Equal("keyword:情报", conjunction.All![1].Raw);

        // 无阵营词 ⇒ 只有事件卡守卫
        var plain = Assert.Single(Parser.Parse("使用情报牌时，抽 1 张牌。").Effects);
        var plainCondition = Assert.Single(plain.Fills["on_event"].Ops).Condition!;
        Assert.Equal(DslCondition.EventCardFilter, plainCondition.Kind);

        var enemy = Assert.Single(Parser.Parse("敌方使用情报牌时，抽 1 张牌。").Effects);
        Assert.Equal(
            DslCondition.OwnerDifferent,
            Assert.Single(enemy.Fills["on_event"].Ops).Condition!.All![0].Kind);

        // E1-54 甲：其余维度（tag／卡类型／阵营／卡名）也可表达；**多维度＝合取**
        var tag = Assert.Single(Parser.Parse("使用海军牌时，抽 1 张牌。").Effects);
        Assert.Equal("tag:海军", Assert.Single(tag.Fills["on_event"].Ops).Condition!.Raw);

        var britishCommand = Assert.Single(Parser.Parse("友方使用英国指令时，抽 1 张牌。").Effects);
        var britishCondition = Assert.Single(britishCommand.Fills["on_event"].Ops).Condition!;
        Assert.Equal(DslCondition.AllKind, britishCondition.Kind);
        Assert.Equal(3, britishCondition.All!.Count); // owner.same ＋ faction ＋ category
        Assert.Contains(britishCondition.All!, item => item.Raw == "faction:Britain");
        Assert.Contains(britishCondition.All!, item => item.Raw == "category:Command");

        var named = Assert.Single(Parser.Parse("友方使用“计划”时，抽 1 张牌。").Effects);
        Assert.Equal(
            "name:计划",
            Assert.Single(named.Fills["on_event"].Ops).Condition!.All![1].Raw);

        // `指令使用时`（另一表达形态）同样可表达：played_basic ＋ 卡类型＋归属守卫
        var commandUsed = Assert.Single(Parser.Parse("敌方指令使用时，抽 1 张牌。").Effects);
        Assert.Equal("played_basic", commandUsed.Template);
        var commandCondition = Assert.Single(commandUsed.Fills["on_event"].Ops).Condition!;
        Assert.Contains(commandCondition.All!, item => item.Raw == "category:Command");
        Assert.Contains(commandCondition.All!, item => item.Kind == DslCondition.OwnerDifferent);

        // **无属性线索**的 `使用…时` ⇒ 不泛触发（保持显式失败）
        var outOfSubset = Parser.Parse("友方使用构筑之外的牌时，抽 1 张牌。");
        Assert.Contains(
            outOfSubset.Unresolved,
            record => record.Reason.Contains("监听型触发", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Maps_Numeric_Comparison_To_Condition_And_Target_Threshold()
    {
        // E1-57：**全局条件**（自有子句）⇒ 真实条件（规范串）
        var count = Assert.Single(Parser.Parse("若友方单位数不小于 3，获得 +1+2。").Effects);
        var countCondition = Assert.Single(count.Fills["on_deploy"].Ops).Condition!;
        Assert.Equal(DslCondition.Compare, countCondition.Kind);
        Assert.Equal("count=s=friendly:gte:#3", countCondition.Raw);

        var versus = Assert.Single(Parser.Parse("部署：若友方单位数不大于敌方单位，抽 1 张牌。").Effects);
        Assert.Equal(
            "count=s=friendly:lte:count=s=enemy",
            Assert.Single(versus.Fills["on_deploy"].Ops).Condition!.Raw);

        var points = Assert.Single(Parser.Parse("若剩余指挥点数不小于 5，抽 1 张牌。").Effects);
        Assert.Equal("points=s=friendly:gte:#5", Assert.Single(points.Fills["on_deploy"].Ops).Condition!.Raw);

        var hq = Assert.Single(Parser.Parse("部署：若友方总部防御力不小于 4，获得 +1+1。").Effects);
        Assert.Equal(
            "stat=f=defense;s=friendly;z=hq:gte:#4",
            Assert.Single(hq.Fills["on_deploy"].Ops).Condition!.Raw);

        // 同子句的**目标阈值** ⇒ 落到选择器过滤（**不再**是占位条件）
        var threshold = Assert.Single(Parser.Parse("消灭 1 个花费不大于 3 的单位。").Effects);
        var thresholdOp = Assert.Single(threshold.Fills["on_deploy"].Ops);
        Assert.Equal("destroy", thresholdOp.Op);
        Assert.Null(thresholdOp.Condition);
        Assert.Equal("opCost", thresholdOp.Target!.Filter!.ThresholdField);
        Assert.Equal("lte", thresholdOp.Target.Filter.ThresholdOp);
        Assert.Equal(3, thresholdOp.Target.Filter.ThresholdValue);

        // 未覆盖形态（敌方总部属性引用）仍为**占位条件**（`if (false)`＝不静默执行）
        var leftover = Assert.Single(Parser.Parse("若友方总部防御力大于敌方总部，造成 3 点伤害。").Effects);
        Assert.Equal(DslCondition.RawKind, Assert.Single(leftover.Fills["on_deploy"].Ops).Condition!.Kind);
    }

    [Fact]
    public void Parse_Maps_Counter_Trigger_With_Owner_Guard()
    {
        // E1-53：`友方反制触发时`／`触发敌方反制时` ⇒ counter_basic（hook counter.triggered，载荷 {Card, Player}）
        var friendly = Assert.Single(Parser.Parse("友方反制触发时，抽 1 张牌。").Effects);
        Assert.Equal("counter_basic", friendly.Template);
        Assert.Equal(
            DslCondition.OwnerSame,
            Assert.Single(friendly.Fills["on_event"].Ops).Condition!.Kind);

        var enemy = Assert.Single(Parser.Parse("触发敌方反制时，获得 +2+2。").Effects);
        Assert.Equal("counter_basic", enemy.Template);
        Assert.Equal(
            DslCondition.OwnerDifferent,
            Assert.Single(enemy.Fills["on_event"].Ops).Condition!.Kind);
    }

    [Fact]
    public void Parse_EventCard_Keyword_Guard_Renders_Real_Csx()
    {
        var dsl = Assert.Single(Parser.Parse("友方使用情报牌时，获得 +1+1。").Effects);
        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        var compiler = new EffectCompiler(templates.Templates, Ops);
        var csx = Assert.Single(compiler.Compile(dsl, "effect.intel.demo").Root.MainTrigger.Events).CsxSource!;

        Assert.Contains("view.Card as Orc.Game.Cards.CardBase", csx, StringComparison.Ordinal);
        Assert.Contains("actorEventKeyword.Keywords.Has(\"情报\")", csx, StringComparison.Ordinal);
        // 归属守卫仍在（合取）
        Assert.Contains("object.ReferenceEquals(actorSelf.Owner, actorEvent.Owner)", csx, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Maps_State_Verb_To_Buff_Grant_And_CostMod()
    {
        // E1-41/E1-56：**静态**（无触发）的 `具有` ⇒ **光环**（受益随进出/位置实时重算），宾语取自动词之后的过滤短语。
        var all = Assert.Single(Parser.Parse("所有友方步兵具有 +1 攻击力。").Effects);
        var allOp = Assert.Single(all.Fills["on_deploy"].Ops);
        Assert.Equal("aura", allOp.Op);
        Assert.Equal("attack", allOp.Field);
        Assert.Equal(1, allOp.Amount);
        Assert.Equal("all", allOp.Target!.Sel);
        Assert.Equal("friendly", allOp.Target.Side);
        Assert.Equal("infantry", allOp.Target.Filter!.UnitType); // `步兵`（动词前）＝目标限定词

        // 无目标 ⇒ 自指（sel 缺省渲染为 self）
        var self = Assert.Single(Parser.Parse("具有 +1 攻击力。").Effects);
        Assert.Null(Assert.Single(self.Fills["on_deploy"].Ops).Target);

        // `其他/其它` ⇒ 过滤维度 excludeSelf（E1-56）
        var other = Assert.Single(Parser.Parse("其他友方坦克具有 -1 行动花费。").Effects);
        var otherOp = Assert.Single(other.Fills["on_deploy"].Ops);
        Assert.Equal("aura", otherOp.Op);
        Assert.Equal("opCost", otherOp.Field);
        Assert.Equal(-1, otherOp.Amount);
        Assert.True(otherOp.Target!.Filter!.ExcludeSelf);
        Assert.Equal("tank", otherOp.Target.Filter!.UnitType);

        // 词条宾语 → grant（词条无光环面 ⇒ 保持一次性授予）
        var keyword = Assert.Single(Parser.Parse("友方步兵具有闪击。").Effects);
        var keywordOp = Assert.Single(keyword.Fills["on_deploy"].Ops);
        Assert.Equal("grant", keywordOp.Op);
        Assert.Equal("blitz", keywordOp.Keyword);

        // 触发体内的 `使…具有…` ⇒ **修饰器**（一次性动作；此处无期限 ⇒ costMod）
        var cost = Assert.Single(Parser.Parse("部署：使 1 个友方单位具有 -1 行动花费。").Effects);
        var costOp = Assert.Single(cost.Fills["on_deploy"].Ops);
        Assert.Equal("costMod", costOp.Op);
        Assert.Equal(-1, costOp.Amount);

        // 单子句**多宾语**（`和`）⇒ 展开为多个 op（静态：光环 ＋ 词条授予）
        var multi = Assert.Single(Parser.Parse("使所有友方单位具有 +1 攻击力和闪击。").Effects);
        var multiOps = multi.Fills["on_deploy"].Ops;
        Assert.Equal(2, multiOps.Count);
        Assert.Equal("aura", multiOps[0].Op);
        Assert.Equal("grant", multiOps[1].Op);
    }

    [Fact]
    public void Parse_State_Verb_Applies_Duration_Only_To_Capable_Ops()
    {
        // `直到回合结束` ⇒ buff 带期限
        var buff = Assert.Single(Parser.Parse("使 1 个友方步兵具有 +2 攻击力，直到回合结束。").Effects);
        var buffOp = Assert.Single(buff.Fills["on_deploy"].Ops);
        Assert.Equal("buff", buffOp.Op);
        Assert.Equal(2, buffOp.Attack); // E1-41：`1 个` 是数量短语、不是载荷
        Assert.Equal(Untils.TurnEnd, buffOp.Until);

        // `本回合` 前缀同样判为本回合结束
        var prefix = Assert.Single(Parser.Parse("本回合，所有友方空军具有 +1 攻击力。").Effects);
        Assert.Equal(Untils.TurnEnd, Assert.Single(prefix.Fills["on_deploy"].Ops).Until);

        // `grant` 无期限面 ⇒ 不产"永久增益"，改留痕 needsCsx
        var grant = Assert.Single(Parser.Parse("使所有友方单位具有闪击，直到回合结束。").Effects);
        Assert.Equal(DslOpRegistry.NeedsCsxOpName, Assert.Single(grant.Fills["on_deploy"].Ops).Op);
    }

    [Fact]
    public void Parse_Reports_NeedsCsx_For_Unmappable_State()
    {
        // 计数（`每有`）／相位条件／不可表达限定词 → 映射成"单次无条件/打到宿主自己"即语义错误
        // ⇒ needsCsx（原文留痕）。`其他/其它` 已由 E1-56 的排除自身维度承载（不再在此列）。
        foreach (var text in new[]
                 {
                     "每有 1 个相邻单位，具有 +2 攻击力。",
                     "敌方回合中，具有 +3 攻击力。",
                     "相邻陆军具有 +2 攻击力。",
                     "本单位左侧所有单位具有 -1 行动花费。",
                 })
        {
            var result = Parser.Parse(text);
            Assert.Empty(result.Unresolved);
            var op = Assert.Single(Assert.Single(result.Effects).Fills["on_deploy"].Ops);
            Assert.Equal(DslOpRegistry.NeedsCsxOpName, op.Op);
        }
    }

    [Fact]
    public void Parse_Keeps_Signed_Value_And_Count_Phrase()
    {
        // 符号并入数值（修前 `-1` 会被读成 `+1`）
        var tokens = Parser.Tokenize("-1");
        var num = Assert.Single(tokens, token => token.Type == TokenType.Num);
        Assert.Equal(-1, num.IntValue);
        Assert.Equal("-1", num.Lexeme);

        // `造成 2 点伤害` 的载荷仍是 2（数量短语规则不误伤动词后的数值）
        var damage = Assert.Single(Parser.Parse("对一个敌方单位造成 2 点伤害。").Effects);
        Assert.Equal(2, Assert.Single(damage.Fills["on_deploy"].Ops).Amount);

        // `将 3 张“X”加入手中` 的张数仍是 3（紧跟引号卡名 ⇒ 不判为数量短语）
        var add = Assert.Single(Parser.Parse("将 3 张“先锋部队”加入手中。").Effects);
        Assert.Equal(3, Assert.Single(add.Fills["on_deploy"].Ops).Count);
    }

    [Fact]
    public void Parse_Maps_Combat_Survived_And_Slot_Listeners_With_Owner_Guard()
    {
        // E1-39：交战存活（载荷 {Unit} ⇒ 卡面归属守卫）
        var survived = Assert.Single(Parser.Parse("本单位交战并存活后，抽一张牌。").Effects);
        Assert.Equal("combat_survived_basic", survived.Template);
        var survivedOp = Assert.Single(survived.Fills["on_event"].Ops);
        Assert.Equal("draw", survivedOp.Op);
        Assert.Null(survivedOp.Condition); // "本单位"＝无阵营词 ⇒ 不加守卫

        var friendlySurvived = Assert.Single(Parser.Parse("友方单位对战并存活后，抽一张牌。").Effects);
        Assert.Equal("combat_survived_basic", friendlySurvived.Template);
        Assert.Equal(
            DslCondition.OwnerSame,
            Assert.Single(friendlySurvived.Fills["on_event"].Ops).Condition!.Kind);

        // E1-39：指挥点槽增减（载荷 {Player} ⇒ **玩家面**归属守卫）
        var slotGain = Assert.Single(Parser.Parse("友方额外获得 1 个指挥点槽时，抽一张牌。").Effects);
        Assert.Equal("slot_gained_basic", slotGain.Template);
        Assert.Equal(
            DslCondition.OwnerSameByPlayer,
            Assert.Single(slotGain.Fills["on_event"].Ops).Condition!.Kind);

        var slotLose = Assert.Single(Parser.Parse("友方失去 1 个指挥点槽时，抽一张牌。").Effects);
        Assert.Equal("slot_lost_basic", slotLose.Template);
        Assert.Equal(
            DslCondition.OwnerSameByPlayer,
            Assert.Single(slotLose.Fills["on_event"].Ops).Condition!.Kind);

        var enemySlotLose = Assert.Single(Parser.Parse("敌方失去 1 个指挥点槽时，抽一张牌。").Effects);
        Assert.Equal(
            DslCondition.OwnerDifferentByPlayer,
            Assert.Single(enemySlotLose.Fills["on_event"].Ops).Condition!.Kind);
    }

    [Fact]
    public void Parse_Maps_Drawn_Listener_With_Number_Between()
    {
        // E1-38：归一后为"抽1张牌"，原 `Contains("抽牌")` 判据恒不命中。
        var friendly = Assert.Single(Parser.Parse("友方抽 1 张牌时，获得 +1 攻击力。").Effects);
        Assert.Equal("drawn_basic", friendly.Template);
        Assert.Equal(
            DslCondition.OwnerSame,
            Assert.Single(friendly.Fills["on_event"].Ops).Condition!.Kind);

        var enemy = Assert.Single(Parser.Parse("敌方额外抽 1 张牌时，造成 4 点伤害。").Effects);
        Assert.Equal("drawn_basic", enemy.Template);
    }

    [Fact]
    public void Parse_Reports_Unresolved_For_Target_Declaration_Only_Effect()
    {
        // R8（E1-38 修正）：整效果只有目标声明/回指、无动作短语时，**不得**既不产效果又不产诊断。
        var result = Parser.Parse("敌方单位部署时，使其防御力为 1。");

        Assert.Empty(result.Effects);
        Assert.Contains(
            result.Unresolved,
            record => record.Reason.Contains("未产出任何可执行效果", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Listener_Player_Face_Condition_Renders_Player_Guard_In_Csx()
    {
        var dsl = Assert.Single(Parser.Parse("友方额外获得 1 个指挥点槽时，抽一张牌。").Effects);
        var templates = EffectTemplateLoader.LoadDirectory(EffectTemplateLoader.DefaultDirectory);
        var compiler = new EffectCompiler(templates.Templates, Ops);
        var csx = Assert.Single(compiler.Compile(dsl, "effect.slot.demo").Root.MainTrigger.Events).CsxSource!;

        Assert.Contains("view.Player is Orc.Game.Players.Player actorPlayer", csx, StringComparison.Ordinal);
        Assert.Contains("object.ReferenceEquals(actorSelf.Owner, actorPlayer)", csx, StringComparison.Ordinal);
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
