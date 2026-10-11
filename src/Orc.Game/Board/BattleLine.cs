using System.Collections;
using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game.Board;

/// <summary>
/// 战线（2A 槽位模型）：固定容量槽位序列（容量 5/5/5；按索引 0..N-1）。
/// 读面：容量 / 槽位序列（实现 <see cref="IReadOnlyList{T}"/>；索引越界＝明确错误、不静默）；
/// 「整线空槽枚举」可经槽位序列读面派生（不单列）。
/// 部署主查询 GetInsertionGaps 提供全部阵型插入边界（含 HQ），底层槽位仅承载最终位置；
/// GetAdjacentEmptySlots 保留为旧槽位模型的查询，不再决定预打出部署候选。
/// 〔移动候选口径（终态）：指挥移动＝仅推进、候选＝前线空槽；无后撤/横移候选——不在本查询范围。〕
/// </summary>
public sealed class BattleLine : IReadOnlyList<Slot>
{
    private readonly Slot[] _slots;
    private object?[]? _gapPositions;
    private IReadOnlyList<FormationGap> _gaps = Array.Empty<FormationGap>();
    private long _gapGeneration;

    /// <summary>N occupied entities offer N+1 insertion gaps, until capacity is full.
    /// Unchanged queries reuse identities; occupancy changes invalidate every old candidate.</summary>
    public IReadOnlyList<FormationGap> GetInsertionGaps()
    {
        var positions = _slots.Select(s => s.Occupant).ToArray();
        if (_gapPositions is not null && positions.SequenceEqual(_gapPositions)) return _gaps;
        foreach (var gap in _gaps) gap.Destroy();
        _gapPositions = positions; _gapGeneration++;
        var occupants = positions.OfType<Entity>().ToArray();
        if (occupants.Length != positions.Count(p => p is not null)
            || occupants.Length >= Capacity || occupants.Any(e => e is not UnitCard && e is not Hq))
            return _gaps = Array.Empty<FormationGap>();
        return _gaps = Array.AsReadOnly(Enumerable.Range(0, occupants.Length + 1)
            .Select(i => new FormationGap(this, _gapGeneration, i, positions,
                i > 0 ? occupants[i - 1] : null, i < occupants.Length ? occupants[i] : null)).ToArray());
    }

    internal bool IsCurrent(FormationGap gap) => ReferenceEquals(gap.Line, this)
        && gap.Generation == _gapGeneration && _gaps.Contains(gap)
        && _slots.Select(s => s.Occupant).SequenceEqual(gap.Positions)
        && _slots.Any(s => s.IsEmpty);

    /// <summary>Engine-only layout commit after play validation. All existing entities retain order;
    /// positions (including HQ) are updated together before any deployment signal is emitted.</summary>
    internal bool TryOpenGap(FormationGap gap, out Slot slot)
    {
        slot = null!;
        if (!gap.IsCurrent) return false;
        var occupants = gap.Positions.OfType<Entity>().ToArray();
        if (occupants.Any(e => e is UnitCard u && (!u.TryGetData<UnitStateData>(out var state)
                || !ReferenceEquals(state.Position?.Occupant, u) || state.IsDestroyed))) return false;
        var start = (Capacity - occupants.Length - 1) / 2;
        foreach (var position in _slots) position.Clear();
        for (var i = 0; i < occupants.Length; i++)
        {
            var destination = _slots[start + i + (i >= gap.Ordinal ? 1 : 0)];
            destination.Place(occupants[i]);
            if (occupants[i] is UnitCard unit) unit.GetData<UnitStateData>().Position = destination;
            else if (occupants[i] is Hq hq) hq.AttachToSlot(destination);
        }
        slot = _slots[start + gap.Ordinal];
        return true;
    }

    /// <summary>创建固定容量战线（槽位随创建生成、索引固定）。</summary>
    /// <param name="capacity">容量（须为正整数）。</param>
    /// <param name="lineName">线名（2B 加性可选参数：用于槽位引用形态的命名，如「玩家A支援线[1]」；缺省＝「战线」）。</param>
    /// <exception cref="ArgumentOutOfRangeException">capacity 非正整数。</exception>
    public BattleLine(int capacity, string? lineName = null)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "战线容量须为正整数（≥1）。");
        }

        var resolvedLineName = string.IsNullOrWhiteSpace(lineName) ? "战线" : lineName;
        _slots = new Slot[capacity];
        for (var i = 0; i < capacity; i++)
        {
            _slots[i] = new Slot(i, resolvedLineName);
        }
    }

    /// <summary>容量（槽位数；4/5/4 配置承载）。</summary>
    public int Capacity => _slots.Length;

    /// <summary>槽位数量（＝容量）。</summary>
    public int Count => _slots.Length;

    /// <summary>按索引访问槽位。</summary>
    /// <exception cref="ArgumentOutOfRangeException">index 越界（明确错误、不静默）。</exception>
    public Slot this[int index]
    {
        get
        {
            if (index < 0 || index >= _slots.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"槽位索引越界（合法范围：0..{_slots.Length - 1}）。");
            }

            return _slots[index];
        }
    }

    /// <summary>
    /// 旧模型邻位空槽查询（不是当前预打出部署候选）：遍历所有被占槽位（含 HQ），取其左右相邻槽位中的空槽、
    /// 去重、按槽位索引升序（端点越界侧跳过）；无被占槽位或无空邻位＝空列表。
    /// 〔移动候选（指挥）＝前线空槽；无后撤/横移候选——见指挥侧，不在本查询范围。〕
    /// </summary>
    public IReadOnlyList<Slot> GetAdjacentEmptySlots()
    {
        var result = new List<Slot>();
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (!slot.IsEmpty)
            {
                continue;
            }

            var hasOccupiedNeighbor = (i > 0 && !_slots[i - 1].IsEmpty)
                || (i < _slots.Length - 1 && !_slots[i + 1].IsEmpty);

            if (hasOccupiedNeighbor)
            {
                result.Add(slot);
            }
        }

        return result;
    }

    /// <summary>枚举（索引序）。</summary>
    public IEnumerator<Slot> GetEnumerator() => ((IEnumerable<Slot>)_slots).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
