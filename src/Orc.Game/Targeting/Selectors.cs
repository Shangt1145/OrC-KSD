namespace Orc.Game.Targeting;

/// <summary>专门选择器名（稳定标识；前端据以选择视觉实现）。并入现役选择器雏形（Q10）。</summary>
public static class SelectorNames
{
    public const string FieldUnit = "fieldUnit";
    public const string HandOrigin = "handOrigin";
    public const string UnitHandDrag = "unitHandDrag";
    public const string AfterPlacement = "afterPlacement";
    public const string Slot = "slot";
    public const string TargetPoint = "targetPoint";
    public const string Hand = "hand";
    public const string Mulligan = "mulligan";
    public const string Option = "option";
    public const string CardPicker = "cardPicker";
    public const string CardPickerReference = "cardPickerReference";
}

public sealed class HandOriginSelector : SingleReferenceSelector
{
    public HandOriginSelector() : base(SelectorNames.HandOrigin, SelectorInteractionMode.Click) { }
}

public sealed class UnitHandDragSelector : SingleReferenceSelector
{
    public UnitHandDragSelector() : base(SelectorNames.UnitHandDrag, SelectorInteractionMode.Drag) { }
}

public sealed class AfterPlacementSelector : SingleReferenceSelector
{
    public AfterPlacementSelector() : base(SelectorNames.AfterPlacement, SelectorInteractionMode.Click) { }
}

public sealed class FieldUnitSelector : SingleReferenceSelector
{
    public FieldUnitSelector() : base(SelectorNames.FieldUnit, SelectorInteractionMode.Drag) { }
}

public sealed class SlotSelector : SingleReferenceSelector
{
    public SlotSelector() : base(SelectorNames.Slot, SelectorInteractionMode.Click) { }
}

public sealed class TargetPointSelector : SingleReferenceSelector
{
    public TargetPointSelector() : base(SelectorNames.TargetPoint, SelectorInteractionMode.Click) { }
}

public sealed class HandSelector : MultiReferenceSelector
{
    public HandSelector() : base(SelectorNames.Hand, SelectorInteractionMode.Click) { }
}

public sealed class MulliganSelector : MultiReferenceSelector
{
    public MulliganSelector() : base(SelectorNames.Mulligan, SelectorInteractionMode.Click) { }
}

public sealed class OptionSelector : SingleIdentifierSelector
{
    public OptionSelector() : base(SelectorNames.Option, SelectorInteractionMode.Click) { }
}

public sealed class CardPickerListSelector : MultiIdentifierSelector
{
    public CardPickerListSelector() : base(SelectorNames.CardPicker, SelectorInteractionMode.Click) { }
}

public sealed class CardPickerSetSelector : MultiReferenceSelector
{
    public CardPickerSetSelector() : base(SelectorNames.CardPickerReference, SelectorInteractionMode.Click) { }
}

/// <summary>选择器模板（预构建定义实例；进程内共用）。</summary>
public static class SelectorTemplates
{
    public static FieldUnitSelector FieldUnit { get; } = new();
    public static HandOriginSelector HandOrigin { get; } = new();
    public static UnitHandDragSelector UnitHandDrag { get; } = new();
    public static AfterPlacementSelector AfterPlacement { get; } = new();
    public static SlotSelector Slot { get; } = new();
    public static TargetPointSelector TargetPoint { get; } = new();
    public static HandSelector Hand { get; } = new();
    public static MulliganSelector Mulligan { get; } = new();
    public static OptionSelector Option { get; } = new();
    public static CardPickerListSelector CardPickerList { get; } = new();
    public static CardPickerSetSelector CardPickerSet { get; } = new();
}