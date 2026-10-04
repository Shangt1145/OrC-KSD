using System.Collections;
using Orc.Cards;

namespace Orc.Game.Collections;

/// <summary>
/// 卡牌实例集合（手牌/战线等容器形态）：有序（插入序），元素＝引擎卡牌实例（Orc Card）。
/// 同一实例重复添加被拒绝（实例唯一归属不变量：一张牌不能同属两处；同名多张＝多个独立实例）。
/// 方法语义：Add / AddRange 尾部装填；Insert 指定位置插入（越界抛错）；Draw 取首张并移除（空集合抛错）；
/// Shuffle 以传入的确定性随机源（受控源形态 <see cref="IRandomSource"/>）就地打乱（Fisher–Yates；集合自身不持有随机源）。
/// 〔G7 手牌顺序原子面（纯容器操作：除成员/顺序变化外无副作用＋原子）〕MoveTo / MoveToLeft / MoveToRight
/// 移至最左/最右/指定位置（目标卡不在集合中、目标位置越界＝明确拒绝；自移＝无操作成功）；
/// RemoveAt 按索引移除（返回被移除卡引用）；Peek 按索引只读读取——「定位→组合」的明确读点
/// （如"弃掉最左"＝Peek(0)＋弃置动作；按索引移除不得与销毁混用/串联）。
/// </summary>
public sealed class CardSet : IReadOnlyList<Card>
{
    private readonly List<Card> _items = new();

    /// <summary>元素数量。</summary>
    public int Count => _items.Count;

    /// <summary>按索引访问（读面）。</summary>
    public Card this[int index] => _items[index];

    /// <summary>尾部添加（保持插入序）。</summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">该实例已在集合中（重复添加被拒绝）。</exception>
    public void Add(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (_items.Contains(card))
        {
            throw new InvalidOperationException($"卡牌 '{card.Name}' 已在集合中（同一实例重复添加被拒绝）。");
        }

        _items.Add(card);
    }

    /// <summary>尾部批量添加（保持传入顺序）。</summary>
    /// <exception cref="ArgumentNullException">cards 为 null。</exception>
    /// <exception cref="InvalidOperationException">含重复实例。</exception>
    public void AddRange(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        foreach (var card in cards)
        {
            Add(card);
        }
    }

    /// <summary>在指定位置插入（0..Count；越界抛错、不钳制）。</summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">index 越界。</exception>
    /// <exception cref="InvalidOperationException">该实例已在集合中（重复添加被拒绝）。</exception>
    public void Insert(int index, Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (index < 0 || index > _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"插入位置越界（合法范围：0..{_items.Count}）。");
        }

        if (_items.Contains(card))
        {
            throw new InvalidOperationException($"卡牌 '{card.Name}' 已在集合中（同一实例重复添加被拒绝）。");
        }

        _items.Insert(index, card);
    }

    /// <summary>
    /// 移除指定实例（2B 加性面；离手链路使用）：存在＝移除并返回 true；不存在＝false、不抛错（幂等语义）。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    public bool Remove(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _items.Remove(card);
    }

    /// <summary>
    /// 移至指定位置（G7 手牌顺序原子面；0 基、＝移动完成后该卡所在位置；有效范围 0..Count−1，以移动后的序列为准）。
    /// 纯容器操作（除成员顺序变化外无副作用——不发射信号、不销毁卡、不触发更新）；原子（要么成功、要么集合不变）。
    /// 自移（目标位置＝当前位置）＝无操作、视为成功（合法请求、结果＝集合不变——无需调用方特判）。
    /// </summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">该实例不在集合中（前提校验失败——明确拒绝）。</exception>
    /// <exception cref="ArgumentOutOfRangeException">index 越界（负数或 ≥Count——明确拒绝）。</exception>
    public void MoveTo(int index, Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var current = _items.IndexOf(card);
        if (current < 0)
        {
            throw new InvalidOperationException($"卡牌 '{card.Name}' 不在集合中（移至位置被拒绝：目标卡不在手牌）。");
        }

        if (index < 0 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"目标位置越界（合法范围：0..{_items.Count - 1}）。");
        }

        if (index == current)
        {
            return; // 自移：无操作、视为成功（结果＝集合不变）
        }

        _items.RemoveAt(current);
        _items.Insert(index, card);
    }

    /// <summary>移至最左（G7；＝移至索引 0 位置）。其余语义同 <see cref="MoveTo"/>。</summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">该实例不在集合中（明确拒绝）。</exception>
    public void MoveToLeft(Card card) => MoveTo(0, card);

    /// <summary>移至最右（G7；＝移至索引 Count−1 位置）。其余语义同 <see cref="MoveTo"/>。</summary>
    /// <exception cref="ArgumentNullException">card 为 null。</exception>
    /// <exception cref="InvalidOperationException">该实例不在集合中（明确拒绝）。</exception>
    public void MoveToRight(Card card) => MoveTo(Count - 1, card);

    /// <summary>
    /// 按索引移除（G7；0 基）并返回被移除的卡引用（供调用方承接后续处置）。
    /// 纯容器操作（不销毁、不发信号——与销毁型动作解耦；「取出（不销毁）供其他处置」的通用容器能力）。
    /// 注意边界的组合约束：按索引移除不得与销毁混用/串联——凡销毁型处置一律「定位 → 对应动作」直达
    /// （先按索引移除再弃置将因弃置前提校验失败而被拒绝——该失败为预期行为）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">index 越界（明确拒绝）。</exception>
    public Card RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"移除位置越界（合法范围：0..{_items.Count - 1}）。");
        }

        var card = _items[index];
        _items.RemoveAt(index);
        return card;
    }

    /// <summary>
    /// 按索引只读读取（G7；0 基；「最左」＝索引 0、「最右」＝Count−1）：不移除、不改动（只读）。
    /// 用途＝为「定位 → 组合」提供明确读点（如"弃掉最左"＝Peek(0)＋弃置动作）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">index 越界（明确拒绝、可区分；读取后集合不变）。</exception>
    public Card Peek(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"读取位置越界（合法范围：0..{_items.Count - 1}）。");
        }

        return _items[index];
    }

    /// <summary>取首张并移除；返回该卡牌实例。</summary>
    /// <exception cref="InvalidOperationException">空集合（明确失败优于静默）。</exception>
    public Card Draw()
    {
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("集合为空，无法抽取（空集合 Draw 被拒绝）。");
        }

        var card = _items[0];
        _items.RemoveAt(0);
        return card;
    }

    /// <summary>以给定确定性随机源就地打乱（Fisher–Yates；对局路径经对局随机服务〔受控源形态〕传入、可复现——G8：集合不持有随机源）。</summary>
    /// <exception cref="ArgumentNullException">source 为 null。</exception>
    public void Shuffle(IRandomSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        for (var i = _items.Count - 1; i > 0; i--)
        {
            var j = source.Next(i + 1);
            (_items[i], _items[j]) = (_items[j], _items[i]);
        }
    }

    /// <summary>枚举（插入序）。</summary>
    public IEnumerator<Card> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
