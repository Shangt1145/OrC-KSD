namespace Orc.Game.Cards;

/// <summary>
/// 对局级具名触发器注册表（E1-27）：把**对局级流程触发器**（指挥/单位移动/单位攻击/造成攻击伤害）
/// 按名登记，供效果预制体 <c>injects</c> 解析——宿主卡未命中登记时**回退**到本表。
/// <para>注册以**延迟提供器**承载（流程触发器创建时点晚于注册表创建，但早于卡加载；解析发生在装载期）。</para>
/// </summary>
public sealed class MatchNamedTriggers
{
    private readonly Dictionary<string, (Type ViewType, Func<object?> Provider)> _entries = new(StringComparer.Ordinal);

    /// <summary>登记一枚对局级具名触发器（重复名＝拒绝）。</summary>
    /// <exception cref="ArgumentException">name 空白。</exception>
    /// <exception cref="ArgumentNullException">viewType 或 provider 为 null。</exception>
    /// <exception cref="InvalidOperationException">同名重复登记。</exception>
    public void Register(string name, Type viewType, Func<object?> provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(viewType);
        ArgumentNullException.ThrowIfNull(provider);

        if (!_entries.TryAdd(name, (viewType, provider)))
        {
            throw new InvalidOperationException($"对局级具名触发器 '{name}' 重复登记（注册即配置）。");
        }
    }

    /// <summary>按名解析（未登记或提供器暂不可用＝false）。</summary>
    /// <exception cref="ArgumentException">name 空白。</exception>
    public bool TryFind(string name, out object? trigger, out Type? viewType)
    {
        trigger = null;
        viewType = null;
        if (string.IsNullOrWhiteSpace(name) || !_entries.TryGetValue(name, out var entry))
        {
            return false;
        }

        var resolved = entry.Provider();
        if (resolved is null)
        {
            return false;
        }

        trigger = resolved;
        viewType = entry.ViewType;
        return true;
    }
}
