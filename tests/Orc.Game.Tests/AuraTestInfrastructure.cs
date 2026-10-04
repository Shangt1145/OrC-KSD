using Orc.Cards;
using Orc.Game;
using Orc.Game.Board;
using Orc.Game.Cards;

namespace Orc.Game.Tests;

/// <summary>
/// W3-1 G4 光环测试基建：定制定义集（光环源两类）＋效果注册表装配（效果经完整装载链注册声明——
/// 「对局装配 → 卡加载装载 → 声明注册」路径）＋对局构造。
/// 效果工厂与声明示例（体现「相邻光环」「源条件全体」语义——谓词形态由实现者选定）：
/// ①相邻光环兵：「相邻单位 +2 攻」（谓词＝受益者与源相邻——关系判定经环境查询面）；
/// ②战地号令兵：「本单位在前线时 → 源方支援线全体 +1 攻」（谓词＝源在前线 ∧ 受益者位于源方支援线）。
/// </summary>
internal static class AuraTestKit
{
    /// <summary>相邻光环兵（单位；攻 2 / 防 6）。</summary>
    public const string AdjacentAuraUnitId = "u_aura_adj";

    /// <summary>战地号令兵（单位；攻 2 / 防 6）。</summary>
    public const string FrontLineAuraUnitId = "u_aura_front";

    /// <summary>相邻光环效果标识（相邻单位 +2 攻）。</summary>
    public const string AdjacentAuraEffectId = "effect.w31.aura.adjacent_attack";

    /// <summary>源条件全体效果标识（本单位在前线时 → 源方支援线全体 +1 攻）。</summary>
    public const string FrontLineAuraEffectId = "effect.w31.aura.frontline_support";

    /// <summary>定制定义集（2 枚光环源卡；与既有 id 无冲突）。</summary>
    public static IReadOnlyList<CardDefinitionEntry> CreateDefinitions() => new[]
    {
        new CardDefinitionEntry(AdjacentAuraUnitId, new CardDefinition(
            "相邻光环兵", deployCost: 1, operateCost: 1, attack: 2, defense: 6,
            unitTypes: new[] { UnitType.Infantry }, faction: Faction.Germany, rarity: Rarity.Standard)),
        new CardDefinitionEntry(FrontLineAuraUnitId, new CardDefinition(
            "战地号令兵", deployCost: 1, operateCost: 1, attack: 2, defense: 6,
            unitTypes: new[] { UnitType.Infantry }, faction: Faction.Germany, rarity: Rarity.Standard)),
    };

    /// <summary>
    /// 创建光环测试对局：注册两类光环效果（工厂 → 声明）并声明至对应卡 id。
    /// 光环源卡经对局卡库实例化 + LoadAsync 时，效果装载链执行 OnMount → 声明注册（完整装载链路径）。
    /// </summary>
    public static Match CreateAuraMatch()
    {
        var registry = new CardEffectRegistry();

        // 相邻光环：「相邻单位 +2 攻」（关系判定经环境查询面——谓词附加条件；通用门禁由收集步骤内建）
        registry.Register(AdjacentAuraEffectId, _ => new AuraGrantEffect(
            "相邻光环",
            CardStatFields.Attack,
            2,
            host => (environment, beneficiary) => environment.AreAdjacent(host, beneficiary)));

        // 源条件全体：「本单位在前线时 → 源方支援线全体 +1 攻」（「不含源自己」由位置结构自然成立）
        registry.Register(FrontLineAuraEffectId, _ => new AuraGrantEffect(
            "战地号令",
            CardStatFields.Attack,
            1,
            host => (environment, beneficiary) =>
                environment.IsOnFrontLine(host)
                && environment.GetLineOf(beneficiary) is { } line
                && host is CardBase { Owner: { } owner }
                && ReferenceEquals(line, environment.Battlefield.GetSupportLine(owner))));

        registry.Declare(AdjacentAuraUnitId, new[] { AdjacentAuraEffectId });
        registry.Declare(FrontLineAuraUnitId, new[] { FrontLineAuraEffectId });

        return CommandTestKit.CreateCommandMatch(
            effectRegistry: registry, extraDefinitions: CreateDefinitions());
    }
}

/// <summary>
/// 光环授予效果（测试用；被动效果）：装载时（OnMount）经「卡 → 玩家 → 环境」解析环境查询面，
/// 向场级收集面注册「光环声明」（来源＝本效果——整组注销依据）。注销不手写——由效果装载链托管
/// （效果卸载 → RunManagedCleanup → Auras.RemoveBySource(effect)）。无环境可达（独立构造）＝不注册（功能不可用）。
/// </summary>
internal sealed class AuraGrantEffect : PassiveEffect
{
    private readonly string _field;
    private readonly int _delta;
    private readonly Func<Card, Func<GameEnvironment, Card, bool>> _predicateFactory;

    /// <summary>创建光环授予效果。</summary>
    /// <param name="name">效果名。</param>
    /// <param name="field">贡献目标字段。</param>
    /// <param name="delta">加法型增量。</param>
    /// <param name="predicateFactory">受益谓词工厂（以源卡为参数——返回「环境查询面 × 受益卡」判定）。</param>
    public AuraGrantEffect(
        string name, string field, int delta, Func<Card, Func<GameEnvironment, Card, bool>> predicateFactory)
        : base(name)
    {
        _field = field;
        _delta = delta;
        _predicateFactory = predicateFactory;
    }

    /// <inheritdoc />
    protected override void OnMount()
    {
        var environment = GameEnvironment.ResolveFor(Host);
        if (environment is null)
        {
            return; // 无环境面（独立构造/脱局）：不注册（功能不可用、不抛错）
        }

        var host = Host;
        environment.Auras.Register(AuraDeclaration.Add(host, _field, _delta, this, _predicateFactory(host)));
    }

    // OnUnmount 不手写注销：由效果装载链托管（注销收口面）。
}
