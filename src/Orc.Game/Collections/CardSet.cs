using System.Collections;
using Orc.Cards;

namespace Orc.Game.Collections;

/// <summary>
/// 卡牌实例集合（手牌/战线等容器形态）：有序（插入序），元素＝引擎卡牌实例（Orc Card）。
/// 同一实例重复添加被拒绝（实例唯一归属不变量：一张牌不能同属两处；同名多张＝多个独立实例）。
/// 方法语义：Add / AddRange 尾部装填；Insert 指定位置插入（越界抛错）；Draw 取首张并移除（空集合抛错）；
/// Shuffle 以传入的对局级随机源就地打乱（Fisher–Yates；集合自身不持有随机源）。
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

    /// <summary>以给定随机源就地打乱（Fisher–Yates；对局级随机源经此传入、可复现）。</summary>
    /// <exception cref="ArgumentNullException">random 为 null。</exception>
    public void Shuffle(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        for (var i = _items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (_items[i], _items[j]) = (_items[j], _items[i]);
        }
    }

    /// <summary>枚举（插入序）。</summary>
    public IEnumerator<Card> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
