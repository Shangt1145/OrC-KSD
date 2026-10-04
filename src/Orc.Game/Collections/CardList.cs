using System.Collections;
using Orc.Game.Cards;

namespace Orc.Game.Collections;

/// <summary>
/// 卡牌 id 序列（卡组名单形态；2A 起条目化）：可重复、有序；条目＝id（与卡牌库注册键一致）＋可选加载实例（<see cref="CardBase"/>）。
/// 对外读面保持 id 形态（Count / 索引 / 枚举＝id；卡组计数语义＝剩余可抽张数、加载不改计数）；实例读面经加载通道
/// （<see cref="AttachInstanceAt"/> 装配 / <see cref="DrawInstance"/> 取件——「加载时实例化的卡＝对局后续使用的实例」）。
/// 纯容器：不校验 id 格式（空白 id 的创建期校验由对局装配层负责）。
/// 方法语义：Add / AddRange 尾部装填；Insert 指定位置插入（越界抛错）；Draw 取首张并移除（空集合抛错）；
/// Shuffle 以传入的确定性随机源（受控源形态 <see cref="IRandomSource"/>）就地打乱（Fisher–Yates；集合自身不持有随机源）；
/// Instantiate 经卡牌库把名单转换为卡牌实例集（纯转换，不含洗牌/抽取等副作用）。
/// </summary>
public sealed class CardList : IReadOnlyList<string>
{
    private readonly List<Entry> _items;

    private sealed class Entry
    {
        internal Entry(string id)
        {
            Id = id;
        }

        internal string Id { get; }

        internal CardBase? Instance { get; set; }
    }

    /// <summary>创建空名单。</summary>
    public CardList() => _items = new List<Entry>();

    /// <summary>批量构造（装入给定 id 序列，保持顺序）。</summary>
    /// <exception cref="ArgumentNullException">ids 为 null。</exception>
    public CardList(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _items = new List<Entry>(ids.Select(id => new Entry(id)));
    }

    /// <summary>元素数量（卡组语义＝剩余可抽张数；加载不改计数）。</summary>
    public int Count => _items.Count;

    /// <summary>按索引访问（读面＝id）。</summary>
    public string this[int index] => _items[index].Id;

    /// <summary>尾部追加一个 id（允许重复）。</summary>
    public void Add(string id) => _items.Add(new Entry(id));

    /// <summary>尾部批量追加（保持传入顺序；允许重复）。</summary>
    /// <exception cref="ArgumentNullException">ids 为 null。</exception>
    public void AddRange(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        foreach (var id in ids)
        {
            _items.Add(new Entry(id));
        }
    }

    /// <summary>在指定位置插入（0..Count；越界抛错、不钳制）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">index 越界。</exception>
    public void Insert(int index, string id)
    {
        if (index < 0 || index > _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"插入位置越界（合法范围：0..{_items.Count}）。");
        }

        _items.Insert(index, new Entry(id));
    }

    /// <summary>取首张并移除；返回该 id（名单通道——语义保持第一批）。</summary>
    /// <exception cref="InvalidOperationException">空集合（明确失败优于静默）。</exception>
    public string Draw()
    {
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("卡组名单为空，无法抽取（空集合 Draw 被拒绝）。");
        }

        var id = _items[0].Id;
        _items.RemoveAt(0);
        return id;
    }

    /// <summary>
    /// 取首张并移除，返回其加载实例（实例通道；起手装载 / 抽牌链路使用——
    /// 「加载时实例化的卡＝对局后续使用的实例」）。
    /// 空集合＝明确错误；首张未装配实例（未加载 / 已错用名单通道）＝明确错误。
    /// </summary>
    /// <exception cref="InvalidOperationException">空集合；或首张未装配加载实例。</exception>
    public CardBase DrawInstance()
    {
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("卡组名单为空，无法抽取（空集合 Draw 被拒绝）。");
        }

        var entry = _items[0];
        if (entry.Instance is null)
        {
            throw new InvalidOperationException($"卡组首条（id '{entry.Id}'）尚未装配加载实例（加载前不允许经实例通道取件）。");
        }

        _items.RemoveAt(0);
        return entry.Instance;
    }

    /// <summary>
    /// 装配加载实例（加载通道）：把实例关联到指定索引的条目（不改计数——「加载不改计数（仅装配）」）；
    /// 装配后该条目可经 <see cref="DrawInstance"/> 取件。
    /// </summary>
    /// <exception cref="ArgumentNullException">instance 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">index 越界。</exception>
    /// <exception cref="InvalidOperationException">该条目已装配实例（重复装配被拒绝）。</exception>
    public void AttachInstanceAt(int index, CardBase instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (index < 0 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"装配位置越界（合法范围：0..{_items.Count - 1}）。");
        }

        var entry = _items[index];
        if (entry.Instance is not null)
        {
            throw new InvalidOperationException($"卡组第 {index} 条（id '{entry.Id}'）已装配实例（重复装配被拒绝）。");
        }

        entry.Instance = instance;
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
        foreach (var entry in _items)
        {
            set.Add(library.Instantiate(entry.Id));
        }

        return set;
    }

    /// <summary>枚举（插入序；读面＝id）。</summary>
    public IEnumerator<string> GetEnumerator() => _items.Select(entry => entry.Id).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
