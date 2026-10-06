namespace Orc.Game.EffectParsing.Lexicon;

/// <summary>词表类别（与 12 类 token 一一对应；标点/未知不登记）。</summary>
public enum LexiconCategory
{
    /// <summary>触发词（具名触发 / 触发后缀）。</summary>
    Trigger,

    /// <summary>连接/逻辑词。</summary>
    Connective,

    /// <summary>代词/回指。</summary>
    Pronoun,

    /// <summary>动作词。</summary>
    Action,

    /// <summary>量词/选靶。</summary>
    Quant,

    /// <summary>阵营词。</summary>
    Side,

    /// <summary>区域/落点词。</summary>
    Zone,

    /// <summary>过滤词（兵种/词条/系列/属性）。</summary>
    Filter,

    /// <summary>条件词。</summary>
    Cond,

    /// <summary>数词。</summary>
    Num,
}

/// <summary>词表条目：字面 + 类别 + 载荷（键值皆为字符串，便于外置）。</summary>
public sealed class LexiconEntry
{
    /// <summary>创建条目。</summary>
    /// <exception cref="ArgumentException">literal 空白。</exception>
    /// <exception cref="ArgumentNullException">payload 为 null。</exception>
    public LexiconEntry(string literal, LexiconCategory category, IReadOnlyDictionary<string, string>? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(literal);
        Literal = literal;
        Category = category;
        Payload = payload ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>字面。</summary>
    public string Literal { get; }

    /// <summary>类别。</summary>
    public LexiconCategory Category { get; }

    /// <summary>载荷。</summary>
    public IReadOnlyDictionary<string, string> Payload { get; }

    /// <summary>取载荷键（缺省 null）。</summary>
    public string? Get(string key) => Payload.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// 词表集合（转换段外的解析段 S6）：以**最长匹配**扫描；同一字面**只允许登记在一个类别**
/// （加载期强制，杜绝运行期优先级歧义——A-D2）。
/// </summary>
public sealed class LexiconSet
{
    private readonly Dictionary<string, LexiconEntry> _map;
    private readonly int[] _lengths;

    /// <summary>创建词表集合。</summary>
    /// <exception cref="ArgumentNullException">entries 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一字面跨类别重复。</exception>
    public LexiconSet(IEnumerable<LexiconEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _map = new Dictionary<string, LexiconEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!_map.TryAdd(entry.Literal, entry))
            {
                throw new InvalidOperationException(
                    $"词表字面重复：'{entry.Literal}'（类别 {_map[entry.Literal].Category} 与 {entry.Category}）。"
                    + "同一字面只允许登记在一个类别，请人工消歧。");
            }
        }

        _lengths = _map.Keys.Select(key => key.Length).Distinct().OrderByDescending(len => len).ToArray();
    }

    /// <summary>条目数。</summary>
    public int Count => _map.Count;

    /// <summary>全部条目。</summary>
    public IReadOnlyCollection<LexiconEntry> Entries => _map.Values;

    /// <summary>最长字面长度（扫描上界）。</summary>
    public int MaxLength => _lengths.Length == 0 ? 0 : _lengths[0];

    /// <summary>在 <paramref name="text"/> 的 <paramref name="index"/> 处做最长匹配。</summary>
    public bool TryMatch(string text, int index, out LexiconEntry entry, out int length)
    {
        entry = null!;
        length = 0;
        foreach (var len in _lengths)
        {
            if (index + len > text.Length)
            {
                continue;
            }

            if (_map.TryGetValue(text.Substring(index, len), out var found))
            {
                entry = found;
                length = len;
                return true;
            }
        }

        return false;
    }
}
