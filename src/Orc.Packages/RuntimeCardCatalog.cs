using Orc.Game.Cards;

namespace Orc.Packages;

/// <summary>
/// **引擎级运行期卡数据目录**（S4·R5/D7·第一级）：宿主持有；注册进此处的卡随 <see cref="EngineHost.CreateSnapshot"/>
/// 冻结进**之后新建**的对局（进行中的对局不受影响）。**只追加不覆盖**（同 id 重复＝拒绝）。
/// </summary>
public sealed class RuntimeCardCatalog
{
    private readonly List<RuntimeCardRegistration> _entries = [];

    /// <summary>已注册条目（注册序）。</summary>
    public IReadOnlyList<RuntimeCardRegistration> Entries => _entries;

    /// <summary>是否已含该 id。</summary>
    /// <exception cref="ArgumentException">id 为 null/空白。</exception>
    public bool Contains(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _entries.Any(e => string.Equals(e.Id, id, StringComparison.Ordinal));
    }

    /// <summary>注册一枚卡（只追加）。</summary>
    /// <exception cref="ArgumentNullException">entry 为 null。</exception>
    /// <exception cref="InvalidOperationException">同 id 已注册（拒绝——只追加不覆盖）。</exception>
    public void Register(RuntimeCardRegistration entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        if (Contains(entry.Id))
        {
            throw new InvalidOperationException($"卡 id '{entry.Id}' 已在引擎级卡数据目录注册（只追加不覆盖）。");
        }

        _entries.Add(entry);
    }

    /// <summary>取快照副本（供对局冻结）。</summary>
    public IReadOnlyList<RuntimeCardRegistration> Snapshot() => _entries.ToArray();
}
