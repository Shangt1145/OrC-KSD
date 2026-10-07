using System.Collections;

namespace Orc.Game.Board;

/// <summary>
/// 战线（2A 槽位模型）：固定容量槽位序列（容量 5/5/5；按索引 0..N-1）。
/// 读面：容量 / 槽位序列（实现 <see cref="IReadOnlyList{T}"/>；索引越界＝明确错误、不静默）；
/// 「整线空槽枚举」可经槽位序列读面派生（不单列）。
/// 邻位动态候选（主查询）：遍历语义＝所有被占槽位（含 HQ）取左右相邻空槽、去重、按槽位索引升序（端点越界侧跳过）；
/// 结果＝部署候选位（部署＝己方支援线邻位空槽；部署校验属后续批次、本批仅提供查询）。
/// 〔移动候选口径（终态）：指挥移动＝仅推进、候选＝前线空槽；无后撤/横移候选——不在本查询范围。〕
/// </summary>
public sealed class BattleLine : IReadOnlyList<Slot>
{
    private readonly Slot[] _slots;

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
    /// 邻位动态候选（部署候选位——本批仅查询）：遍历所有被占槽位（含 HQ），取其左右相邻槽位中的空槽、
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
