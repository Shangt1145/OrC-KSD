using Orc.Core;
using Orc.Game.Targeting;

namespace Orc.Game.Tests;

// ===========================================================================
// 旧契约仿真层（**仅测试侧**）：把旧形状（槽位声明 + 两级筛选 + 候选收集 + 产出读面）
// 映射到新契约（RunAsync / Step / 语义事件），使既有集成测试少改、可编译。
// 新契约测试请直接用 RunAsync/Step（见 TargeterFlowTests）。
// ===========================================================================

/// <summary>槽位种类（仿真）。</summary>
internal enum TargetSlotKind { SingleSelect, MultiSelect, OptionSelect, HandSelect, CardPicker, MulliganSelect }

/// <summary>呈现形态（仿真）。</summary>
internal enum TargetSlotPresentation { TargetPoints, OptionList, HandSelect, CardArray, MulliganSelect }

/// <summary>卡牌选择器形态（仿真）。</summary>
internal enum CardPickerForm { Listing, ReferenceSet }

/// <summary>选项条目（仿真）。</summary>
internal sealed class OptionEntry
{
    public OptionEntry(string id, string text)
    {
        Id = id;
        Text = text;
    }

    public string Id { get; }

    public string Text { get; }
}

/// <summary>卡牌名单条目（仿真）。</summary>
internal sealed class CardListing
{
    public CardListing(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }
}

/// <summary>目标筛选器（仿真；两级）。</summary>
internal sealed class TargetFilter
{
    public TargetFilter(
        Func<IReadOnlyList<Ref<Entity>>, IReadOnlyList<Ref<Entity>>>? coarseFilter = null,
        Func<Ref<Entity>, bool>? fineFilter = null)
    {
        CoarseFilter = coarseFilter;
        FineFilter = fineFilter;
    }

    public Func<IReadOnlyList<Ref<Entity>>, IReadOnlyList<Ref<Entity>>>? CoarseFilter { get; }

    public Func<Ref<Entity>, bool>? FineFilter { get; }
}

/// <summary>选择槽位（仿真基类）。</summary>
internal abstract class TargetSlot
{
    public const string DefaultName = "default";

    protected TargetSlot(string? name) => Name = string.IsNullOrWhiteSpace(name) ? DefaultName : name;

    public string Name { get; }

    public abstract TargetSlotKind Kind { get; }

    public abstract int MinSelection { get; }

    public abstract int MaxSelection { get; }

    public abstract TargetSlotPresentation Presentation { get; }
}

/// <summary>单选槽位（仿真）。</summary>
internal sealed class SingleSelectSlot : TargetSlot
{
    public SingleSelectSlot(string? name = null) : base(name) { }

    public override TargetSlotKind Kind => TargetSlotKind.SingleSelect;

    public override int MinSelection => 1;

    public override int MaxSelection => 1;

    public override TargetSlotPresentation Presentation => TargetSlotPresentation.TargetPoints;
}

/// <summary>多选槽位（仿真）。</summary>
internal sealed class MultiSelectSlot : TargetSlot
{
    public MultiSelectSlot(int min, int max, string? name = null) : base(name)
    {
        Min = min;
        Max = max;
    }

    public int Min { get; }

    public int Max { get; }

    public override TargetSlotKind Kind => TargetSlotKind.MultiSelect;

    public override int MinSelection => Min;

    public override int MaxSelection => Max;

    public override TargetSlotPresentation Presentation => TargetSlotPresentation.TargetPoints;
}

/// <summary>手牌选择槽位（仿真）。</summary>
internal sealed class HandSelectSlot : TargetSlot
{
    public HandSelectSlot(int min, int max, string? name = null) : base(name)
    {
        Min = min;
        Max = max;
    }

    public int Min { get; }

    public int Max { get; }

    public override TargetSlotKind Kind => TargetSlotKind.HandSelect;

    public override int MinSelection => Min;

    public override int MaxSelection => Max;

    public override TargetSlotPresentation Presentation => TargetSlotPresentation.HandSelect;
}

/// <summary>换牌选择槽位（仿真）。</summary>
internal sealed class MulliganSelectSlot : TargetSlot
{
    public MulliganSelectSlot(int min, int max, string? name = null) : base(name)
    {
        Min = min;
        Max = max;
    }

    public int Min { get; }

    public int Max { get; }

    public override TargetSlotKind Kind => TargetSlotKind.MulliganSelect;

    public override int MinSelection => Min;

    public override int MaxSelection => Max;

    public override TargetSlotPresentation Presentation => TargetSlotPresentation.MulliganSelect;
}

/// <summary>选项槽位（仿真）。</summary>
internal sealed class OptionSelectSlot : TargetSlot
{
    public OptionSelectSlot(IEnumerable<OptionEntry> options, string? name = null) : base(name)
        => Options = options.ToArray();

    public IReadOnlyList<OptionEntry> Options { get; }

    public override TargetSlotKind Kind => TargetSlotKind.OptionSelect;

    public override int MinSelection => 1;

    public override int MaxSelection => 1;

    public override TargetSlotPresentation Presentation => TargetSlotPresentation.OptionList;
}

/// <summary>卡牌选择器槽位（仿真）。</summary>
internal sealed class CardPickerSlot : TargetSlot
{
    public CardPickerSlot(CardPickerForm form, int min, int max, string? name = null) : base(name)
    {
        Form = form;
        Min = min;
        Max = max;
    }

    public CardPickerForm Form { get; }

    public int Min { get; }

    public int Max { get; }

    public override TargetSlotKind Kind => TargetSlotKind.CardPicker;

    public override int MinSelection => Min;

    public override int MaxSelection => Max;

    public override TargetSlotPresentation Presentation => TargetSlotPresentation.CardArray;
}

/// <summary>请求级槽位数据（仿真）。</summary>
internal sealed class TargetingRequestContext
{
    private readonly Dictionary<string, IReadOnlyList<Ref<Entity>>> _references = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Ref<Entity>, bool>> _validators = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<CardListing>> _listings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);

    public TargetingRequestContext WithSlotReferences(string slotName, IEnumerable<Ref<Entity>> references)
    {
        _references[slotName] = references.ToArray();
        return this;
    }

    public TargetingRequestContext WithSlotDomainValidator(string slotName, Func<Ref<Entity>, bool> validator)
    {
        _validators[slotName] = validator;
        return this;
    }

    public TargetingRequestContext WithSlotListings(string slotName, IEnumerable<CardListing> listings)
    {
        _listings[slotName] = listings.ToArray();
        return this;
    }

    public TargetingRequestContext WithSlotParameter(string slotName, object? value)
    {
        _parameters[slotName] = value;
        return this;
    }

    internal bool TryGetReferences(string slotName, out IReadOnlyList<Ref<Entity>> references)
        => _references.TryGetValue(slotName, out references!);

    internal Func<Ref<Entity>, bool>? GetValidator(string slotName)
        => _validators.TryGetValue(slotName, out var validator) ? validator : null;

    internal bool TryGetListings(string slotName, out IReadOnlyList<CardListing> listings)
        => _listings.TryGetValue(slotName, out listings!);

    internal bool TryGetParameter(string slotName, out object? parameter)
        => _parameters.TryGetValue(slotName, out parameter);
}

/// <summary>仿真标签（承载业务槽位名 + 原参数）。</summary>
internal sealed class LegacyTag
{
    public LegacyTag(string name, object? value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public object? Value { get; }
}

/// <summary>产出（仿真；按槽位名读引用/标识）。</summary>
internal sealed class TargetOutcome
{
    private readonly IReadOnlyList<string> _names;
    private readonly Dictionary<string, IReadOnlyList<Ref<Entity>>> _bySlot;
    private readonly Dictionary<string, IReadOnlyList<string>> _idsBySlot;
    private readonly Dictionary<string, TargetSlotKind> _kindBySlot;

    internal TargetOutcome(
        IReadOnlyList<string> names,
        Dictionary<string, IReadOnlyList<Ref<Entity>>> bySlot,
        Dictionary<string, IReadOnlyList<string>> idsBySlot,
        Dictionary<string, TargetSlotKind> kindBySlot)
    {
        _names = names;
        _bySlot = bySlot;
        _idsBySlot = idsBySlot;
        _kindBySlot = kindBySlot;
    }

    public bool IsFlat => _names.Count <= 1;

    public Ref<Entity>? Single
        => IsFlat && _names.Count == 1 && _bySlot.TryGetValue(_names[0], out var list) && list.Count == 1
            ? list[0]
            : null;

    public IReadOnlyList<Ref<Entity>> List
        => IsFlat && _names.Count == 1 && _bySlot.TryGetValue(_names[0], out var list)
            ? list
            : Array.Empty<Ref<Entity>>();

    public IReadOnlyList<Ref<Entity>> GetSelection(string slotName) => _bySlot[slotName];

    public IReadOnlyList<string> GetIdentifiers(string slotName) => _idsBySlot[slotName];

    public TargetSlotKind GetSlotKind(string slotName) => _kindBySlot[slotName];

    public IReadOnlyList<string> SlotNames => _names;
}

/// <summary>targeter 结果（仿真；携带 Status + Reason + Outcome）。</summary>
internal sealed class LegacyTargetingResult
{
    internal LegacyTargetingResult(TargeterResult result, TargetOutcome? outcome)
    {
        Status = result.Status;
        Reason = result.Reason;
        Detail = result.Detail;
        Outcome = outcome;
    }

    public TargeterStatus Status { get; }

    public TargeterFailureReason? Reason { get; }

    public string? Detail { get; }

    public TargetOutcome? Outcome { get; }
}

/// <summary>targeter 请求对象（仿真）。</summary>
internal sealed class LegacyTargeter
{
    private readonly TargeterManager _manager;
    private readonly IReadOnlyList<TargetSlot> _slots;
    private readonly TargetingRequestContext? _context;
    private readonly TargetFilter? _filter;

    internal LegacyTargeter(
        TargeterManager manager,
        IReadOnlyList<TargetSlot> slots,
        TargetingRequestContext? context,
        TargetFilter? filter)
    {
        _manager = manager;
        _slots = slots;
        _context = context;
        _filter = filter;
    }

    /// <summary>执行（仿真：逐个槽位映射为选择器 Step）。</summary>
    public async Task<LegacyTargetingResult> Targeting()
    {
        var bySlot = new Dictionary<string, IReadOnlyList<Ref<Entity>>>(StringComparer.Ordinal);
        var idsBySlot = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var kindBySlot = new Dictionary<string, TargetSlotKind>(StringComparer.Ordinal);
        var names = new List<string>();

        var result = await _manager.RunAsync(async flow =>
        {
            var slots = _slots.Count > 0 ? _slots : new TargetSlot[] { new SingleSelectSlot() };

            foreach (var slot in slots)
            {
                names.Add(slot.Name);
                kindBySlot[slot.Name] = slot.Kind;
                var parameter = _context is not null && _context.TryGetParameter(slot.Name, out var p) ? p : null;

                switch (slot)
                {
                    case OptionSelectSlot option:
                    {
                        var step = await StepWithRetry(flow, 
                            SelectorTemplates.Option,
                            new IdentifierSetParameter(option.Options.Select(o => o.Id), 1, 1, new LegacyTag(slot.Name, parameter)));
                        if (!step.IsOk)
                        {
                            return Map(step.IsCancelled, step.Failure);
                        }

                        idsBySlot[slot.Name] = new[] { step.Value! };
                        break;
                    }

                    case CardPickerSlot picker when picker.Form == CardPickerForm.Listing:
                    {
                        var listings = _context is not null && _context.TryGetListings(picker.Name, out var l)
                            ? l
                            : Array.Empty<CardListing>();
                        var step = await StepWithRetry(flow, 
                            SelectorTemplates.CardPickerList,
                            new IdentifierSetParameter(listings.Select(x => x.Id), picker.MinSelection, picker.MaxSelection, new LegacyTag(slot.Name, parameter)));
                        if (!step.IsOk)
                        {
                            return Map(step.IsCancelled, step.Failure);
                        }

                        idsBySlot[slot.Name] = step.Value ?? Array.Empty<string>();
                        break;
                    }

                    case CardPickerSlot picker:
                    {
                        var references = _context is not null && _context.TryGetReferences(picker.Name, out var r)
                            ? r
                            : ResolveCandidates();
                        if (references.Count == 0 && picker.MinSelection > 0)
                        {
                            return TargeterResult.Failed(TargeterFailureReason.NoAvailableCandidates);
                        }

                        var step = await StepWithRetry(flow, 
                            SelectorTemplates.CardPickerSet,
                            new ReferenceSetParameter(references, picker.MinSelection, picker.MaxSelection, null, new LegacyTag(slot.Name, parameter)));
                        if (!step.IsOk)
                        {
                            return Map(step.IsCancelled, step.Failure);
                        }

                        bySlot[slot.Name] = step.Value ?? Array.Empty<Ref<Entity>>();
                        break;
                    }

                    case HandSelectSlot hand:
                    {
                        var references = _context is not null && _context.TryGetReferences(hand.Name, out var r)
                            ? r
                            : ResolveCandidates();
                        var validator = _context?.GetValidator(hand.Name);
                        var template = hand.Kind == TargetSlotKind.MulliganSelect
                            ? (Selector<IReadOnlyList<Ref<Entity>>>)SelectorTemplates.Mulligan
                            : SelectorTemplates.Hand;

                        var step = await StepWithRetry(flow, 
                            template,
                            new ReferenceSetParameter(references, hand.MinSelection, hand.MaxSelection, validator, new LegacyTag(slot.Name, parameter)));
                        if (!step.IsOk)
                        {
                            return Map(step.IsCancelled, step.Failure);
                        }

                        bySlot[slot.Name] = step.Value ?? Array.Empty<Ref<Entity>>();
                        break;
                    }

                    default:
                    {
                        var references = ResolveCandidates();
                        if (references.Count == 0 && slot.MinSelection > 0)
                        {
                            return TargeterResult.Failed(TargeterFailureReason.NoAvailableCandidates);
                        }

                        var tag = new LegacyTag(slot.Name, parameter);

                        if (slot.Kind == TargetSlotKind.SingleSelect)
                        {
                            var step = await StepWithRetry(flow, 
                                SelectorTemplates.TargetPoint,
                                new ReferenceSetParameter(references, 1, 1, null, tag));
                            if (!step.IsOk)
                            {
                                return Map(step.IsCancelled, step.Failure);
                            }

                            bySlot[slot.Name] = new[] { step.Value! };
                        }
                        else
                        {
                            var step = await StepWithRetry(flow, 
                                SelectorTemplates.Hand,
                                new ReferenceSetParameter(references, slot.MinSelection, slot.MaxSelection, null, tag));
                            if (!step.IsOk)
                            {
                                return Map(step.IsCancelled, step.Failure);
                            }

                            bySlot[slot.Name] = step.Value ?? Array.Empty<Ref<Entity>>();
                        }

                        break;
                    }
                }
            }

            return TargeterResult.Ok();
        });

        var outcome = result.IsOk ? new TargetOutcome(names, bySlot, idsBySlot, kindBySlot) : null;
        return new LegacyTargetingResult(result, outcome);
    }

    private static async Task<SelectorResult<TResult>> StepWithRetry<TResult>(ITargeterFlow flow, Selector<TResult> selector, SelectorParameter parameter)
    {
        var result = await flow.Step(selector, parameter);
        while (result.IsFailed && result.Failure == SelectorFailureReason.InvalidSelection)
        {
            result = await flow.Retry<TResult>();
        }

        return result;
    }

    private static TargeterResult Map(bool cancelled, SelectorFailureReason? failure)
        => cancelled ? TargeterResult.Cancelled() : TargeterResult.FromSelectorFailure(failure);

    /// <summary>候选＝收集脚本产物经两级筛选（仿真旧链路；新契约由后端直接给出候选）。</summary>
    private IReadOnlyList<Ref<Entity>> ResolveCandidates()
    {
        IReadOnlyList<Ref<Entity>> collected = Array.Empty<Ref<Entity>>();
        if (_manager.Bridge is MockTargeterBridge mock && mock.CollectScript is { } script)
        {
            var items = script(new object()).GetAwaiter().GetResult();
            var list = new List<Ref<Entity>>();
            foreach (var item in items)
            {
                if (item is Ref<Entity> reference)
                {
                    list.Add(reference);
                }
            }

            collected = list;
        }

        if (_filter?.CoarseFilter is { } coarse)
        {
            var output = coarse(collected);
            var inputSet = new HashSet<Ref<Entity>>(collected);
            collected = output is null ? Array.Empty<Ref<Entity>>() : output.Where(inputSet.Contains).ToArray();
        }

        if (_filter?.FineFilter is { } fine)
        {
            collected = collected.Where(fine).ToArray();
        }

        return collected;
    }
}

/// <summary>产出写入辅助（仿真）。</summary>
internal static class TargetOutcomeWriter
{
    public static TargetOutcome WriteOutcome(LegacyTargetingResult result, Action<TargetOutcome> write)
    {
        var outcome = Require(result);
        write(outcome);
        return outcome;
    }

    public static Ref<Entity> WriteSingle(LegacyTargetingResult result, Action<Ref<Entity>> write)
    {
        var outcome = Require(result);
        var single = outcome.Single
            ?? throw new InvalidOperationException("产出非扁平单值形态（仿真）。");
        write(single);
        return single;
    }

    public static IReadOnlyList<Ref<Entity>> WriteList(LegacyTargetingResult result, Action<IReadOnlyList<Ref<Entity>>> write)
    {
        var outcome = Require(result);
        if (!outcome.IsFlat)
        {
            throw new InvalidOperationException("产出为非扁平形态（仿真）。");
        }

        write(outcome.List);
        return outcome.List;
    }

    private static TargetOutcome Require(LegacyTargetingResult result)
        => result.Status == TargeterStatus.Ok && result.Outcome is not null
            ? result.Outcome
            : throw new InvalidOperationException("结果非成功（仿真）。");
}

/// <summary>旧入口扩展（仿真）：把 CreateTargeter 映射为仿真请求对象。</summary>
internal static class LegacyTargeterManagerExtensions
{
    public static LegacyTargeter CreateTargeter(
        this TargeterManager manager,
        TargetFilter? filter = null,
        IEnumerable<TargetSlot>? slots = null,
        TargetingRequestContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return new LegacyTargeter(manager, slots?.ToArray() ?? Array.Empty<TargetSlot>(), context, filter);
    }
}

/// <summary>判定器选择规则扩展（仿真）：把选择规则包装为旧形状筛选器。</summary>
internal static class LegacyJudicatorSelectionRuleExtensions
{
    public static TargetFilter AsFilter(this Orc.Game.Judicators.JudicatorSelectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new TargetFilter(fineFilter: rule.AsPredicate());
    }
}
