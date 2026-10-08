using Orc.Game.EffectParsing.Lexicon;
using Orc.Game.EffectParsing.Parsing;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 词条行联动·批 4 序列③：词条行**声明产出**专项验收——
/// 正例（无参）／参值（阿拉伯＋中文数字）／边界（一行多词条、成分级异常、同行不产出、空白豁免）／未知（未注册标注、不成词法既有路径）。
/// <para>样本集（<see cref="Samples"/>）与机制报告「词条行（声明产出）」节**同源**（逐例断言随行）。</para>
/// </summary>
public class KeywordLineDeclarationTests
{
    private static readonly (EffectParser Parser, IReadOnlyList<LexiconLoadFailure> Failures) Setup = Create();

    private static EffectParser Parser => Setup.Parser;

    private static (EffectParser, IReadOnlyList<LexiconLoadFailure>) Create()
    {
        var parser = EffectParser.CreateDefault(out var failures);
        return (parser, failures);
    }

    /// <summary>
    /// 词条行专项样本（四类 ≥1；Id／类别／原文／期望声明串／未解析数／效果数／备注）——
    /// 期望声明串格式＝`维度:标识=参值@注册状态`（多个以 "; " 分隔）。
    /// </summary>
    public static readonly (string Id, string Category, string Text, string Declarations, int Unresolved, int Effects, string Note)[] Samples =
    {
        // 正例（无参）
        ("K1", "正例", "闪击", "Keyword:闪击=null@Registered", 0, 0, "词条行（无参）→ 声明；不产效果单元（原「丢弃」语义保留）"),
        ("K2", "正例", "隐蔽", "Keyword:隐蔽=null@Registered", 0, 0, "词条行（无参）→ 声明"),
        // 参值（阿拉伯＋中文数字）
        ("K3", "参值", "重甲 3", "Keyword:重甲=3@Registered", 0, 0, "紧邻后随数字→参值（整数）"),
        ("K4", "参值", "情报 2", "Keyword:情报=2@Registered", 0, 0, "紧邻后随数字→参值"),
        ("K5", "参值", "重甲三", "Keyword:重甲=3@Registered", 0, 0, "中文数字词（Num 形态族）→ 整数化参值"),
        // 边界
        ("K6", "边界", "闪击 重甲 3", "Keyword:闪击=null@Registered; Keyword:重甲=3@Registered", 0, 0, "一行多词条→逐词条分列（行内序）"),
        ("K7", "边界", "重甲 3、情报 2", "Keyword:重甲=3@Registered; Keyword:情报=2@Registered", 0, 0, "一行多词条（标点分隔）→ 逐词条分列"),
        ("K8", "边界", "重甲 3 3", "Keyword:重甲=3@Registered", 1, 0, "成分级：多余数字→异常记录（合法对仍产出）"),
        ("K9", "边界", "3 重甲", "Keyword:重甲=null@Registered", 1, 0, "成分级：数字在前→异常记录；词条参值空缺"),
        ("K10", "边界", "重甲 3情报", "Keyword:重甲=3@Registered; Keyword:情报=null@Registered", 0, 0, "数字后紧接词条→按各自形态继续（参值归前邻）"),
        ("K11", "边界", "重甲 3。部署：抽 1 张牌。", "", 1, 1, "词条与效果同行（整行判定不成立）→ 不产出声明、走既有效果路径"),
        ("K12", "边界", "", "", 0, 0, "空白行豁免（无内容可记）"),
        ("K16", "边界", "重甲 +3", "Keyword:重甲=null@Registered", 1, 0, "含符号数词不构成参值（参值限无符号非负整数）→ 异常记录"),
        ("K17", "边界", "步兵 3", "UnitType:步兵=null@NotApplicable", 1, 0, "兵种声明参值位恒空（口径裁量点，申报）；紧邻数字无法归属→异常"),
        ("K18", "边界", "攻击力 2", "Attribute:攻击力=2@NotApplicable", 0, 0, "属性声明（紧邻数字，如有）"),
        ("K19", "边界", "闪击\n部署：抽 1 张牌。", "Keyword:闪击=null@Registered", 0, 1, "独立词条行（按换行为界）→ 声明＋效果面不变"),
        // 未知
        ("K13", "未知", "游击", "Keyword:游击=null@Unregistered", 0, 0, "成词法未注册→产出并标注（不丢弃、不静默）"),
        ("K14", "未知", "炮击 3", "", 1, 0, "不成词法（含 Unknown）→ 整行判定不成立、走既有解析路径（未解析留痕）"),
        ("K15", "未知", "闪击 炮击 3", "", 1, 0, "混行（已知＋未知）→ 整行不产出、走既有解析路径"),
    };

    /// <summary>声明摘要（与报告节同源格式）：`维度:标识=参值@注册状态`。</summary>
    public static string Describe(LineDeclaration declaration) =>
        $"{declaration.Dimension}:{declaration.Id}={declaration.Value?.ToString() ?? "null"}@{declaration.Registration}";

    [Fact]
    public void Lexicons_Load_Without_Failures()
    {
        Assert.Empty(Setup.Failures);
    }

    [Theory]
    [MemberData(nameof(SampleData))]
    public void Keyword_Line_Samples(string id, string category, string text, string declarations, int unresolved, int effects, string note)
    {
        var result = Parser.Parse(text);
        var actual = string.Join("; ", result.Declarations.Select(Describe));

        Assert.True(
            declarations == actual,
            $"样本 {id}（{category}）：声明产出不符——期望「{declarations}」，实际「{actual}」。{note}");
        Assert.True(
            unresolved == result.Unresolved.Count,
            $"样本 {id}（{category}）：未解析数不符——期望 {unresolved}，实际 {result.Unresolved.Count}。{note}");
        Assert.True(
            effects == result.Effects.Count,
            $"样本 {id}（{category}）：效果数不符——期望 {effects}，实际 {result.Effects.Count}。{note}");
    }

    public static TheoryData<string, string, string, string, int, int, string> SampleData()
    {
        var data = new TheoryData<string, string, string, string, int, int, string>();
        foreach (var sample in Samples)
        {
            data.Add(sample.Id, sample.Category, sample.Text, sample.Declarations, sample.Unresolved, sample.Effects, sample.Note);
        }

        return data;
    }

    [Fact]
    public void Declarations_Minimum_Required_Set_And_Source_Span()
    {
        // 最小必需集＝维度＋标识＋参值＋注册状态；来源定位＝原文 span（建议携带）。
        var result = Parser.Parse("重甲 3");
        var declaration = Assert.Single(result.Declarations);
        Assert.Equal(DeclarationDimension.Keyword, declaration.Dimension);
        Assert.Equal("重甲", declaration.Id);
        Assert.Equal(3, declaration.Value);
        Assert.Equal(DeclarationRegistration.Registered, declaration.Registration);
        Assert.Equal(new TextSpan(0, 2), declaration.Span);
    }

    [Fact]
    public void Declarations_Order_And_Duplicates_Preserved()
    {
        // 声明序＝行序＋行内词条序；保留重复（层不判重）。
        var multiline = Parser.Parse("闪击\n重甲 3");
        Assert.Equal(
            new[] { "闪击", "重甲" },
            multiline.Declarations.Select(item => item.Id).ToArray());

        var duplicated = Parser.Parse("闪击 闪击");
        Assert.Equal(2, duplicated.Declarations.Count);
        Assert.All(duplicated.Declarations, item => Assert.Equal("闪击", item.Id));
    }

    [Fact]
    public void Declarations_Pure_Function_Deterministic()
    {
        // 纯函数性：同输入同产出（无副作用、不依赖外部状态）。
        const string text = "闪击\n重甲 3、情报 2\n游击";
        var first = Parser.Parse(text);
        var second = Parser.Parse(text);
        Assert.Equal(
            first.Declarations.Select(Describe).ToArray(),
            second.Declarations.Select(Describe).ToArray());
        Assert.Equal(first.Unresolved.Count, second.Unresolved.Count);
        Assert.Equal(first.Effects.Count, second.Effects.Count);
    }

    [Fact]
    public void Declarations_Do_Not_Produce_Effect_Units()
    {
        // 判定成立行仍不产效果单元（原「丢弃」语义保留）：单独词条行无效果、无未解析。
        var result = Parser.Parse("游击");
        Assert.Empty(result.Effects);
        Assert.Empty(result.Unresolved);
        Assert.Single(result.Declarations);
    }

    [Fact]
    public void Anomalies_Are_Reachable_Through_Unresolved_Records()
    {
        // 异常记录归既有无解析记录面（原文＋可辨识原因）——不新增独立异常面。
        var result = Parser.Parse("重甲 3 3");
        var anomaly = Assert.Single(result.Unresolved);
        Assert.Equal("3", anomaly.RawText);
        Assert.Contains("无法作为参值", anomaly.Reason, StringComparison.Ordinal);
        Assert.Equal(5, anomaly.Start);
        Assert.Equal(1, anomaly.Length);
    }
}
