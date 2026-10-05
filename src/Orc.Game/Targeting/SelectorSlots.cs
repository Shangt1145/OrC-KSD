using Orc.Game.Cards;

namespace Orc.Game.Targeting;

/// <summary>
/// 选择器槽位产出（槽位声明＋请求级绑定；S1 三类选择器槽位）：
/// <see cref="Slots"/> 交 <see cref="TargeterManager.CreateTargeter"/> 的 slots 参数；
/// <see cref="Context"/> 交其 context 参数（承载槽位参数等请求级绑定）。
/// </summary>
public sealed class SelectorSlotSet
{
    internal SelectorSlotSet(IReadOnlyList<TargetSlot> slots, TargetingRequestContext context)
    {
        Slots = slots;
        Context = context;
    }

    /// <summary>槽位声明（声明序只读快照；交 CreateTargeter 的 slots 参数）。</summary>
    public IReadOnlyList<TargetSlot> Slots { get; }

    /// <summary>请求级绑定（槽位参数；交 CreateTargeter 的 context 参数）。</summary>
    public TargetingRequestContext Context { get; }
}

/// <summary>
/// 三类选择器槽位工厂（S1；按"业务场景"归类——对齐"三类选择器槽位"）：
/// ①手牌起始指向（手牌打出时；参数＝起始卡牌实例）；②放置后指向（放置/部署后的指向；参数＝新入场单位）；
/// ③场上单位指向（在场单位指挥；参数＝被拖动单位）。
/// 形态＝既有 <see cref="SingleSelectSlot"/> ＋ 请求级槽位参数（G5：不新增 Kind；G2：参数经请求级绑定随描述交付前端）；
/// 候选域（允许集／域判定／筛选）由各调用路径按对局状态填充——本工厂只产出槽位形态与参数。
/// 呈现约定：槽位按声明序呈现、一次 Begin、一次 Complete 按槽位名回填（G1：不改桥接契约）。
/// </summary>
public static class SelectorSlots
{
    /// <summary>手牌起始指向槽位名（参数＝起始卡牌；手牌打出时）。</summary>
    public const string HandOrigin = "handOrigin";

    /// <summary>放置后指向槽位名（参数＝新入场单位；放置/部署后的指向）。</summary>
    public const string AfterPlacement = "afterPlacement";

    /// <summary>场上单位指向槽位名（参数＝被拖动单位；在场单位指挥）。</summary>
    public const string FieldUnit = "fieldUnit";

    /// <summary>手牌起始指向（槽位＝单选；槽位参数＝起始卡牌实例）。</summary>
    /// <param name="startCard">起始卡牌（手牌实例）。</param>
    /// <exception cref="ArgumentNullException">startCard 为 null。</exception>
    public static SelectorSlotSet ForHandOrigin(CardBase startCard)
    {
        ArgumentNullException.ThrowIfNull(startCard);
        return Build(HandOrigin, startCard);
    }

    /// <summary>放置后指向（槽位＝单选；槽位参数＝新入场单位）。</summary>
    /// <param name="placedUnit">新入场单位。</param>
    /// <exception cref="ArgumentNullException">placedUnit 为 null。</exception>
    public static SelectorSlotSet ForAfterPlacement(UnitCard placedUnit)
    {
        ArgumentNullException.ThrowIfNull(placedUnit);
        return Build(AfterPlacement, placedUnit);
    }

    /// <summary>场上单位指向（槽位＝单选；槽位参数＝被拖动单位）。</summary>
    /// <param name="draggedUnit">被拖动单位。</param>
    /// <exception cref="ArgumentNullException">draggedUnit 为 null。</exception>
    public static SelectorSlotSet ForFieldUnit(UnitCard draggedUnit)
    {
        ArgumentNullException.ThrowIfNull(draggedUnit);
        return Build(FieldUnit, draggedUnit);
    }

    private static SelectorSlotSet Build(string slotName, object parameter)
        => new(
            new TargetSlot[] { new SingleSelectSlot(slotName) },
            new TargetingRequestContext().WithSlotParameter(slotName, parameter));
}
