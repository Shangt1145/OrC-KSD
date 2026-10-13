using Orc.Cards;
using Orc.Game.EffectParsing.Compilation;
using Orc.Game.EffectParsing.Dsl;
using Orc.Game.EffectParsing.Templates;

namespace Orc.Game.EffectParsing;

// ─────────────────────────────────────────────────────────────────────────────
// 卡面文本 → 效果（前端资产迁移 S2b）：把前端 `PackCardTextParser` + `DiyCardCompiler.CompileEffects`
// 的**卡包文本→效果**能力上移引擎（D3·Q9d），供引擎卡包管道复用。
// 语法与 `tools/build_builtin_packs.py` 的准入预检同面（两侧须同步演进）；不支持＝null（调用方按漂移报错）。
// 编译复用引擎既有 `EffectCompilationDriver`（不新建第二套编译链）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>卡面文本解析出的一条效果草稿（引擎侧形态；与前端 DIY 草稿同构子集）。</summary>
public sealed record CardTextEffectDraft
{
    /// <summary>操作名（draw/gainPoint/gainSlot/damage/destroy/pin/silence/buff/costMod）。</summary>
    public string Op { get; init; } = "draw";

    /// <summary>选靶（self/friendlyUnits/enemyUnits/friendlyUnit/enemyUnit/anyUnit/bothUnits）。</summary>
    public string Target { get; init; } = "self";

    /// <summary>数量/数值。</summary>
    public int Amount { get; init; }

    /// <summary>攻增益（buff）。</summary>
    public int Attack { get; init; }

    /// <summary>防增益（buff）。</summary>
    public int Defense { get; init; }

    /// <summary>期限（空＝持续）。</summary>
    public string Until { get; init; } = "";
}

/// <summary>
/// 卡面文本 → 效果草稿 → 编译为效果快照的引擎侧能力（卡包管道与 DIY 共用）。
/// </summary>
public static class CardTextEffectCompiler
{
    private const string TargetAlternation =
        "(?:1\\s*个敌方(?:单位|目标)|1\\s*个友方单位|1\\s*个(?:单位|目标)|所有敌方单位|所有友方单位|所有(?:单位|目标))";

    private static readonly string[] TargetTokens =
        ["self", "friendlyUnits", "enemyUnits", "friendlyUnit", "enemyUnit", "anyUnit", "bothUnits"];

    private static readonly string[] OperationTokens =
        ["draw", "gainPoint", "gainSlot", "damage", "buff", "pin", "silence", "costMod", "destroy"];

    private static readonly string[] DurationTokens = ["", "turnEnd", "nextOwnerTurnStart"];

    private static readonly Lazy<OpTemplateCatalog> Ops = new(() =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "EffectParsing", "Templates", "ops");
        if (!Directory.Exists(directory))
        {
            return OpTemplateCatalog.LoadFiles(Array.Empty<string>(), out _);
        }

        var files = Directory.EnumerateFiles(directory, "*.csx.tpl", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal);
        var catalog = OpTemplateCatalog.LoadFiles(files, out var failures);
        if (failures.Count > 0)
        {
            throw new InvalidDataException("效果 op 模板装载失败：" + string.Join("；", failures.Select(f => f.Error)));
        }

        return catalog;
    });

    /// <summary>解析卡面文本。空文本＝空草稿集；不可解析＝null（＝生成器与解析器漂移）。</summary>
    /// <exception cref="ArgumentNullException">cardType 为 null。</exception>
    public static IReadOnlyList<CardTextEffectDraft>? Parse(string? text, string cardType)
    {
        ArgumentNullException.ThrowIfNull(cardType);

        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<CardTextEffectDraft>();
        }

        var body = text.Trim().TrimEnd('。').Trim();
        var hadDeployPrefix = body.StartsWith("部署：", StringComparison.Ordinal);
        if (hadDeployPrefix)
        {
            body = body["部署：".Length..].Trim();
        }

        if (cardType == "unit" && !hadDeployPrefix)
        {
            return null; // 无前缀单位文本＝被动/光环，不在 v1 面内
        }

        var drafts = new List<CardTextEffectDraft>();
        foreach (var raw in body.Split('；', ';'))
        {
            var sentence = raw.Trim().TrimEnd('。').Trim();
            if (sentence.Length == 0)
            {
                continue;
            }

            var draft = ParseSentence(sentence);
            if (draft is null)
            {
                return null;
            }

            drafts.Add(draft);
        }

        return drafts;
    }

    /// <summary>
    /// 把草稿序列编译为效果快照（复用 <see cref="EffectCompilationDriver"/>）。
    /// 任一步失败/留痕/占位条件/产物数≠1 ＝ <see cref="InvalidDataException"/>（拒绝半成品效果）。
    /// </summary>
    /// <exception cref="ArgumentNullException">drafts 为 null。</exception>
    /// <exception cref="ArgumentException">compileId 为 null/空白。</exception>
    public static EffectSnapshot Compile(string cardType, IReadOnlyList<CardTextEffectDraft> drafts, string compileId)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentException.ThrowIfNullOrWhiteSpace(compileId);
        foreach (var draft in drafts)
        {
            CheckEffect(cardType, draft);
        }

        var template = Template(cardType);
        var driver = new EffectCompilationDriver();
        driver.UseEffectAssets([template], Ops.Value);
        var ops = drafts.Select(d => ToDsl(d, cardType)).ToArray();
        var dsl = new DslEffectInstance(template.Id, new Dictionary<string, DslSlotFill> { ["on_event"] = new(ops) });
        var result = driver.CompileDsl(DslJson.Serialize(dsl), compileId.Replace('/', '.') + ".effect");
        if (result.Report.Failures.Count > 0 || result.Report.UnresolvedTraces.Count > 0
            || result.Report.NeedsCsxTraces.Count > 0 || result.Report.PlaceholderConditionTraces.Count > 0
            || result.Artifacts.Count != 1)
        {
            throw new InvalidDataException("效果无法完整编译：" + string.Join("；",
                result.Report.Failures.Select(f => f.Reason)
                    .Concat(result.Report.Inputs.Where(i => i.FileFailureReason is not null).Select(i => i.FileFailureReason!))
                    .Concat(result.Report.UnresolvedTraces.Select(t => t.Reason))
                    .Concat(result.Report.NeedsCsxTraces.Select(t => $"{t.Location} 需要补全脚本：{t.Script}"))
                    .Concat(result.Report.PlaceholderConditionTraces.Select(t => $"{t.Location} 条件未实现：{t.RawText}"))));
        }

        return result.Artifacts[0].Snapshot;
    }

    // ------------------------------------------------------------------ 语法

    private static CardTextEffectDraft? ParseSentence(string s) =>
        Num(@"^抽\s*(?<n>\d+)\s*张牌$", s, out var n) ? D("draw", "self", n)
        : Num(@"^(?:额外)?获得\s*(?<n>\d+)\s*个指挥点槽$", s, out n) ? D("gainSlot", "self", n)
        : Num(@"^获得\s*(?<n>\d+)\s*个指挥点$", s, out n) ? D("gainPoint", "self", n)
        : Two(@"^对\s*(?<t>__T__)\s*造成\s*(?<n>\d+)\s*点伤害$", s, out var t, out n) ? D("damage", t, n)
        : Target(@"^消灭\s*(?<t>__T__)$", s, out t) ? D("destroy", t, null)
        : Target(@"^压制\s*(?<t>__T__)(?:\s*1\s*回合)?$", s, out t) ? D("pin", t, 1)
        : Target(@"^抑制\s*(?<t>__T__)$", s, out t) ? D("silence", t, null)
        : Buff(@"^使\s*(?<t>__T__)\s*获得\s*(?<a>[+-]?\d+)\s*/\s*(?<d>[+-]?\d+)$", s, out t, out var a, out var d)
            ? D("buff", t, null, a, d)
        : Num(@"^使\s*(?<t>__T__)\s*获得\s*(?<n>[+-]?\d+)\s*行动花费$", s, out n, t: out t)
            ? Math.Abs(n) is > 0 and <= 20 ? D("costMod", t, n) : null
        : null;

    private static string Pattern(string pattern) => pattern.Replace("__T__", TargetAlternation);

    private static CardTextEffectDraft D(string op, string target, int? amount, int? attack = null, int? defense = null) =>
        new()
        {
            Op = op,
            Target = target,
            Amount = amount ?? 0,
            Attack = attack ?? 0,
            Defense = defense ?? 0,
            Until = "",
        };

    private static bool Num(string pattern, string s, out int n) => Num(pattern, s, out n, out _);

    private static bool Num(string pattern, string s, out int n, out string t)
    {
        n = 0;
        t = "";
        var m = System.Text.RegularExpressions.Regex.Match(s, Pattern(pattern));
        if (!m.Success)
        {
            return false;
        }

        if (!int.TryParse(m.Groups["n"].Value, out n))
        {
            return false;
        }

        if (m.Groups["t"].Success)
        {
            t = Side(m.Groups["t"].Value);
        }

        return true;
    }

    private static bool Two(string pattern, string s, out string t, out int n)
    {
        t = "";
        n = 0;
        var m = System.Text.RegularExpressions.Regex.Match(s, Pattern(pattern));
        if (!m.Success)
        {
            return false;
        }

        t = Side(m.Groups["t"].Value);
        return int.TryParse(m.Groups["n"].Value, out n);
    }

    private static bool Target(string pattern, string s, out string t)
    {
        t = "";
        var m = System.Text.RegularExpressions.Regex.Match(s, Pattern(pattern));
        if (!m.Success)
        {
            return false;
        }

        t = Side(m.Groups["t"].Value);
        return true;
    }

    private static bool Buff(string pattern, string s, out string t, out int a, out int d)
    {
        t = "";
        a = 0;
        d = 0;
        var m = System.Text.RegularExpressions.Regex.Match(s, Pattern(pattern));
        if (!m.Success)
        {
            return false;
        }

        t = Side(m.Groups["t"].Value);
        return int.TryParse(m.Groups["a"].Value, out a) && int.TryParse(m.Groups["d"].Value, out d);
    }

    private static string Side(string text) => text switch
    {
        "所有敌方单位" => "enemyUnits",
        "所有友方单位" => "friendlyUnits",
        "所有单位" or "所有目标" => "bothUnits",
        "1 个敌方单位" or "1 个敌方目标" => "enemyUnit",
        "1 个友方单位" => "friendlyUnit",
        _ => "anyUnit",
    };

    // ------------------------------------------------------------------ 编译

    private static EffectTemplate Template(string type)
    {
        // 效果宿主 actor ＋ payload.self 守卫：把每次触发限制在本卡自身。
        var hook = type == "unit" ? "unit.deployed" : "card.played";
        return EffectTemplateJson.Deserialize(System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            id = "diy_" + type,
            actorFrom = "effectHost",
            slots = new[] { new { name = "on_event", target = "mainTrigger.events[e1]" } },
            root = new
            {
                mainTrigger = new
                {
                    id = "t_diy",
                    stableKey = "diy." + type,
                    kind = "passive",
                    viewType = "Orc.Cards.CardEventView",
                    hooks = new[] { hook },
                    events = new[] { new { id = "e1", entry = "HandleAsync" } },
                },
                otherTriggers = Array.Empty<object>(),
                modings = Array.Empty<object>(),
                injects = Array.Empty<object>(),
            },
        }));
    }

    private static void CheckEffect(string type, CardTextEffectDraft e)
    {
        if (!OperationTokens.Contains(e.Op) || !TargetTokens.Contains(e.Target) || !DurationTokens.Contains(e.Until))
        {
            throw new InvalidDataException("效果包含未知操作、目标或期限");
        }

        if (e.Op is not ("buff" or "costMod") && e.Until.Length != 0)
        {
            throw new InvalidDataException("只有攻防增益和行动费修饰可指定期限");
        }

        if (e.Op is "draw" or "gainPoint" or "gainSlot")
        {
            if (e.Amount is < 1 or > 10 || e.Target != "self")
            {
                throw new InvalidDataException("抽牌与资源效果的数量须为 1–10，归属为自己");
            }
        }
        else
        {
            if (type == "order" && e.Target == "self")
            {
                throw new InvalidDataException("指令的单位效果须选择友方或敌方单位");
            }

            if (e.Op == "damage" && e.Amount is < 1 or > 99)
            {
                throw new InvalidDataException("伤害须为 1–99");
            }

            if (e.Op == "pin" && e.Amount != 1)
            {
                throw new InvalidDataException("当前原生压制模板只支持 1 回合");
            }

            if (e.Op == "costMod" && (e.Amount is < -20 or > 20 || e.Amount == 0))
            {
                throw new InvalidDataException("行动费变化须为 -20–20，不能为 0");
            }

            if (e.Op == "buff" && (e.Attack is < -20 or > 20 || e.Defense is < -20 or > 20 || e.Attack == 0 && e.Defense == 0))
            {
                throw new InvalidDataException("增益攻防须为 -20–20，不能同时为 0");
            }
        }
    }

    private static DslOp ToDsl(CardTextEffectDraft e, string type) => new(e.Op,
        target: e.Target switch
        {
            "self" => new("self"),
            "friendlyUnits" => new("all", "friendly"),
            "enemyUnits" => new("all", "enemy"),
            "friendlyUnit" => new("one", "friendly"),
            "enemyUnit" => new("one", "enemy"),
            "anyUnit" => new("one", "both"),
            "bothUnits" => new("all", "both"),
            _ => throw new InvalidDataException("未知选靶：" + e.Target),
        },
        amount: e.Op is "damage" or "gainPoint" or "gainSlot" or "costMod" ? e.Amount : null,
        count: e.Op == "draw" ? e.Amount : null,
        attack: e.Op == "buff" ? e.Attack : null,
        defense: e.Op == "buff" ? e.Defense : null,
        condition: new DslCondition(DslCondition.PayloadSelf, type == "unit" ? "Unit" : "Card"),
        until: e.Until.Length == 0 ? null : e.Until);
}
