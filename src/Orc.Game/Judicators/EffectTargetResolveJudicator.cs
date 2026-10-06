using Orc.Cards;
using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Effects;
using Orc.Game.Players;

namespace Orc.Game.Judicators;

/// <summary>
/// 效果无头选靶判定器（效果运行期选择面）：把 <see cref="EffectSelector"/>（sel/side/zone/keyword/count）
/// 求值为一组单位——**无头**（不排队、不等 UI 桥），供 csx handler 经 <see cref="EffectRuntime.SelectAsync"/> 调用。
/// <para>形态＝「持有转发」：内部持有默认选择规则；moding 替换后整体更换（纯替换）。</para>
/// <para>无状态：只读枚举战场；不持有对局状态。名称＝<see cref="JudicatorNames.EffectTargetResolve"/>。</para>
/// <para>默认规则范围：side（friendly/enemy/both，按相对视角）＋ zone（frontline/support）＋ <b>unitType 过滤</b>
/// ＋ keyword 过滤 ＋ sel/count；<c>random</c> 经对局随机服务<b>真随机</b>取样（不可用＝确定性取首个）。</para>
/// </summary>
public sealed class EffectTargetResolveJudicator : Judicator<EffectTargetResolveJudicator.ResolveRule>
{
    /// <summary>强类型 delegate（该判定器签名）：（视角卡，选择器）→ 候选单位。</summary>
    /// <param name="viewer">视角卡（决定 friendly/enemy 相对性）。</param>
    /// <param name="selector">选择器。</param>
    /// <returns>候选单位（顺序＝战场槽位序）。</returns>
    public delegate IReadOnlyList<Card> ResolveRule(Card viewer, EffectSelector selector);

    private readonly ResolveRule _rule;

    /// <summary>创建判定器（持有默认选择规则——「持有转发」形态）。</summary>
    /// <param name="battlefield">战场（枚举来源）。</param>
    /// <param name="randomServiceProvider">对局随机服务解析器（random 取样；缺省＝确定性取首个）。</param>
    /// <exception cref="ArgumentNullException">battlefield 为 null。</exception>
    public EffectTargetResolveJudicator(
        Battlefield battlefield,
        Func<Card, MatchRandomService?>? randomServiceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        _rule = (viewer, selector) => DefaultRule(battlefield, viewer, selector, randomServiceProvider);
    }

    /// <inheritdoc />
    protected override Func<object[]?, object[]?> Adapt(ResolveRule handler)
        => args => new object[] { handler(Unpack<Card>(args, 0), Unpack<EffectSelector>(args, 1)) };

    /// <inheritdoc />
    public override object[]? Invoke(object[]? args) => Adapt(_rule)(args);

    private static IReadOnlyList<Card> DefaultRule(
        Battlefield battlefield,
        Card viewer,
        EffectSelector selector,
        Func<Card, MatchRandomService?>? randomServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(selector);

        var viewerOwner = OwnerOf(viewer);
        var lines = SelectLines(battlefield, selector.Zone);

        var candidates = new List<Card>();
        foreach (var line in lines)
        {
            foreach (var slot in line)
            {
                if (slot.Occupant is not UnitCard unit
                    || !MatchesSide(unit, selector.Side, viewerOwner)
                    || !MatchesUnitType(unit, selector.UnitType)
                    || !MatchesKeyword(unit, selector.Keyword))
                {
                    continue;
                }

                candidates.Add(unit);
            }
        }

        if (selector.Sel == "all")
        {
            return candidates;
        }

        var take = selector.Count is { } explicitCount && explicitCount > 0 ? explicitCount : 1;
        take = Math.Min(take, candidates.Count);
        if (take <= 0)
        {
            return Array.Empty<Card>();
        }

        if (selector.Sel == "random")
        {
            var random = randomServiceProvider?.Invoke(viewer);
            if (random is not null)
            {
                return random.PickN(candidates, take);
            }
        }

        return candidates.GetRange(0, take);
    }

    private static IEnumerable<BattleLine> SelectLines(Battlefield battlefield, string? zone)
    {
        if (zone is null or "support")
        {
            yield return battlefield.PlayerASupportLine;
            yield return battlefield.PlayerBSupportLine;
        }

        if (zone is null or "frontline")
        {
            yield return battlefield.FrontLine;
        }
    }

    private static bool MatchesSide(UnitCard unit, string? side, Player? viewerOwner)
    {
        if (side is null or "both" or "any")
        {
            return true;
        }

        if (viewerOwner is null)
        {
            return true;
        }

        var unitIndex = unit.Owner?.Index;
        return side switch
        {
            "friendly" => unitIndex == viewerOwner.Index,
            "enemy" => unitIndex is { } index && index != viewerOwner.Index,
            _ => true,
        };
    }

    private static bool MatchesKeyword(UnitCard unit, string? keyword) =>
        string.IsNullOrWhiteSpace(keyword) || unit.Keywords.Has(keyword);

    /// <summary>兵种过滤：选择器给的是词表键（如 <c>infantry</c>），按 <see cref="UnitType"/> 枚举解析后比对单位类型清单。</summary>
    private static bool MatchesUnitType(UnitCard unit, string? unitType)
    {
        if (string.IsNullOrWhiteSpace(unitType))
        {
            return true;
        }

        if (!Enum.TryParse<UnitType>(unitType, ignoreCase: true, out var parsed))
        {
            return false; // 未知兵种词：不匹配（保守——不静默放行）
        }

        return unit.GetData<UnitStateData>().UnitTypes.Contains(parsed);
    }

    private static Player? OwnerOf(Card card) => card switch
    {
        Hq hq => hq.Owner,
        CardBase cardBase => cardBase.Owner,
        _ => null,
    };
}
