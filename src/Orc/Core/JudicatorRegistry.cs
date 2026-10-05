namespace Orc.Core;

/// <summary>
/// 判定器注册表（J1 机制骨架；对局级）：字符串名（ordinal）→ 判定器条目的注册表——
/// 「注册即配置」（装配期一次成型；重复注册拒绝）、未注册引用 fail-fast（按名解析/调用时明确失败、不设宽容旁路）；
/// moding（逻辑替换）注入面（双形态：统一签名底层＋强类型封装便捷面）与统一解析点（全局生效）；
/// 判定器条目自带等价句柄产物（注册所得＝解析所得——同一可寻址锚）。
/// 语义要素与 X4 handler moding 一致：栈语义（最后者胜、注销回退）、快照一致（一次调用＝一次解析）、
/// 幂等宽容（句柄面：跨注册表目标＝无操作不抛错、重复注销＝false）、句柄即权限（无注册者校验）、
/// 机制静默（moding 面无读面、注册/注销/解析不留痕）。
/// 无状态核验点：注册表仅持有装配期配置（条目）与机制状态（moding 栈）；判定器实例不持有对局状态。
/// 并发口径：条目注册单线程假设（装配期）；同步调用不承诺并发安全、快照＝同步语境一致性语义。
/// </summary>
public sealed class JudicatorRegistry
{
    private readonly Dictionary<string, JudicatorEntry> _entries = new(StringComparer.Ordinal);
    private long _modingSeq;

    /// <summary>
    /// 注册一枚判定器条目（字符串名 ordinal；注册即配置——重复注册拒绝）。返回条目等价句柄
    /// （与按名解析所得为同一可寻址锚——注册方自持句柄、按名解析两通道等效）。
    /// </summary>
    /// <param name="name">判定器名（受控常量集合为唯一称谓来源；非 null、非空白）。</param>
    /// <param name="judicator">判定器实例（无状态服务）。</param>
    /// <returns>条目等价句柄（供持用：调用/moding 锚定）。</returns>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">judicator 为 null。</exception>
    /// <exception cref="InvalidOperationException">同名条目重复注册（被拒绝——注册即配置）。</exception>
    public JudicatorRegistration Register(string name, Judicator judicator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(judicator);

        if (_entries.ContainsKey(name))
        {
            throw new InvalidOperationException($"判定器名 '{name}' 已注册（重复注册被拒绝——注册即配置）。");
        }

        var entry = new JudicatorEntry(this, name, judicator);
        _entries.Add(name, entry);
        return entry.Registration;
    }

    /// <summary>
    /// 按名解析条目（字符串名 ordinal、逐字敏感）。返回条目等价句柄（与注册所得为同一可寻址锚——
    /// 「解析所得」与「注册所得」可等效用于 moding，杜绝解析口与 moding 口错位）。
    /// </summary>
    /// <param name="name">判定器名。</param>
    /// <returns>条目等价句柄。</returns>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="KeyNotFoundException">未注册引用（配置错误——fail-fast、不设宽容旁路）。</exception>
    public JudicatorRegistration Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_entries.TryGetValue(name, out var entry))
        {
            throw new KeyNotFoundException($"判定器 '{name}' 未注册（未注册引用＝配置错误——明确失败）。");
        }

        return entry.Registration;
    }

    /// <summary>
    /// 按名调用（引用面入口）：解析栈顶（moding 生效时＝最后注册且未注销的替换逻辑；无 moding＝默认逻辑）并执行。
    /// 一次调用＝一次解析（快照语义）：解析后即固定；调用期间的重入增删自下一次调用生效。
    /// </summary>
    /// <param name="name">判定器名。</param>
    /// <param name="args">载荷（可为 null）。</param>
    /// <returns>调用结果（可为 null）。</returns>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="KeyNotFoundException">未注册引用（fail-fast）。</exception>
    public object[]? Invoke(string name, object[]? args) => Resolve(name).Invoke(args);

    /// <summary>
    /// 注册一条 moding（逻辑替换）项（统一签名底层形态）：以 <paramref name="target"/> 句柄锚定目标条目，
    /// 之后该条目调用时改用本条 delegate 逻辑（纯替换——moding 生效时默认逻辑不执行；
    /// 机制不提供任何指向默认逻辑的调用途径：默认逻辑不作参数暴露、无 next/proceed 式入口）。
    /// 解析语义：每次调用动态解析「当前最后一个未注销的 moding 项」并采用（栈语义——栈序＝moding 注册序，
    /// 与判定器名无关）；注销后回退上一项，全部注销回退默认逻辑。双形态共用同一条目同一栈（与强类型注入面等效替换）。
    /// 装配期与运行时为同一注册面（仅调用时点区分）。
    /// 无效目标（句柄不属本注册表）＝幂等无操作、不生效（返回 null、不抛错）。
    /// 快照语义：调用期间的增删不打断进行中调用（本次已解析选择固定）；被注销项不再被选用、新增项自下一次调用生效。
    /// </summary>
    /// <param name="target">目标条目句柄（注册所得或按名解析所得——同一可寻址锚）。</param>
    /// <param name="moding">替换逻辑（统一签名；同步）。</param>
    /// <returns>moding 注册句柄（注销凭据）。</returns>
    /// <exception cref="ArgumentNullException">target 或 moding 为 null。</exception>
    public JudicatorModingRegistration? RegisterModing(JudicatorRegistration target, Func<object[]?, object[]?> moding)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(moding);

        if (!ReferenceEquals(target.Entry.Owner, this))
        {
            return null; // 跨注册表句柄：幂等无操作
        }

        var modingSeq = _modingSeq++;
        target.Entry.AddModing(moding, modingSeq);
        return new JudicatorModingRegistration(target.Entry, modingSeq);
    }

    /// <summary>
    /// 注册一条 moding（逻辑替换）项（强类型封装便捷面）：注入者以子类声明的强类型 delegate 注入、无需手工拆包/组包——
    /// 机制经 <see cref="Judicator{TDelegate}.Adapt"/> 适配为统一签名后入栈（与统一签名底层等效替换：
    /// 同一条目、同一栈、同一解析点——不形成第二解析点或分栈）。经本面注入的 moding 同样返回句柄（双形态同凭据语义）。
    /// 适配错误（目标判定器不支持该强类型 delegate）＝fail-fast 族：明确失败、不静默容忍。
    /// 无效目标（句柄不属本注册表）＝幂等无操作、不生效（返回 null、不抛错——与统一签名形态一致）。
    /// </summary>
    /// <typeparam name="TDelegate">子类声明的强类型 delegate 类型。</typeparam>
    /// <param name="target">目标条目句柄（注册所得或按名解析所得——同一可寻址锚）。</param>
    /// <param name="moding">替换逻辑（子类声明的强类型 delegate）。</param>
    /// <returns>moding 注册句柄（注销凭据）。</returns>
    /// <exception cref="ArgumentNullException">target 或 moding 为 null。</exception>
    /// <exception cref="InvalidOperationException">目标判定器不支持该强类型 delegate（适配错误——fail-fast）。</exception>
    public JudicatorModingRegistration? RegisterModing<TDelegate>(JudicatorRegistration target, TDelegate moding)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(moding);

        if (!ReferenceEquals(target.Entry.Owner, this))
        {
            return null; // 跨注册表句柄：幂等无操作
        }

        var unified = AdaptStrongTypedModing(target.Entry, moding);
        var modingSeq = _modingSeq++;
        target.Entry.AddModing(unified, modingSeq);
        return new JudicatorModingRegistration(target.Entry, modingSeq);
    }

    /// <summary>
    /// 注销一条 moding（逻辑替换）项：以句柄为准（任意持有句柄者均可注销，无注册者身份校验——句柄即权限）。
    /// 语义：句柄指向本注册表且对应 moding 项仍存在 → 移除并返回 true（解析随即回退上一项；全部注销＝回退默认逻辑）；
    /// 重复注销/句柄不属本注册表 → 幂等无操作、返回 false、不抛错。
    /// 快照语义：调用期间的注销不打断进行中调用（本次已解析选择固定）；被注销项不再被选用（自下一次调用起）。
    /// </summary>
    /// <param name="registration">moding 注册句柄（注销凭据）。</param>
    /// <returns>是否发生移除（已注销/无效句柄＝false）。</returns>
    /// <exception cref="ArgumentNullException">registration 为 null。</exception>
    public bool UnregisterModing(JudicatorModingRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!ReferenceEquals(registration.Target.Owner, this))
        {
            return false; // 跨注册表句柄：幂等无操作
        }

        return registration.Target.RemoveModing(registration.ModingSeq);
    }

    /// <summary>强类型 delegate → 统一签名 的机制适配：目标判定器为强类型适配型（泛型类型参数匹配）＝经其适配；
    /// 恰为统一签名 delegate ＝直接采用（统一签名恒受支持）；否则＝适配错误（fail-fast）。</summary>
    private static Func<object[]?, object[]?> AdaptStrongTypedModing<TDelegate>(JudicatorEntry entry, TDelegate moding)
        where TDelegate : Delegate
    {
        if (entry.Judicator is Judicator<TDelegate> typed)
        {
            return typed.Adapt(moding);
        }

        if (moding is Func<object[]?, object[]?> unified)
        {
            return unified; // 统一签名恒受支持（双形态等效通道）
        }

        throw new InvalidOperationException(
            $"判定器 '{entry.Name}' 不支持强类型 delegate '{typeof(TDelegate).Name}' 的注入（适配错误——fail-fast）。");
    }
}

/// <summary>
/// 判定器条目（注册表内部件；J1）：持有默认判定器与 moding 栈；统一解析点在 <see cref="Invoke"/>。
/// 栈序＝moding 注册序（尾＝最后注册者）；执行时点解析（LastModing），不做注册期换绑。
/// 条目生命周期＝注册表生命周期（无条目注销通道——「注册即配置」强口径：运行期不提供条目注册通道）。
/// </summary>
internal sealed class JudicatorEntry
{
    private readonly List<ModingEntry> _modings = new();

    internal JudicatorEntry(JudicatorRegistry owner, string name, Judicator judicator)
    {
        Owner = owner;
        Name = name;
        Judicator = judicator;
        Registration = new JudicatorRegistration(this);
    }

    internal JudicatorRegistry Owner { get; }

    internal string Name { get; }

    internal Judicator Judicator { get; }

    /// <summary>条目等价句柄（唯一实例：注册所得＝解析所得）。</summary>
    internal JudicatorRegistration Registration { get; }

    /// <summary>
    /// 当前生效的 moding（逻辑替换）逻辑：最后一个注册且未注销者；无＝null（回退默认逻辑）。
    /// 列表按 moding 注册序追加（尾＝最后注册者）；执行时点解析，不做注册期换绑。机制静默：无公共读面。
    /// </summary>
    internal Func<object[]?, object[]?>? LastModing => _modings.Count == 0 ? null : _modings[^1].Handler;

    internal void AddModing(Func<object[]?, object[]?> handler, long seq)
        => _modings.Add(new ModingEntry(handler, seq));

    /// <summary>按 moding 注册序移除一项；不存在＝false（幂等）。</summary>
    internal bool RemoveModing(long seq)
    {
        var index = _modings.FindIndex(m => m.Seq == seq);
        if (index < 0)
        {
            return false;
        }

        _modings.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// 统一解析点（全局生效）：每次调用解析栈顶一次并固定——moding 生效时＝栈顶替换逻辑（纯替换）；
    /// 无 moding＝默认逻辑主方法。一次调用＝一次解析：调用执行期间（含替换逻辑内部重入增删）不打断本次已解析选择；
    /// 增删自下一次调用生效。异常对称性：不捕获、不包装——默认逻辑与替换逻辑的异常可见性一致（直接传播）。
    /// </summary>
    internal object[]? Invoke(object[]? args)
    {
        var moding = LastModing;
        return moding is null ? Judicator.Invoke(args) : moding(args);
    }

    private sealed class ModingEntry
    {
        internal ModingEntry(Func<object[]?, object[]?> handler, long seq)
        {
            Handler = handler;
            Seq = seq;
        }

        internal Func<object[]?, object[]?> Handler { get; }

        /// <summary>moding 注册序（注册表内唯一，作 moding 项身份）。</summary>
        internal long Seq { get; }
    }
}

/// <summary>
/// 判定器条目等价句柄（J1 可寻址产物）：注册动作返回；按名解析所得为同一实例（同一可寻址锚——
/// 两通道等效，moding 注册/注销经其锚定）。含 <see cref="Invoke"/>（持有句柄的调用——每次调用经统一解析点取栈顶；
/// 引用获取与逻辑固定解耦：拿到句柄不等于锁定逻辑）。
/// 仅由注册面创建；不含可变状态。
/// </summary>
public sealed class JudicatorRegistration
{
    internal JudicatorRegistration(JudicatorEntry entry)
    {
        Entry = entry;
    }

    /// <summary>判定器名（可读标识；注册源与消费引用的称谓来源＝受控常量集合）。</summary>
    public string Name => Entry.Name;

    /// <summary>条目内部件（机制内部使用）。</summary>
    internal JudicatorEntry Entry { get; }

    /// <summary>
    /// 调用（持有句柄的调用；经统一解析点）：每次调用解析栈顶一次并固定（moding 生效＝替换逻辑；无 moding＝默认逻辑）。
    /// 一次调用＝一次解析；调用期间的增删自下一次调用生效。
    /// </summary>
    /// <param name="args">载荷（可为 null）。</param>
    /// <returns>调用结果（可为 null）。</returns>
    public object[]? Invoke(object[]? args) => Entry.Invoke(args);
}

/// <summary>
/// moding（逻辑替换）注册句柄：<see cref="JudicatorRegistry.RegisterModing(JudicatorRegistration, Func{object[], object[]})"/>
/// 成功时返回，标识某条目上的一条 moding 项，可用作 <see cref="JudicatorRegistry.UnregisterModing"/> 的注销依据
/// （句柄即权限——不引入注册者校验/所有权面）。
/// 仅由注册面创建；不含可变状态。
/// </summary>
public sealed class JudicatorModingRegistration
{
    internal JudicatorModingRegistration(JudicatorEntry target, long modingSeq)
    {
        Target = target;
        ModingSeq = modingSeq;
    }

    /// <summary>目标条目（注销/解析时按内部引用匹配；机制内部使用）。</summary>
    internal JudicatorEntry Target { get; }

    /// <summary>moding 注册序（注册表内唯一，作 moding 项身份；机制内部使用）。</summary>
    internal long ModingSeq { get; }
}
