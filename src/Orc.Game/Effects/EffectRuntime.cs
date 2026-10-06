using Orc.Cards;
using Orc.Core;
using Orc.Game.Board;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Judicators;
using Orc.Game.Managers;
using Orc.Game.Players;

namespace Orc.Game.Effects;

/// <summary>无头选靶描述（效果运行期）：sel/side/zone/unitType/keyword/count 的最小集。</summary>
/// <param name="Sel">选靶方式（all/one/any/random/self——<c>self</c>＝视角卡自身，E1-41）。</param>
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
    int? Count = null,
    EffectThreshold? Threshold = null);

/// <summary>
/// **目标阈值过滤**（E1-57）：目标卡的属性须满足 字段 算子 取值（如 花费不大干 3 的单位）。
/// </summary>
/// <param name="Field">字段（ttack／defense／opCost／deployCost）。</param>
/// <param name="Op">算子（gte／lte／gt／lt／eq）。</param>
/// <param name="Value">取值。</param>
public sealed record EffectThreshold(string Field, string Op, int Value);

/// <summary>
/// **光环受益谓词描述**（E1-56）：由游戏层据此构造 <see cref="Orc.Game.Cards.AuraDeclaration"/> 的受益谓词
/// （csx 侧只描述"谁受益"，谓词本体不落到脚本）。
/// </summary>
/// <param name="Side">阵营面（friendly/enemy/both；null＝不限）。</param>
/// <param name="Faction">阵营（<see cref="Cards.Faction"/> 枚举名；null＝不限）。</param>
/// <param name="UnitType">兵种（<see cref="Cards.UnitType"/> 枚举名；null＝不限）。</param>
/// <param name="Keyword">词条标识（null＝不限）。</param>
/// <param name="ExcludeSelf">排除宿主自身（`其他/其它…`）。</param>
/// <param name="Zone">区域（frontline/support；null＝不限）。</param>
public sealed record EffectAuraFilter(
    string? Side = null,
    string? Faction = null,
    string? UnitType = null,
    string? Keyword = null,
    bool ExcludeSelf = false,
    string? Zone = null);

/// <summary>
/// 修饰期限（E1-41；csx 侧经 <c>buff</c>/<c>costMod</c> 的 <c>until</c> 参数传入）：
/// 持续态修饰器的**到期相位**——<see cref="Permanent"/>＝随效果存续（无期限）。
/// </summary>
public enum EffectDuration
{
    /// <summary>无期限（修饰器随效果存续／随效果卸载撤销）。</summary>
    Permanent = 0,

    /// <summary>本回合结束（`直到回合结束`）。</summary>
    TurnEnd = 1,

    /// <summary>下个己方回合开始（`直到下个友方回合开始`）。</summary>
    NextOwnerTurnStart = 2,
}

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
    /// <param name="source">施动方（E1-50；提供时作为 <c>card.died</c> 的 <c>Killer</c> 归属——"本单位消灭"可命中）。</param>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> KillAsync(Card target, Card? source = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_commands() is not { } commands || target is not UnitCard unit)
        {
            return false;
        }

        await commands.KillUnitAsync(unit, source as UnitCard, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>造成伤害（游戏语义：单位扣防御、总部扣血）。</summary>
    /// <param name="source">施动方（E1-50；提供时归属：发 <c>unit.damage.dealt</c>、致死时作 <c>Killer</c>）。</param>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> DamageAsync(Card target, int amount, Card? source = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var dealer = source as UnitCard;
        switch (target)
        {
            case UnitCard unit when dealer is not null && _commands() is { } commands:
                // E1-50：有来源 ⇒ 走指挥管理器的受控面（**伤害来源游标** ＋ unit.damage.dealt 归属）
                await commands.DealDamageAsync(unit, amount, dealer, ct).ConfigureAwait(false);
                return true;
            case UnitCard unit:
                await unit.ApplyDefenseDamageAsync(amount, ct).ConfigureAwait(false);
                return true;
            case Hq hq when dealer is not null && _commands() is { } commands:
                await commands.DealDamageToHqAsync(hq, amount, dealer, ct).ConfigureAwait(false);
                return true;
            case Hq hq:
                await hq.ApplyDamageAsync(amount, ct).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>属性修饰（攻击力/防御力增减；来源＝本门面实例——撤销请经 <c>RemoveBySourceAsync</c>）。</summary>
    /// <param name="duration">期限（E1-41；<see cref="EffectDuration.Permanent"/>＝随效果存续）。</param>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> BuffAsync(
        Card target, int attack, int defense,
        EffectDuration duration = EffectDuration.Permanent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not CardBase cardBase)
        {
            return false;
        }

        var expiry = ExpiryFor(duration, target);
        var modifiers = new List<Modifier>();
        if (attack != 0)
        {
            modifiers.Add(new AddModifier(CardStatFields.Attack, attack, this, expiry));
        }

        if (defense != 0)
        {
            modifiers.Add(new AddModifier(CardStatFields.Defense, defense, this, expiry));
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
    /// <param name="duration">期限（E1-41；<see cref="EffectDuration.Permanent"/>＝随效果存续）。</param>
    /// <exception cref="ArgumentNullException">target 为 null。</exception>
    public async Task<bool> CostModAsync(
        Card target, int delta,
        EffectDuration duration = EffectDuration.Permanent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is not CardBase cardBase || delta == 0)
        {
            return false;
        }

        await cardBase.Modifiers
            .AddModifierAsync(
                new AddModifier(CardStatFields.OperateCost, delta, this, ExpiryFor(duration, target)), ct)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 期限 → 修饰器期限声明（E1-41）：<see cref="EffectDuration.TurnEnd"/>＝本回合结束相位（无需过滤——
    /// 挂载后遇到的第一个 <c>turn.end</c> 即"本回合结束"）；
    /// <see cref="EffectDuration.NextOwnerTurnStart"/>＝<c>turn.start</c> 相位 **且载荷玩家＝目标卡归属玩家**。
    /// </summary>
    private static ModifierExpiry? ExpiryFor(EffectDuration duration, Card target) => duration switch
    {
        EffectDuration.TurnEnd => new ModifierExpiry(GameUpdates.TurnEnd),
        EffectDuration.NextOwnerTurnStart => new ModifierExpiry(
            GameUpdates.TurnStart,
            payload => payload is not null
                && payload.TryGetValue(GameUpdates.PayloadPlayer, out var player)
                && ReferenceEquals(player, OwnerOf(target))),
        _ => null,
    };

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

    /// <summary>
    /// **声明光环**（E1-56；持续态的正确机制）：把"某组卡**持续**获得 ±N 字段"登记为场级光环声明——
    /// 受益谓词由本门面按 <see cref="EffectAuraFilter"/> 构造（**不进 csx**），来源＝本门面实例
    /// （随效果卸载整组撤销）；登记后经 <c>RerunAllCardsAsync</c> 衔接一轮使收益现算生效。
    /// <para>与 <see cref="BuffAsync"/> 的分工：**静态/持续**文本（无触发）⇒ 光环（受益随进出/位置**实时重算**）；
    /// **触发体内**一次性动作 ⇒ 修饰器（尤其带期限）。</para>
    /// </summary>
    /// <param name="host">光环宿主（源卡；其「在场」为通用门禁）。</param>
    /// <param name="field">目标字段（`attack`／`defense`／`opCost`／`deployCost`）。</param>
    /// <param name="delta">增量（可负）。</param>
    /// <param name="filter">受益谓词描述。</param>
    /// <exception cref="ArgumentNullException">host 或 filter 为 null。</exception>
    public async Task<bool> DeclareAuraAsync(
        Card host, string field, int delta, EffectAuraFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(filter);
        if (delta == 0
            || StatFieldOf(field) is not { } statField
            || GameEnvironment.ResolveFor(host) is not { } environment)
        {
            return false;
        }

        environment.Auras.Register(AuraDeclaration.Add(
            host, statField, delta, this, (env, beneficiary) => MatchesAura(env, host, beneficiary, filter)));
        await environment.RerunAllCardsAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// **数值比较条件求值**（E1-57；**纯函数**——可安全参与 `&amp;&amp;` 合取，无需 `await`）。
    /// <para><paramref name="spec"/> 形如 `左度量:算子:右操作数`：度量＝`count=s=&lt;side&gt;`（单位数）／
    /// `points=s=friendly`（剩余指挥点数）／`stat=f=&lt;field&gt;;s=friendly;z=hq`（总部属性）；
    /// 算子＝`gte`／`lte`／`gt`／`lt`／`eq`；右操作数＝`#&lt;整数&gt;` 或**同类度量**（`count=s=enemy`）。</para>
    /// <para>不合法/不可解析/视角缺归属 ⇒ **false**（不抛错——与"占位条件"同观感但**不静默执行**）。</para>
    /// </summary>
    public static bool EvaluateCondition(Card? viewer, string spec)
    {
        if (viewer is null || string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var parts = spec.Split(':');
        if (parts.Length != 3
            || GameEnvironment.ResolveFor(viewer) is not { } environment
            || Measure(environment, viewer, parts[0]) is not { } left
            || RightOperand(environment, viewer, parts[2]) is not { } right)
        {
            return false;
        }

        return parts[1] switch
        {
            "gte" => left >= right,
            "lte" => left <= right,
            "gt" => left > right,
            "lt" => left < right,
            "eq" => left == right,
            _ => false,
        };
    }

    /// <summary>右操作数（`#n` 或度量引用）。</summary>
    private static int? RightOperand(GameEnvironment environment, Card viewer, string spec) =>
        spec.StartsWith('#') && int.TryParse(spec[1..], out var literal)
            ? literal
            : Measure(environment, viewer, spec);

    /// <summary>度量求值（`count:`／`points:`／`stat:`；未知＝null）。</summary>
    private static int? Measure(GameEnvironment environment, Card viewer, string spec)
    {
        var owner = OwnerOf(viewer);
        if (owner is null)
        {
            return null;
        }

        if (spec.StartsWith("count=", StringComparison.Ordinal))
        {
            var side = Attribute(spec, "s");
            var total = 0;
            foreach (var unit in EnumerateUnits(environment))
            {
                var unitOwner = OwnerOf(unit);
                var matches = side switch
                {
                    "enemy" => unitOwner is not null && !ReferenceEquals(unitOwner, owner),
                    "both" or null => true,
                    _ => ReferenceEquals(unitOwner, owner),
                };
                if (matches)
                {
                    total++;
                }
            }

            return total;
        }

        if (spec.StartsWith("points=", StringComparison.Ordinal))
        {
            // 视角玩家的**剩余指挥点数**（`s=enemy` 需对手对象——本轮不接，返回 null ⇒ 条件为假）。
            return Attribute(spec, "s") is "enemy" ? null : owner.Points;
        }

        if (spec.StartsWith("stat=", StringComparison.Ordinal))
        {
            // 仅支持"己方总部属性"（`z=hq`）——`敌方总部` 需对手对象（登记为待接）。
            if (Attribute(spec, "s") is "enemy" || Attribute(spec, "z") != "hq")
            {
                return null;
            }

            // `总部防御力`＝**HQ 生命值**（总部无防御修饰字段）；其余字段未接 ⇒ null。
            return Attribute(spec, "f") is "defense" ? owner.Hq.Health : null;
        }

        return null;
    }

    /// <summary>`key=value;key=value` 规范串取值。</summary>
    private static string? Attribute(string spec, string key)
    {
        var body = spec[(spec.IndexOf('=', StringComparison.Ordinal) + 1)..];
        foreach (var pair in body.Split(';'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && string.Equals(pair[..separator], key, StringComparison.Ordinal))
            {
                return pair[(separator + 1)..];
            }
        }

        return null;
    }

    /// <summary>场上单位枚举（双方支援线 ＋ 前线）。</summary>
    private static IEnumerable<UnitCard> EnumerateUnits(GameEnvironment environment)
    {
        var battlefield = environment.Battlefield;
        foreach (var line in new[] { battlefield.PlayerASupportLine, battlefield.PlayerBSupportLine, battlefield.FrontLine })
        {
            foreach (var slot in line)
            {
                if (slot.Occupant is UnitCard unit)
                {
                    yield return unit;
                }
            }
        }
    }

    /// <summary>DSL 字段名 → 机制字段标识（白名单；未知＝null）。</summary>
    private static string? StatFieldOf(string field) => field switch
    {
        "attack" => CardStatFields.Attack,
        "defense" => CardStatFields.Defense,
        "opCost" => CardStatFields.OperateCost,
        "deployCost" => CardStatFields.DeployCost,
        _ => null,
    };

    /// <summary>
    /// 光环受益谓词（E1-56）：按 <see cref="EffectAuraFilter"/> 逐项判定
    /// （自身排除 → 阵营面 → 阵营 → 兵种 → 词条 → 相邻 → 区域）。
    /// </summary>
    private static bool MatchesAura(
        GameEnvironment environment, Card host, Card beneficiary, EffectAuraFilter filter)
    {
        if (filter.ExcludeSelf && ReferenceEquals(host, beneficiary))
        {
            return false;
        }

        if (!MatchesAuraSide(host, beneficiary, filter.Side))
        {
            return false;
        }

        if (filter.Faction is { } faction
            && (beneficiary is not CardBase factionCard
                || !Enum.TryParse<Faction>(faction, ignoreCase: true, out var parsedFaction)
                || factionCard.Definition.Faction != parsedFaction))
        {
            return false;
        }

        if (filter.UnitType is { } unitType
            && (beneficiary is not UnitCard unit
                || !Enum.TryParse<UnitType>(unitType, ignoreCase: true, out var parsedType)
                || !unit.GetData<UnitStateData>().UnitTypes.Contains(parsedType)))
        {
            return false;
        }

        if (filter.Keyword is { } keyword
            && (beneficiary is not CardBase keywordCard || !keywordCard.Keywords.Has(keyword)))
        {
            return false;
        }

        return filter.Zone switch
        {
            "frontline" => environment.IsOnFrontLine(beneficiary),
            "support" => environment.GetLineOf(beneficiary) is not null && !environment.IsOnFrontLine(beneficiary),
            _ => true,
        };
    }

    /// <summary>光环的**阵营面**判定（friendly/enemy/both）。</summary>
    private static bool MatchesAuraSide(Card host, Card beneficiary, string? side)
    {
        if (side is null or "both" or "any")
        {
            return true;
        }

        var hostOwner = OwnerOf(host);
        var beneficiaryOwner = OwnerOf(beneficiary);
        if (hostOwner is null)
        {
            return false;
        }

        return side switch
        {
            "friendly" => ReferenceEquals(hostOwner, beneficiaryOwner),
            "enemy" => beneficiaryOwner is not null && !ReferenceEquals(hostOwner, beneficiaryOwner),
            _ => true,
        };
    }

    private static Player? OwnerOf(Card card) => card switch
    {
        Hq hq => hq.Owner,
        CardBase cardBase => cardBase.Owner,
        _ => null,
    };
}
