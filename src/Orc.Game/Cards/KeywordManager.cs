using System.Diagnostics.CodeAnalysis;
using Orc.Cards;
using Orc.Core;

namespace Orc.Game.Cards;

/// <summary>
/// 词条管理组件（2C-A1；卡上伴生、构造期常驻——轻量伴生非数据组件，对齐卡侧修饰器组件先例）：
/// 词条组件的注册与索引（有无/参值查询）＋词条集合管理（授予/移除/参值改写入口面）＋清空处置面（A2 加性）。
/// 宿主＝引擎薄容器 <see cref="Card"/>（A2 泛化：单位与 HQ 同族承载——对齐修饰机制 W3-3 泛化先例）。
/// 与旧三口的关系＝合并替代（不保留为独立可查询组件）：标识登记 → 本组件注册与索引；逻辑装载 → 词条组件
/// 自含运行逻辑（个体卸载替代整体注销）；运行计数器 → 词条组件自含数据（不保留通用字符串键计数器形态）。
/// 查询面：无词条卡/无该词条/词条无参值＝false/null（不抛错——动态查询语义、存在性判定不含异常面）；
/// 「HasKeyword=true 且 GetKeywordValue=null」⇔ 有词条无参值。
/// 操作面（授予/移除/参值改写）：对已死亡卡＝拒绝（终态；查询面保持可用）；授予/移除幂等（重复＝无操作、返回 false）；
/// 卸载不存在词条不报错；参值改写前提＝词条存在（不存在＝明确异常）。
/// 授予/移除提供可等待完成面（await 返回时装载/卸载已毕、状态一致——内部同步执行、无异步悬置，P2 精神）。
/// 空管理面（无词条卡）零行为负担、可寻址（含无词条卡不抛）。
/// </summary>
public sealed class KeywordManager
{
    private readonly Card _card;
    private readonly LogicEngine _engine;
    private readonly Dictionary<string, KeywordComponent> _components = new(StringComparer.Ordinal);
    private readonly List<KeywordComponent> _active = new(); // 行为态（登记序；死亡注销清空——存在性/参值保留）

    internal KeywordManager(Card card, LogicEngine engine)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(engine);
        _card = card;
        _engine = engine;
    }

    /// <summary>
    /// 词条装载上下文提供器（A2 加性：自宿主属性内聚至本组件——CardBase 与 HQ 装配时各自转注；
    /// 授予链经此取装载上下文〔延迟读取〕；未装配＝null＝组件运行逻辑装载跳过注册、不抛错）。
    /// </summary>
    internal Func<KeywordLoadContext?>? LoadContextProvider { get; set; }

    // ---------- 查询面（统一读口：有无＋参值；无词条卡/无该词条等价） ----------

    /// <summary>是否存在指定词条（存在性查询；null/空白＝false、不抛错——无词条卡不抛）。</summary>
    public bool Has(string keyword)
        => !string.IsNullOrWhiteSpace(keyword) && _components.ContainsKey(keyword);

    /// <summary>读取词条参值（参值查询；无词条卡/卡无该词条/词条无参值＝null——与 <see cref="Has"/> 组合区分「有词条无参值」）。</summary>
    public int? GetValue(string keyword)
        => !string.IsNullOrWhiteSpace(keyword) && _components.TryGetValue(keyword, out var component)
            ? component.Value
            : null;

    /// <summary>已挂载词条组件（行为态、登记序只读快照——体系遍历面如部署链收尾；死亡注销后为空）。</summary>
    public IReadOnlyList<KeywordComponent> Components => _active.ToArray();

    /// <summary>按标识寻址词条组件（体系面；未注册＝false——null/空白不抛错）。</summary>
    public bool TryGetComponent(string keyword, [NotNullWhen(true)] out KeywordComponent? component)
    {
        if (!string.IsNullOrWhiteSpace(keyword) && _components.TryGetValue(keyword, out var found))
        {
            component = found;
            return true;
        }

        component = null;
        return false;
    }

    /// <summary>按标识寻址强类型词条组件（体系面；类型不匹配＝false）。</summary>
    public bool TryGetComponent<T>(string keyword, [NotNullWhen(true)] out T? component)
        where T : KeywordComponent
    {
        if (TryGetComponent(keyword, out var found) && found is T typed)
        {
            component = typed;
            return true;
        }

        component = null;
        return false;
    }

    /// <summary>
    /// 按标识寻址词条组件（行为态面；A4 加性——「按行为态取组件」的查询承载，如亡计执行面解析）：
    /// 仅当组件已注册（<see cref="Has"/> 面）且处于行为态时命中；死亡注销后＝false
    /// （行为态已清空——登记/参值保留、照常可读，但不可执行）。
    /// </summary>
    /// <exception cref="ArgumentNullException">无（null/空白＝false、不抛错——与查询面一致）。</exception>
    public bool TryGetActiveComponent<T>(string keyword, [NotNullWhen(true)] out T? component)
        where T : KeywordComponent
    {
        if (TryGetComponent(keyword, out var found) && found is T typed && _active.Contains(found))
        {
            component = typed;
            return true;
        }

        component = null;
        return false;
    }

    // ---------- 操作面（统一写口：授予/移除/参值改写；对已死亡卡＝拒绝——终态） ----------

    /// <summary>
    /// 授予（挂载；可等待完成）：①存在性置位（数据面可见）→ ②运行逻辑装载＋内嵌效果装载 → ③OnGrant（授予回调、最后）。
    /// 幂等（已存在＝无操作、返回 false——含不同参值：参值变更一律走 <see cref="SetValue"/>）；
    /// 装载链内失败＝fail-fast（逆序整体回滚：存在性回 false、零残留、无半态；异常上抛）。
    /// </summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">对已死亡卡授予（终态拒绝）；或标识未注册/组件构造失败（fail-fast）。</exception>
    public Task<bool> GrantAsync(string keyword, int? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (IsDead())
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 已死亡（终态）：词条授予被拒绝（查询面保持可用）。");
        }

        var context = LoadContextProvider?.Invoke();
        return Task.FromResult(GrantCore(keyword, value, context));
    }

    /// <summary>
    /// 授予（携带内容载荷形态；A4 加性——「获得亡计」的承载）：与标识授予（<see cref="GrantAsync(string, int?)"/>）同一机制——
    /// 存在性置位 → 运行逻辑装载＋内嵌效果装载 → OnGrant；幂等/回滚/终态语义一致（内容生命周期随词条组件生灭：
    /// 授予＝内容装载；移除/死亡注销＝内容卸载；授予失败回滚＝无残留）。
    /// 内容载荷在组件创建后、装载遍历前经组件「内容装载点」（<see cref="KeywordComponent.AttachContent"/>）注入
    /// （内容型词条覆写吸收；非内容型词条＝忽略——内容不进入承载）；空内容（null）＝合法
    /// （空内容授予——结算时无操作、静默跳过）。参值位不随本形态携带（内容型词条的参值位无语义）。
    /// </summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">对已死亡卡授予（终态拒绝）；或标识未注册/组件构造失败（fail-fast）。</exception>
    public Task<bool> GrantWithContentAsync(string keyword, Effect? content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (IsDead())
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 已死亡（终态）：词条授予被拒绝（查询面保持可用）。");
        }

        var context = LoadContextProvider?.Invoke();
        return Task.FromResult(GrantCore(keyword, value: null, context, content));
    }

    /// <summary>
    /// 移除（完整卸载；可等待完成）：①OnRevoke（行为面先撤）→ ②运行逻辑注销＋内嵌效果卸载 → ③存在性清除（参值不可读）。
    /// 幂等（不存在＝无操作、返回 false、不报错）；对已死亡卡＝拒绝（明确异常）。
    /// </summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">对已死亡卡移除（终态拒绝）。</exception>
    public Task<bool> RevokeAsync(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (IsDead())
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 已死亡（终态）：词条移除被拒绝（查询面保持可用）。");
        }

        return Task.FromResult(RevokeCore(keyword));
    }

    /// <summary>
    /// 参值改写（统一写口；纯存储改写——A1 不重载行为面）：前提＝词条存在；允许置空参值（参值位可空、与声明面一致）。
    /// 对已死亡卡＝拒绝（明确异常）。
    /// </summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">对已死亡卡改写（终态拒绝）；或词条不存在（更新型操作的目标缺失——明确异常、
    /// 与「授予/移除＝状态确保型幂等」形成清晰对比）。</exception>
    public void SetValue(string keyword, int? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (IsDead())
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 已死亡（终态）：词条参值改写被拒绝（查询面保持可用）。");
        }

        if (!_components.TryGetValue(keyword, out var component))
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 无词条 '{keyword}' 可改写（参值改写前提＝词条存在；不存在＝明确异常）。");
        }

        component.OverrideValue(value);
    }

    // ---------- 清空处置面（A2 加性：抑制＝清空处置的「清空词条」承载） ----------

    /// <summary>
    /// 清空词条（「除保留项外全部」——抑制清空处置的第一件套；A2 加性）：
    /// 除 <paramref name="preserveKeyword"/>（功能保留位——保住「被抑制」自身存续）外，对全部登记词条
    /// 逐一走移除路径（完整卸载：OnRevoke → 运行逻辑注销＋内嵌效果卸载 → 存在性清除）；
    /// 含其它施加物（被压制）与防护标记（无法被压制/无法被抑制）——字面全量、无保护名单（Q&A-11）。
    /// 逐条异常隔离（记录、继续完成——「卸载力求完成」先例）；返回实际清除数。
    /// 对已死亡卡＝拒绝（终态；死亡注销已完成行为撤销）。
    /// </summary>
    /// <exception cref="ArgumentException">preserveKeyword 为 null/空白。</exception>
    /// <exception cref="InvalidOperationException">对已死亡卡清空（终态拒绝）。</exception>
    public Task<int> ClearAllExceptAsync(string preserveKeyword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preserveKeyword);
        if (IsDead())
        {
            throw new InvalidOperationException(
                $"卡牌 '{_card.Name}' 已死亡（终态）：清空词条被拒绝（查询面保持可用）。");
        }

        var toRevoke = new List<string>();
        foreach (var keyword in _components.Keys)
        {
            if (!string.Equals(keyword, preserveKeyword, StringComparison.Ordinal))
            {
                toRevoke.Add(keyword);
            }
        }

        var cleared = 0;
        foreach (var keyword in toRevoke) // 快照遍历（RevokeCore 修改注册表）
        {
            try
            {
                if (RevokeCore(keyword))
                {
                    cleared++;
                }
            }
            catch (Exception ex)
            {
                WriteError("词条清空", $"词条 '{keyword}' 移除异常（隔离：继续完成清空）", ex);
            }
        }

        return Task.FromResult(cleared);
    }

    // ---------- 装载链核心（加载期固有词条与运行时动态授予同一机制） ----------

    /// <summary>
    /// 授予链核心：①存在性置位（数据面可见）→ ②运行逻辑装载＋内嵌效果装载 → ③OnGrant（最后）。
    /// 装载链内失败（构造/挂载/回调抛）＝fail-fast：逆序整体回滚（②→①；存在性回 false、零残留）、异常上抛。
    /// 已存在＝幂等无操作（false）；不做死亡校验（公开面已校验、加载期为未死亡卡）。
    /// A4 加性：<paramref name="content"/>＝内容载荷（携带内容形态的授予；组件创建后、装载遍历前经内容装载点注入——
    /// 注入失败＝未置位、无状态变更）。
    /// </summary>
    internal bool GrantCore(string keyword, int? value, KeywordLoadContext? context, Effect? content = null)
    {
        if (_components.ContainsKey(keyword))
        {
            return false; // 幂等：重复授予（含不同参值）＝无操作、参值保持
        }

        var component = KeywordRegistry.Create(_card, keyword, value); // 构造失败＝fail-fast（未置位、无状态变更）
        component.AttachContent(content); // A4：内容载荷注入（内容型词条吸收；默认忽略）——失败＝fail-fast（未置位、无状态变更）
        _components.Add(keyword, component);
        _active.Add(component); // ① 存在性置位（数据面可见）

        var attached = new List<Effect>();
        try
        {
            component.Mount(_card, context); // ② 运行逻辑装载（如伏击注册改写；缺上下文＝组件内防御跳过）

            foreach (var effect in component.EmbeddedEffects)
            {
                AttachEmbeddedEffect(component, effect, attached); // ② 内嵌效果装载（完整装载链语义）
            }

            component.OnGrant(); // ③ 授予回调（最后）
        }
        catch (Exception)
        {
            RollbackGrant(component, attached);
            throw;
        }

        return true;
    }

    /// <summary>
    /// 移除链核心：①OnRevoke（作者回调、最先——其异常＝操作失败上抛、机制面未变更、无半态）→
    /// ②运行逻辑注销＋内嵌效果卸载（逆序；异常隔离记录、不阻断后续——对齐效果体系卸载链先例「卸载力求完成」）
    /// → ③存在性清除（参值不可读）。不存在＝幂等无操作（false）。
    /// </summary>
    internal bool RevokeCore(string keyword)
    {
        if (!_components.TryGetValue(keyword, out var component))
        {
            return false; // 幂等：卸载不存在词条不报错
        }

        component.OnRevoke(); // ① 行为面先撤（上抛＝操作失败；机制面未变更）

        try
        {
            component.Unmount(); // ② 运行逻辑注销
        }
        catch (Exception ex)
        {
            WriteError("词条移除", "运行逻辑注销异常（隔离：继续完成卸载）", ex);
        }

        var effects = component.EmbeddedEffects;
        for (var i = effects.Count - 1; i >= 0; i--)
        {
            _card.RemoveEffect(effects[i]); // ② 内嵌效果卸载（逆序；统一卸载链含托管清理——无残留）
        }

        _components.Remove(keyword); // ③ 存在性清除
        _active.Remove(component);
        return true;
    }

    /// <summary>
    /// 死亡注销（仅行为撤销——存在性保留、参值照常可读）：对全部行为态词条（后进先出）执行
    /// OnRevoke → 运行逻辑注销＋内嵌效果卸载；不执行「存在性清除」步。重复调用＝无操作（幂等）。
    /// 回调/注销异常＝隔离记录（不阻断死亡清理链——死亡流程健壮性优先；「清理未完成，以实况计」）。
    /// </summary>
    internal void RevokeOnDeath()
    {
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var component = _active[i];
            try
            {
                component.OnRevoke();
            }
            catch (Exception ex)
            {
                WriteError("词条死亡注销", "OnRevoke 异常（隔离：继续完成注销）", ex);
            }

            try
            {
                component.Unmount();
            }
            catch (Exception ex)
            {
                WriteError("词条死亡注销", "运行逻辑注销异常（隔离：继续完成注销）", ex);
            }

            var effects = component.EmbeddedEffects;
            for (var j = effects.Count - 1; j >= 0; j--)
            {
                _card.RemoveEffect(effects[j]); // 内嵌效果卸载（无残留）
            }
        }

        _active.Clear(); // 行为态清空（存在性/参值保留——登记保留语义；重复注销＝无操作）
    }

    // ---------- 内部辅助 ----------

    /// <summary>
    /// 内嵌效果装载（复用 Effect 体系完整链语义）：登记入卡（列表序；Add 即装载——W3-A3）→ 装载兜底
    /// （幂等：已装载跳过；未装载者经装载面补齐）→ 装载失败＝fail-fast（上抛——词条授予链据此整体回滚）；
    /// 托管登记（卸载时框架按来源撤销修饰器/光环）自 W3-A3 起随「通用装载路径」自动完成——本链不再手动登记。
    /// </summary>
    private void AttachEmbeddedEffect(KeywordComponent component, Effect effect, List<Effect> attached)
    {
        _card.AddEffect(effect);
        attached.Add(effect); // 先记录：其后任何失败回滚均须将其撤离（未装载＝仅容器移除）

        if (effect.Kind != TriggerKind.Passive)
        {
            return; // 主动效果不参与装载/卸载（仅列表进出；施放经 CastAsync——与外部主动效果同待遇）
        }

        if (!effect.IsMounted)
        {
            _card.MountPassiveEffects(); // 装载兜底（幂等：已装载者跳过）
        }

        if (!effect.IsMounted)
        {
            throw new InvalidOperationException(
                $"词条 '{component.Keyword}' 的内嵌效果 '{effect.Name}' 装载失败（装载链内失败——词条授予整体回滚）。");
        }
    }

    /// <summary>授予链整体回滚（逆序：②内嵌效果 → 运行逻辑 → ①存在性）；回滚动作力求完成（异常隔离记录——不掩盖原始失败）。</summary>
    private void RollbackGrant(KeywordComponent component, List<Effect> attached)
    {
        for (var i = attached.Count - 1; i >= 0; i--)
        {
            _card.RemoveEffectSilently(attached[i]); // 静默清理路径：「无痕」回滚——不发 effect.removed（W3-A3；已装载＝统一卸载链、未装载＝容器移除，幂等、不抛）
        }

        try
        {
            component.Unmount();
        }
        catch (Exception ex)
        {
            WriteError("词条授予回滚", "运行逻辑注销异常（隔离）", ex);
        }

        _components.Remove(component.Keyword); // ① 存在性回 false
        _active.Remove(component);
    }

    /// <summary>已死亡判定（终态）：单位卡且 IsDestroyed 置位（非单位卡无死亡语义——恒 false）。</summary>
    private bool IsDead()
        => _card is UnitCard unit
            && unit.TryGetData<UnitStateData>(out var state)
            && state.IsDestroyed;

    /// <summary>写词条链错误记录（引擎总流；与既有装载留痕同渠道）。</summary>
    private void WriteError(string source, string message, Exception ex)
    {
        _engine.RootStream.WriteLog(
            source,
            $"{message}：{ex.Message}",
            LogLevel.Error,
            new[] { "keyword", "error", $"exception:{ex.GetType().Name}" });
    }
}
