using Orc.Cards;
using Orc.Core;
using Orc.Game.Commanding;
using Orc.Game.Players;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 第 2 批·A2 新增词条的组件实现（口径依据《需求文档（A2）》Q&A-1..13）：
// 含行为面六枚——被压制（状态标记＋限期解除）／动员（+1/+1 累积与受伤失去）／钳击（配对与离场失效）／
// 重甲（对战伤害减免；参值 0..3）／情报（触发结构＋待办环节）／免疫（伤害归零）；
// 轻量标记型四枚——被抑制／预报／无法被压制／无法被抑制（PlainKeywordComponent，注册面装配；无行为面）。
// 全部走 A1 组件化体系（同一基类、同一挂载/卸载机制；宿主＝引擎薄容器 Card——单位与 HQ 同族承载）。
// 服务面（施加/读取/待办）见 KeywordServices.cs；钳击关系体系见 PincerSystem.cs。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 被压制（状态标记型＋限期解除；Q&A-5 口径）：施加即时受限（「不能移动或攻击」＝行动合法性消费面读本标记——
/// 指挥可用性/复验侧接线）；标记保留、经移除路径清除。解除＝「施加后首先到来的拥有者回合结束」时
/// （剩余回合数以参值承载——默认 1；「额外压制一回」＝＋1）；「下一个回合」不含当前回合
/// （施加时若处于拥有者回合，本回合结束不递减——组件内 skip 标志、不进参值面）。
/// </summary>
public sealed class SuppressedKeywordComponent : KeywordComponent
{
    private Card? _card;
    private IDisposable? _subscription;
    private bool _skipOwnerTurnEnd;

    /// <summary>创建被压制组件（参值＝剩余拥有者回合数；缺省＝1——施加侧经服务面传入）。</summary>
    public SuppressedKeywordComponent(int? value = null)
        : base(KeywordIds.Suppressed, value)
    {
    }

    /// <inheritdoc />
    internal override void Mount(Card card, KeywordLoadContext? context)
    {
        _card = card;

        // 「下一个回合」不含当前回合：施加时若正处于该单位的拥有者回合，本回合结束不递减（skip 消费一次）
        var current = context?.CurrentPlayerProvider?.Invoke();
        _skipOwnerTurnEnd = current is not null
            && card is CardBase ownerCard
            && ReferenceEquals(ownerCard.Owner, current);

        if (context is not null)
        {
            _subscription = context.Engine.Subscribe(HandleUpdateAsync);
        }
    }

    /// <inheritdoc />
    internal override void Unmount()
    {
        var subscription = _subscription;
        _subscription = null;
        _card = null;
        subscription?.Dispose(); // 幂等（未订阅＝无操作）
    }

    /// <summary>拥有者回合结束驱动：递减剩余回合数；到 0＝到期解除（走标记移除路径）。</summary>
    private Task HandleUpdateAsync(
        string updateType, IReadOnlyDictionary<string, object?>? payload, CancellationToken ct)
    {
        if (_card is not { } card || updateType != GameUpdates.TurnEnd)
        {
            return Task.CompletedTask;
        }

        if (payload?[GameUpdates.PayloadPlayer] is not Player ended
            || card is not CardBase ownerCard
            || !ReferenceEquals(ownerCard.Owner, ended))
        {
            return Task.CompletedTask; // 非该单位拥有者的回合结束：不处理
        }

        if (_skipOwnerTurnEnd)
        {
            _skipOwnerTurnEnd = false; // 当前（拥有者）回合的结束：跳过——尚未开始递减
            return Task.CompletedTask;
        }

        var manager = KeywordRules.TryGetKeywordManager(card);
        if (manager is null)
        {
            return Task.CompletedTask;
        }

        var remaining = Value ?? SuppressRules.SuppressedDefaultTurns;
        var next = remaining - 1;
        if (next <= 0)
        {
            return manager.RevokeAsync(KeywordIds.Suppressed); // 到期：解除（标记移除路径）
        }

        manager.SetValue(KeywordIds.Suppressed, next); // 剩余回合数递减
        return Task.CompletedTask;
    }
}

/// <summary>
/// 动员（能力型；Q&A-6 口径）：友方回合开始（拥有者回合开始相位）时 +1/+1（累积）；
/// 受到实际伤害（净伤害＞0）后失去动员（词条移除链——由受伤害门户调用
/// <see cref="MobilizeRules.OnUnitDamagedAsync"/>；伤害被完全吸收/归零＝不算）。
/// 既得 +1/+1 保留（失去/再获得不清理——修饰器来源＝本组件、无撤销路径）。
/// 仅在场（已单位化、未死亡、有位置）单位获得加成（卡在卡组/手牌/未在场时跳过）。
/// </summary>
public sealed class MobilizeKeywordComponent : KeywordComponent
{
    private Card? _card;
    private IDisposable? _subscription;

    /// <summary>创建动员词条组件。</summary>
    public MobilizeKeywordComponent()
        : base(KeywordIds.Mobilize)
    {
    }

    /// <inheritdoc />
    internal override void Mount(Card card, KeywordLoadContext? context)
    {
        _card = card;
        if (context is not null)
        {
            _subscription = context.Engine.Subscribe(HandleUpdateAsync);
        }
    }

    /// <inheritdoc />
    internal override void Unmount()
    {
        var subscription = _subscription;
        _subscription = null;
        _card = null;
        subscription?.Dispose();
    }

    /// <summary>回合开始驱动：友方回合开始 → +1/+1（累积——来源＝本组件；失去动员不撤销既得）。</summary>
    private async Task HandleUpdateAsync(
        string updateType, IReadOnlyDictionary<string, object?>? payload, CancellationToken ct)
    {
        if (_card is not { } card || updateType != GameUpdates.TurnStart)
        {
            return;
        }

        if (payload?[GameUpdates.PayloadPlayer] is not Player current
            || card is not CardBase ownerCard
            || !ReferenceEquals(ownerCard.Owner, current))
        {
            return; // 非该单位拥有者的回合开始：不处理
        }

        if (card is not UnitCard unit
            || !unit.TryGetData<UnitStateData>(out var state)
            || state.IsDestroyed
            || state.Position is null)
        {
            return; // 未在场（未单位化/已死亡/无位置）：不获得（「单位在其回合开始时获得」）
        }

        await unit.Modifiers.AddModifiersAsync(
            new Modifier[]
            {
                new AddModifier(CardStatFields.Attack, 1, this),
                new AddModifier(CardStatFields.Defense, 1, this),
            },
            ct);
    }
}

/// <summary>
/// 钳击（能力型；Q&A-3/Q&A-12 口径）：部署链收尾（扣费后）发起「同伴选择」（可选、既有单选交互）；
/// 形成一对一配对（候选＝己方在场单位〔不含自身/HQ/已配对〕）；双方获得钳击效果（以「钳击关系」为来源的
/// 成组效果——本批示例＝修饰类 +2 攻击力；通用承载面见 <see cref="PincerPair"/>）；任一方离场
/// （「不再在场」——本批覆盖死亡路径）→ 另一方即时失去（按来源整组撤销）；关系终结不自动恢复。
/// 放弃/无候选/取消＝不形成、部署照常完成（该单位仍具「钳击」词条——计数不受配对影响）；加入路径不形成。
/// </summary>
public sealed class PincerKeywordComponent : KeywordComponent
{
    private UnitCard? _self;
    private KeywordLoadContext? _context;

    /// <summary>创建钳击词条组件。</summary>
    public PincerKeywordComponent()
        : base(KeywordIds.Pincer)
    {
    }

    /// <inheritdoc />
    internal override void Mount(Card card, KeywordLoadContext? context)
    {
        _self = card as UnitCard;
        _context = context;
    }

    /// <inheritdoc />
    internal override void Unmount()
    {
        _self = null;
        _context = null;
    }

    /// <inheritdoc />
    internal override Task OnDeployChainFinalizedAsync(Card card, CancellationToken ct)
    {
        if (_self is not { } self || _context is not { } context)
        {
            return Task.CompletedTask; // 非单位/无装载上下文：跳过（防御、不抛错）
        }

        return PincerRules.TryFormPairAsync(self, context, ct);
    }
}

/// <summary>
/// 重甲（参值型；Q&A-7 口径）：受到的对战伤害 -X（攻击伤害与反击伤害均含；指令伤害不减免）。
/// 参值域：下限 0、上限 3（「每张卡最多为 3」＝值封顶——构造/参值改写均在组件侧钳制）。
/// 减伤经「造成攻击伤害」handler 链介入（默认结算 handler 之前登记减伤量；结果下限 0 自然收敛）——
/// 与免疫同族、不新增第二套伤害流程。判定读点（承载面）：①攻击者侧「无视重甲」（默认不无视——
/// 读点经 <see cref="ArmorRules.IgnoresArmor"/>）；②指令伤害域（<see cref="AppliesToCommandDamage"/>——默认关闭，
/// 指令伤害路径接入随该路径批次）。「+1 重甲」＝当前值 +1（经 <see cref="ArmorRules.AddArmorAsync"/>）。
/// </summary>
public sealed class ArmorKeywordComponent : KeywordComponent
{
    /// <summary>参值下限（负值钳制为 0）。</summary>
    public const int MinValue = 0;

    /// <summary>参值上限（值封顶 3——「重甲叠加数」即重甲值、非「叠加次数」）。</summary>
    public const int MaxValue = 3;

    private Card? _card;
    private Trigger<AttackDamageTriggerView>? _trigger;
    private TriggerRegistration? _registration;

    /// <summary>创建重甲词条组件（参值在组件侧钳制至 [0, 3]；null＝「仅标识」形态保留）。</summary>
    public ArmorKeywordComponent(int? value = null)
        : base(KeywordIds.Armor, Clamp(value))
    {
    }

    /// <summary>是否作用于指令伤害（域扩展开关；默认 false＝仅对战伤害）——内容层效果将来经设置开启（本批承载面就位）。</summary>
    public bool AppliesToCommandDamage { get; set; }

    /// <summary>参值钳制（写入/增改均受上限约束——词条层校验/封顶）。</summary>
    private static int? Clamp(int? value) => value is { } v ? Math.Clamp(v, MinValue, MaxValue) : null;

    /// <inheritdoc />
    internal override void OverrideValue(int? value) => base.OverrideValue(Clamp(value));

    /// <inheritdoc />
    internal override void Mount(Card card, KeywordLoadContext? context)
    {
        _card = card;
        if (context is null)
        {
            return; // 无装载上下文（独立构造场景）：不注册（防御、不抛错）
        }

        _trigger = context.AttackDamageTrigger;
        _registration = _trigger.Register("重甲减伤", HandleAttackDamageAsync);
    }

    /// <inheritdoc />
    internal override void Unmount()
    {
        var trigger = _trigger;
        var registration = _registration;
        _trigger = null;
        _registration = null;
        _card = null;
        if (trigger is not null && registration is not null)
        {
            trigger.Unregister(registration); // 幂等（重复注销＝无操作、不抛错）
        }
    }

    /// <summary>重甲减伤判定（「造成攻击伤害」内、默认结算之前执行——登记减伤量，默认结算读取）。</summary>
    private Task HandleAttackDamageAsync(AttackDamageTriggerView view, Context ctx, CancellationToken ct)
    {
        if (_card is null || Value is not { } armor || armor <= 0)
        {
            return Task.CompletedTask; // 无参值/值 0：无减伤效果（语义等效「重甲 0」）
        }

        if (view.Resolution is not AttackDamageResolution resolution)
        {
            return Task.CompletedTask;
        }

        // 目标方向（本卡被攻击——攻击伤害减免；判定读点：攻击者侧「无视重甲」——默认不无视）
        var attackerIgnoresArmor = view.Attacker?.Value is UnitCard attacker && ArmorRules.IgnoresArmor(attacker);
        if (!attackerIgnoresArmor
            && view.Target is { IsAlive: true } targetRef
            && ReferenceEquals(targetRef.Value, _card))
        {
            resolution.AddDamageReduction(_card, armor);
        }

        // 攻击者方向（本卡受反击——反击伤害同属对战伤害、同减）
        if (view.Attacker is { IsAlive: true } attackerRef && ReferenceEquals(attackerRef.Value, _card))
        {
            resolution.AddDamageReduction(_card, armor);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 情报（参值型；Q&A-8 口径）：落地＝骨架（声明/装配/读取）＋触发结构——「具有情报的卡被使用时」触发
/// （card.played 时点接线；推断口径、官方规则文本未直述）；「情报执行」（明牌 X 张）因 G18 明牌系统未就绪
/// ＝待办环节（<see cref="IntelligenceRules"/>——触发点调用明确待办、申报待接线）。
/// 参值域：下限 0、上限 3（词条层钳制——「每张卡最多为 3」＝值封顶）。
/// </summary>
public sealed class IntelligenceKeywordComponent : KeywordComponent
{
    /// <summary>参值下限（负值钳制为 0）。</summary>
    public const int MinValue = 0;

    /// <summary>参值上限（值封顶 3）。</summary>
    public const int MaxValue = 3;

    private Card? _card;
    private LogicEngine? _engine;
    private IDisposable? _subscription;

    /// <summary>创建情报词条组件（参值在组件侧钳制至 [0, 3]；null＝「仅标识」形态保留）。</summary>
    public IntelligenceKeywordComponent(int? value = null)
        : base(KeywordIds.Intelligence, Clamp(value))
    {
    }

    /// <summary>参值钳制（写入/增改均受上限约束——词条层校验/封顶）。</summary>
    private static int? Clamp(int? value) => value is { } v ? Math.Clamp(v, MinValue, MaxValue) : null;

    /// <inheritdoc />
    internal override void OverrideValue(int? value) => base.OverrideValue(Clamp(value));

    /// <inheritdoc />
    internal override void Mount(Card card, KeywordLoadContext? context)
    {
        _card = card;
        _engine = context?.Engine;
        if (context is not null)
        {
            _subscription = context.Engine.Subscribe(HandleUpdateAsync);
        }
    }

    /// <inheritdoc />
    internal override void Unmount()
    {
        var subscription = _subscription;
        _subscription = null;
        _card = null;
        _engine = null;
        subscription?.Dispose();
    }

    /// <summary>「被使用」驱动（card.played）：触发情报执行待办环节（G18 就绪后替换为实际「明牌 X 张」执行）。</summary>
    private Task HandleUpdateAsync(
        string updateType, IReadOnlyDictionary<string, object?>? payload, CancellationToken ct)
    {
        if (_card is not { } card || updateType != GameUpdates.CardPlayed)
        {
            return Task.CompletedTask;
        }

        if (payload?[GameUpdates.PayloadCard] is not Card played || !ReferenceEquals(played, card))
        {
            return Task.CompletedTask; // 非本卡被使用：不处理
        }

        return IntelligenceRules.InvokePendingRevealAsync(card, Value ?? 0, _engine, ct);
    }
}

/// <summary>
/// 免疫（能力型；Q&A-9 口径）：不会受到伤害——伤害归零（不限制索敌：目标候选/校验不查免疫；非伤害效果照常）。
/// 单位侧＝「造成攻击伤害」handler 注入（默认结算 handler 之前把对本卡的伤害设为 0——攻击伤害与反击伤害两方向）；
/// HQ 侧＝HQ 伤害路径改写段挂「归零改写器」（机制同一、接入点各按路径对齐）。归零后按「0 伤害」正常走
/// （净伤害＝0 不算「受到伤害」——动员等消费自然一致）。「全部来源」：本批伤害通道＝对战结算（单位）与
/// HQ 数值路径（HQ）；非对战路径（指令/效果伤害）接入随统一伤害路径批次对齐（申报）。期限/清除走施加物机制。
/// </summary>
public sealed class ImmuneKeywordComponent : KeywordComponent
{
    private Card? _card;
    private Trigger<AttackDamageTriggerView>? _trigger;
    private TriggerRegistration? _registration;
    private HqDamageRewriter? _hqRewriter;

    /// <summary>创建免疫词条组件。</summary>
    public ImmuneKeywordComponent()
        : base(KeywordIds.Immune)
    {
    }

    /// <inheritdoc />
    internal override void Mount(Card card, KeywordLoadContext? context)
    {
        _card = card;
        if (card is Hq hq)
        {
            // HQ 伤害路径（改写段）：伤害性扣减归零（「机制同一、接入点各按路径对齐」）
            _hqRewriter = new HqDamageRewriter(this, _ => 0);
            hq.AddDamageRewriter(_hqRewriter);
            return;
        }

        if (context is null)
        {
            return; // 无装载上下文（独立构造场景）：不注册（防御、不抛错）
        }

        _trigger = context.AttackDamageTrigger;
        _registration = _trigger.Register("免疫归零", HandleAttackDamageAsync);
    }

    /// <inheritdoc />
    internal override void Unmount()
    {
        var trigger = _trigger;
        var registration = _registration;
        _trigger = null;
        _registration = null;
        if (trigger is not null && registration is not null)
        {
            trigger.Unregister(registration);
        }

        if (_card is Hq hq && _hqRewriter is not null)
        {
            hq.RemoveDamageRewriter(_hqRewriter); // 幂等（未挂载＝无操作）
        }

        _hqRewriter = null;
        _card = null;
    }

    /// <summary>免疫归零（「造成攻击伤害」内、默认结算之前执行——把对本卡的伤害设为 0）。</summary>
    private Task HandleAttackDamageAsync(AttackDamageTriggerView view, Context ctx, CancellationToken ct)
    {
        if (_card is not { } self || view.Resolution is not AttackDamageResolution resolution)
        {
            return Task.CompletedTask;
        }

        if (view.Target is { IsAlive: true } targetRef && ReferenceEquals(targetRef.Value, self))
        {
            resolution.MarkDamageZeroed(self); // 目标方向：本卡（免疫）不受伤
        }

        if (view.Attacker is { IsAlive: true } attackerRef && ReferenceEquals(attackerRef.Value, self))
        {
            resolution.MarkDamageZeroed(self); // 攻击者方向（反击）：本卡（免疫）亦不受伤
        }

        return Task.CompletedTask;
    }
}
