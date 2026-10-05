using Orc.Cards;
using Orc.Core;
using Orc.Game;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Players;
using Orc.Game.Targeting;

namespace Orc.Game.Sample;

/// <summary>
/// 示例前端桥接（mock UI）：实现 <see cref="ITargeterBridge"/> 两个阶段入口——
/// ①候选收集（<see cref="CollectCandidatesAsync"/>）：提交"前端当前可交互的完整引用列表"
///   （示例＝全战场槽位引用 ＋ 全在场单位引用 ＋ 双方 HQ 实体引用；后端再做两级筛选收敛）；
/// ②交互（<see cref="BeginInteraction"/>）：打印请求描述（槽位名／类别／数量／呈现标注／槽位参数／允许候选），
///   **自动模式**＝按策略应答（mulligan 槽位＝按 <see cref="AutoReplaceCount"/> 选前 N 张；其余＝第一个允许候选），
///   **交互模式**＝读控制台输入应答（序号多选／空＝空选／c＝取消）。
/// 用途＝示例宿主（**非生产实现**；生产侧由真实前端实现本接口——桥接是后端↔前端的唯一通道）。
/// </summary>
internal sealed class DemoTargeterBridge : ITargeterBridge
{
    private readonly bool _interactive;

    /// <summary>创建示例桥接。</summary>
    /// <param name="interactive">true＝读控制台输入应答；false＝自动应答（无人值守演示）。</param>
    public DemoTargeterBridge(bool interactive) => _interactive = interactive;

    /// <summary>所服务的对局（构造 Match 后回填——收集与交互需读对局状态）。</summary>
    public Match? Match { get; set; }

    /// <summary>自动模式下 mulligan 换牌张数（示例用；0＝不换）。</summary>
    public int AutoReplaceCount { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<object?>> CollectCandidatesAsync(TargetingCollectionContext context)
    {
        var match = RequireMatch();
        var references = new List<object?>();
        foreach (var slot in AllSlotsOf(match))
        {
            references.Add(slot.Ref);
        }

        foreach (var unit in AllUnitsOf(match))
        {
            references.Add(unit.Ref);
        }

        foreach (var player in match.Players)
        {
            references.Add(player.Hq.Ref);
        }

        return Task.FromResult<IReadOnlyList<object?>>(references);
    }

    /// <inheritdoc />
    public void BeginInteraction(TargetingRequestDescription description, ITargetingResponder responder)
    {
        Console.WriteLine($"  [交互] 请求 {description.RequestId[..8]}…");
        foreach (var slot in description.Slots)
        {
            var parameter = slot.HasParameter ? $"、槽位参数={Describe(slot.Parameter)}" : string.Empty;
            Console.WriteLine(
                $"    槽位 '{slot.Name}'：类别={slot.Kind}、数量={slot.Min}..{slot.Max}、呈现={slot.Presentation}{parameter}");
        }

        var primary = description.Slots[0];
        var candidates = CandidatesOf(description, primary);

        if (!_interactive)
        {
            AutoRespond(description, primary, candidates, responder);
            return;
        }

        InteractiveRespond(description, primary, candidates, responder);
    }

    // ---------- 自动模式 ----------

    private void AutoRespond(
        TargetingRequestDescription description,
        TargetSlotDescription slot,
        IReadOnlyList<Ref<Entity>> candidates,
        ITargetingResponder responder)
    {
        var picks = slot.Kind == TargetSlotKind.MulliganSelect
            ? candidates.Take(Math.Min(AutoReplaceCount, candidates.Count)).ToArray()
            : candidates.Take(1).ToArray();

        var map = new Dictionary<string, IReadOnlyList<TargetSelection>>(StringComparer.Ordinal)
        {
            [slot.Name] = picks.Select(TargetSelection.FromReference).ToArray(),
        };
        var accepted = responder.Complete(description.RequestId, map);
        Console.WriteLine(
            $"    [自动应答] 选中 {picks.Length} 项 → Complete（{(accepted ? "接受" : "被拒")}；候选 {candidates.Count} 个）");
    }

    // ---------- 交互模式 ----------

    private static void InteractiveRespond(
        TargetingRequestDescription description,
        TargetSlotDescription slot,
        IReadOnlyList<Ref<Entity>> candidates,
        ITargetingResponder responder)
    {
        Console.WriteLine("    允许候选：");
        for (var i = 0; i < candidates.Count; i++)
        {
            Console.WriteLine($"      [{i + 1}] {Describe(candidates[i].Value)}");
        }

        while (true)
        {
            Console.Write("    输入序号（逗号分隔可多选；直接回车＝空选；c＝取消）> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                return; // 输入流结束：不构成终局（请求继续等待）
            }

            line = line.Trim();
            if (line.Equals("c", StringComparison.OrdinalIgnoreCase))
            {
                var cancelled = responder.Cancel(description.RequestId);
                Console.WriteLine($"    [取消] {(cancelled ? "接受" : "被拒")}");
                return;
            }

            if (line.Length == 0)
            {
                if (slot.Min <= 0 && responder.Complete(description.RequestId, EmptySelection(slot.Name)))
                {
                    Console.WriteLine("    [空选] 已确认");
                    return;
                }

                Console.WriteLine($"    该槽位至少须选 {slot.Min} 项——请重新输入。");
                continue;
            }

            if (!TryParsePicks(line, candidates, out var picks))
            {
                Console.WriteLine("    输入非法（须为范围内的序号）——请重新输入。");
                continue;
            }

            var map = new Dictionary<string, IReadOnlyList<TargetSelection>>(StringComparer.Ordinal)
            {
                [slot.Name] = picks.Select(TargetSelection.FromReference).ToArray(),
            };
            if (responder.Complete(description.RequestId, map))
            {
                Console.WriteLine("    [确认] 已提交");
                return;
            }

            Console.WriteLine("    内容不合规（被拒）——请重新输入。");
        }
    }

    private static bool TryParsePicks(
        string line, IReadOnlyList<Ref<Entity>> candidates, out IReadOnlyList<Ref<Entity>> picks)
    {
        var result = new List<Ref<Entity>>();
        foreach (var part in line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var number) || number < 1 || number > candidates.Count)
            {
                picks = Array.Empty<Ref<Entity>>();
                return false;
            }

            result.Add(candidates[number - 1]);
        }

        picks = result;
        return true;
    }

    private static Dictionary<string, IReadOnlyList<TargetSelection>> EmptySelection(string slotName)
        => new(StringComparer.Ordinal) { [slotName] = Array.Empty<TargetSelection>() };

    // ---------- 读面辅助 ----------

    /// <summary>槽位允许候选：新引用类＝槽位快照（手牌/换牌/卡牌选择器）；既有引用类＝请求级允许子集。</summary>
    private static IReadOnlyList<Ref<Entity>> CandidatesOf(
        TargetingRequestDescription description, TargetSlotDescription slot)
        => slot.AllowedReferences ?? description.AllowedTargets;

    private static IEnumerable<Slot> AllSlotsOf(Match match)
        => match.Battlefield.PlayerASupportLine
            .Concat(match.Battlefield.FrontLine)
            .Concat(match.Battlefield.PlayerBSupportLine);

    private static IEnumerable<UnitCard> AllUnitsOf(Match match)
        => AllSlotsOf(match).Select(slot => slot.Occupant).OfType<UnitCard>();

    private Match RequireMatch()
        => Match ?? throw new InvalidOperationException("示例桥接未绑定对局（构造 Match 后须设置 Match 属性）。");

    private static string Describe(object? value) => value switch
    {
        null => "<空>",
        Ref<Entity> reference => Describe(reference.Value),
        Hq hq => Describe(hq.Owner),
        Player player => player.Index == 0 ? "玩家A" : "玩家B",
        Card card => card.Name,
        Slot slot => slot.IsEmpty ? "空槽" : $"槽（占：{Describe(slot.Occupant)}）",
        _ => value.ToString() ?? value.GetType().Name,
    };
}
