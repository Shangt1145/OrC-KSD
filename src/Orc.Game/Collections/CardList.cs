using System.Collections;
using Orc.Game.Cards;

namespace Orc.Game.Collections;

/// <summary>
/// 卡牌 id 序列（卡组名单形态）：可重复、有序；元素＝字符串 id（与卡牌库注册键一致）。
/// 纯容器：不校验 id 格式（空白 id 的创建期校验由对局装配层负责）。
/// 方法语义：Add / AddRange 尾部装填；Insert 指定位置插入（越界抛错）；Draw 取首张并移除（空集合抛错）；
/// Shuffle 以传入的对局级随机源就地打乱（Fisher–Yates；集合自身不持有随机源）；
/// Instantiate 经卡牌库把名单转换为卡牌实例集（纯转换，不含洗牌/抽取等副作用）。
/// </summary>
public sealed class CardList : IReadOnlyList<string>
{
    private readonly List<string> _items;

    /// <summary>创建空名单。</summary>
    public CardList() => _items = new List<string>();

    /// <summary>批量构造（装入给定 id 序列，保持顺序）。</summary>
    /// <exception cref="ArgumentNullException">ids 为 null。</exception>
    public CardList(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _items = new List<string>(ids);
    }

    /// <summary>元素数量。</summary>
    public int Count => _items.Count;

    /// <summary>按索引访问（读面）。</summary>
    public string this[int index] => _items[index];

    /// <summary>尾部追加一个 id（允许重复）。</summary>
    public void Add(string id) => _items.Add(id);

    /// <summary>尾部批量追加（保持传入顺序；允许重复）。</summary>
    /// <exception cref="ArgumentNullException">ids 为 null。</exception>
    public void AddRange(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _items.AddRange(ids);
    }

    /// <summary>在指定位置插入（0..Count；越界抛错、不钳制）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">index 越界。</exception>
    public void Insert(int index, string id)
    {
        if (index < 0 || index > _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"插入位置越界（合法范围：0..{_items.Count}）。");
        }

        _items.Insert(index, id);
    }

    /// <summary>取首张并移除；返回该 id。</summary>
    /// <exception cref="InvalidOperationException">空集合（明确失败优于静默）。</exception>
    public string Draw()
    {
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("卡组名单为空，无法抽取（空集合 Draw 被拒绝）。");
        }

        var id = _items[0];
        _items.RemoveAt(0);
        return id;
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

    /// <summary>
    /// 名单到引用集的实例化（经由卡牌库）：逐 id 创建卡牌实例并保持顺序。
    /// 空名单 → 空集；含未注册 id → 抛明确错误；重复 id → 多个独立实例。纯转换（无副作用）。
    /// </summary>
    /// <exception cref="ArgumentNullException">library 为 null。</exception>
    /// <exception cref="KeyNotFoundException">名单含未注册 id。</exception>
    public CardSet Instantiate(CardLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        var set = new CardSet();
        foreach (var id in _items)
        {
            set.Add(library.Instantiate(id));
        }

        return set;
    }

    /// <summary>枚举（插入序）。</summary>
    public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
