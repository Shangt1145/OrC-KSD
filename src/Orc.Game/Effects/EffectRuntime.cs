using Orc.Cards;
using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Judicators;
using Orc.Game.Managers;
using Orc.Game.Players;

namespace Orc.Game.Effects;

/// <summary>无头选靶描述（效果运行期）：sel/side/zone/unitType/keyword/count 的最小集。</summary>
/// <param name="Sel">选靶方式（all/one/any/random）。</param>
/// <param name="Side">阵营（friendly/enemy/both；null＝both）。</param>
/// <param name="Zone">区域（frontline/support；null＝两者）。</param>
/// <param name="UnitType">兵种过滤（null＝不过滤）。</param>
/// <param name="Keyword">词条过滤（null＝不过滤）。</param>
/// <param name="Count">数量（null＝按 sel 默认）。</param>
public sealed record EffectSelector(
    string Sel,
    string? Side = null,
    string? Zone = null,
    string? UnitType = null,
    string? Keyword = null,
    int? Count = null);

/// <summary>
/// 效果运行时门面（csx handler 的**唯一游戏层受控入口**）：把「消灭（死亡链）／伤害／属性修饰／词条授予／
/// 抽牌／无头选靶／指挥点槽加·减／指挥点加·减（E1-25）」收敛到一处集中暴露——不给各服务零散加 public 面（便于审计与替换）。
/// <para>解析路径与既有服务同构：<see cref="ResolveFor"/>（卡 → 玩家 → 服务）。</para>
/// <para>服务不可达（未装配/脱局）＝**降级不抛错**：返回 false／空集（沿用「功能不可用＝不失败」口径）。</para>
/// </summary>
public sealed class EffectRuntime
{
    private readonly Func<CommandManager?> _commands;
    private readonly Func<PlayerManager?> _players;
    private readonly Func<JudicatorRegistry?> _judicators;
    private readonly Func<ResourceManager?> _resources;

    /// <summary>创建运行时门面（协作者以延迟读取注入——装配链时序无关）。</summary>
    /// <exception cref="ArgumentNullException">任一访问器为 null。</exception>
    public EffectRuntime(
        Func<CommandManager?> commands,
        Func<PlayerManager?> players,
        Func<JudicatorRegistry?> judicators,
        Func<ResourceManager?> resources)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(judicators);
        ArgumentNullException.ThrowIfNull(resources);
        _commands = commands;
        _players = players;
        _judicators = judicators;
        _resources = resources;
    }

    /// <summary>卡 → 运行时解析（「卡 → 玩家 → 服务」收敛点；脱局/未注入＝null）。</summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    public static EffectRuntime? ResolveFor(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card switch
        {
            Hq hq => hq.Owner.EffectRuntime,
            CardBase cardBase => cardBase.Owner?.EffectRuntime,
            _ => null,
        };
    }

    /// <summary>消灭（**游戏层死亡链**：亡计/词条注销/修饰清理/<c>card.died</c>；与总线 <c>card.destroyed</c> 语义不同）。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> KillAsync(Card target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_commands() is not { } commands || target is not UnitCard unit)
        {
            return false;
        }

        await commands.KillUnitAsync(unit, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>造成伤害（游戏语义：单位扣防御、总部扣血）。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> DamageAsync(Card target, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        switch (target)
        {
            case UnitCard unit:
                await unit.ApplyDefenseDamageAsync(amount, ct).ConfigureAwait(false);
                return true;
            case Hq hq:
                await hq.ApplyDamageAsync(amount, ct).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>属性修饰（攻击力/防御力增减；来源＝本门面实例——撤销请经 <c>RemoveBySourceAsync</c>）。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> BuffAsync(Card target, int attack, int defense, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not CardBase cardBase)
        {
            return false;
        }

        var modifiers = new List<Modifier>();
        if (attack != 0)
        {
            modifiers.Add(new AddModifier(CardStatFields.Attack, attack, this));
        }

        if (defense != 0)
        {
            modifiers.Add(new AddModifier(CardStatFields.Defense, defense, this));
        }

        if (modifiers.Count == 0)
        {
            return false;
        }

        await cardBase.Modifiers.AddModifiersAsync(modifiers, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>词条授予。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public Task<bool> GrantAsync(Card target, string keyword, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (target is not CardBase cardBase)
        {
            return Task.FromResult(false);
        }

        return cardBase.Keywords.GrantAsync(keyword);
    }

    /// <summary>抽牌（走玩家管理器——含满手爆牌裁决）；返回请求的抽牌次数（服务不可达＝0）。</summary>
    /// <exception cref="ArgumentNullException">viewer 为 null。</exception>
    public async Task<int> DrawAsync(Card viewer, int count, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (count <= 0 || _players() is not { } players || OwnerOf(viewer) is not { } player)
        {
            return 0;
        }

        for (var i = 0; i < count; i++)
        {
            await players.DrawCard(player, ct).ConfigureAwait(false);
        }

        return count;
    }

    /// <summary>行动花费修饰（+N 花费；缺省来源＝本门面实例）。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> CostModAsync(Card target, int delta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not CardBase cardBase || delta == 0)
        {
            return false;
        }

        await cardBase.Modifiers
            .AddModifierAsync(new AddModifier(CardStatFields.OperateCost, delta, this), ct)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>加入手牌（按**卡名**在卡池解析定义；不可用/未命中＝0）。</summary>
    /// <exception cref="ArgumentNullException">viewer 为 null。</exception>
    public async Task<int> AddToHandAsync(Card viewer, string cardName, int count, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardName);
        if (count <= 0 || MatchCardService.ResolveFor(viewer) is not { } service || OwnerOf(viewer) is not { } player)
        {
            return 0;
        }

        if (ResolveDefinitionIdByName(service, cardName) is not { } definitionId)
        {
            return 0;
        }

        for (var i = 0; i < count; i++)
        {
            await service.CreateAndPlaceToHandAsync(definitionId, player, ct).ConfigureAwait(false);
        }

        return count;
    }

    /// <summary>洗入卡组（按**卡名**解析定义；发 <c>deck.shuffled</c>；不可用/未命中＝0）。</summary>
    /// <exception cref="ArgumentNullException">viewer 为 null。</exception>
    public async Task<int> ShuffleInAsync(Card viewer, string cardName, int count, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardName);
        if (count <= 0 || MatchCardService.ResolveFor(viewer) is not { } service || OwnerOf(viewer) is not { } player)
        {
            return 0;
        }

        if (ResolveDefinitionIdByName(service, cardName) is not { } definitionId)
        {
            return 0;
        }

        for (var i = 0; i < count; i++)
        {
            await service.CreateAndPlaceIntoDeckShuffledAsync(definitionId, player, ct).ConfigureAwait(false);
        }

        return count;
    }

    /// <summary>压制（游戏层压制服务；施加前资格判定，拒绝＝false）。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> PinAsync(Card target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target is UnitCard unit && await SuppressRules.ApplyAsync(unit).ConfigureAwait(false);
    }

    /// <summary>抑制（游戏层抑制服务——清空处置全链；资格拒绝/非单位＝false）。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public Task<bool> SilenceAsync(Card target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target is UnitCard unit ? InhibitRules.ApplyAsync(unit, ct) : Task.FromResult(false);
    }

    /// <summary>移动（无头；zone＝<c>frontline</c>／<c>support</c>；不可用＝false）。</summary>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> MoveAsync(Card target, string zone, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(zone);
        if (_commands() is not { } commands || target is not UnitCard unit)
        {
            return false;
        }

        return await commands.MoveUnitAsync(unit, zone, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 额外获得指挥点槽（E1-25；卡 → 玩家 → 对局资源管理器；不可达＝false）。
    /// 槽实际变化才经由资源管理器发 <c>slot.gained</c>→<c>slot.changed</c>。
    /// </summary>
    /// <exception cref="ArgumentNullException">viewer 为 null。</exception>
    public async Task<bool> GainPointSlotsAsync(Card viewer, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (amount <= 0 || _resources() is not { } resources || OwnerOf(viewer) is not { } player)
        {
            return false;
        }

        return await resources.GainSlotsAsync(player, amount, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 失去指挥点槽（E1-25；卡 → 玩家 → 对局资源管理器；不可达＝false）。
    /// 槽实际变化才经由资源管理器发 <c>slot.lost</c>→<c>slot.changed</c>。
    /// </summary>
    /// <exception cref="ArgumentNullException">viewer 为 null。</exception>
    public async Task<bool> LosePointSlotsAsync(Card viewer, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (amount <= 0 || _resources() is not { } resources || OwnerOf(viewer) is not { } player)
        {
            return false;
        }

        return await resources.LoseSlotsAsync(player, amount, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 额外获得指挥点（E1-25 后续；卡效果语义路——卡 → 玩家 → 对局资源管理器；不可达＝false）。
    /// 点数实际增加才经由资源管理器发 <c>point.gained</c>→<c>point.changed</c>。
    /// </summary>
    /// <exception cref="ArgumentNullException">viewer 为 null。</exception>
    public async Task<bool> GainPointsAsync(Card viewer, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (amount <= 0 || _resources() is not { } resources || OwnerOf(viewer) is not { } player)
        {
            return false;
        }

        return await resources.GainPointsAsync(player, amount, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 失去指挥点（E1-25 后续；卡效果语义路——卡 → 玩家 → 对局资源管理器；不可达＝false）。
    /// 点数实际减少才经由资源管理器发 <c>point.lost</c>→<c>point.changed</c>。
    /// </summary>
    /// <exception cref="ArgumentNullException">viewer 为 null。</exception>
    public async Task<bool> LosePointsAsync(Card viewer, int amount, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (amount <= 0 || _resources() is not { } resources || OwnerOf(viewer) is not { } player)
        {
            return false;
        }

        return await resources.LosePointsAsync(player, amount, ct).ConfigureAwait(false);
    }

    /// <summary>无头选靶（经游戏层判定器 <see cref="JudicatorNames.EffectTargetResolve"/> 求值；不可用＝空集）。</summary>
    /// <exception cref="ArgumentNullException">viewer 或 selector 为 null。</exception>
    public Task<IReadOnlyList<Card>> SelectAsync(Card viewer, EffectSelector selector, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(selector);

        if (_judicators() is not { } judicators)
        {
            return Task.FromResult<IReadOnlyList<Card>>(Array.Empty<Card>());
        }

        var result = judicators.Invoke(JudicatorNames.EffectTargetResolve, new object[] { viewer, selector });
        return Task.FromResult(
            result is { Length: > 0 } && result[0] is IReadOnlyList<Card> cards
                ? cards
                : (IReadOnlyList<Card>)Array.Empty<Card>());
    }

    /// <summary>按**卡名**在卡池反查定义 id（首个同名；未命中＝null）。</summary>
    private static string? ResolveDefinitionIdByName(MatchCardService service, string cardName)
    {
        foreach (var pair in service.RegisteredDefinitions)
        {
            if (string.Equals(pair.Value.Name, cardName, StringComparison.Ordinal))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static Player? OwnerOf(Card card) => card switch
    {
        Hq hq => hq.Owner,
        CardBase cardBase => cardBase.Owner,
        _ => null,
    };
}
