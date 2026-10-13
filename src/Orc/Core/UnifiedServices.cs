namespace Orc.Core;

// ─────────────────────────────────────────────────────────────────────────────
// 统一服务机制（资产化 S2）：判定器与资产服务**共用**的"条目 + moding 栈 + 句柄"机制基类。
// 背景：判定器（Judicator：「可被改写」的逻辑服务）与资产服务（AssetService：静态资产的数据/逻辑
// 储存服务）语义分立、**名字域独立**（判定器名＝受控常量集；资产名＝插件命名），但机制同构——
// 本文件把该机制抽出为泛型基类，二者各具其具体化实例，避免机制复制。
// 语义（与既有 JudicatorRegistry 对齐）：注册即配置（装配期一次成型、重复注册拒绝、未注册引用 fail-fast）；
// moding＝栈语义（最后者胜、注销回退、纯替换）；句柄即权限（无注册者校验）；快照一致（一次调用＝一次解析）；
// 机制静默（moding 面无读面、注册/注销/解析不留痕）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 统一服务面（判定器/资产服务共用）：载荷进、结果出（无类型统一签名）。
/// </summary>
public interface IUnifiedService
{
    /// <summary>统一主方法（调用侧唯一入口）；载荷/结果为调用方约定序列，可为 null。</summary>
    object[]? Invoke(object[]? args);
}

/// <summary>
/// 强类型 moding 适配面：服务子类声明强类型 delegate 并实现本面时，moding 可经强类型 delegate 注入
/// （机制侧调用 <see cref="Adapt"/> 适配为统一签名后入栈——与统一签名底层等效替换，同一条目同一栈）。
/// </summary>
/// <typeparam name="TDelegate">服务子类声明的强类型 delegate（该服务的签名）。</typeparam>
public interface IStrongTypedService<in TDelegate> where TDelegate : Delegate
{
    /// <summary>把强类型 delegate 适配为统一签名；适配错误＝fail-fast 族。</summary>
    Func<object[]?, object[]?> Adapt(TDelegate handler);
}

/// <summary>
/// 服务条目：默认服务 ＋ moding 栈。栈序＝moding 注册序（尾＝最后注册者）；执行时点解析（不做注册期换绑）。
/// 条目生命周期＝注册表生命周期（无条目注销通道——「注册即配置」）。
/// </summary>
/// <typeparam name="TService">服务类型。</typeparam>
public sealed class ServiceEntry<TService> where TService : class, IUnifiedService
{
    private readonly List<ModingEntry> _modings = new();

    internal ServiceEntry(object owner, string name, TService service)
    {
        Owner = owner;
        Name = name;
        Service = service;
    }

    /// <summary>所属注册表（跨注册表句柄判定用）。</summary>
    internal object Owner { get; }

    /// <summary>条目名（服务名）。</summary>
    public string Name { get; }

    /// <summary>默认服务实例。</summary>
    public TService Service { get; }

    /// <summary>当前生效的 moding 逻辑：最后注册且未注销者；无＝null（回退默认）。机制静默：无公共读面。</summary>
    internal Func<object[]?, object[]?>? LastModing => _modings.Count == 0 ? null : _modings[^1].Handler;

    internal void AddModing(Func<object[]?, object[]?> handler, long seq) => _modings.Add(new ModingEntry(handler, seq));

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
    /// 统一解析点：每次调用解析栈顶一次并固定（moding 生效＝纯替换；无 moding＝默认服务主方法）。
    /// 异常对称性：不捕获、不包装——默认与替换逻辑异常可见性一致。
    /// </summary>
    public object[]? Invoke(object[]? args)
    {
        var moding = LastModing;
        return moding is null ? Service.Invoke(args) : moding(args);
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

/// <summary>服务条目等价句柄基类（注册所得＝解析所得——同一可寻址锚）。仅由注册面创建；不含可变状态。</summary>
public class ServiceRegistration<TService> where TService : class, IUnifiedService
{
    internal ServiceRegistration(ServiceEntry<TService> entry)
    {
        Entry = entry;
    }

    /// <summary>服务名（可读标识）。</summary>
    public string Name => Entry.Name;

    /// <summary>条目内部件（机制内部使用）。</summary>
    internal ServiceEntry<TService> Entry { get; }

    /// <summary>调用（持有句柄的调用；经统一解析点）。</summary>
    public object[]? Invoke(object[]? args) => Entry.Invoke(args);
}

/// <summary>moding 注册句柄基类（注销凭据；句柄即权限）。仅由注册面创建；不含可变状态。</summary>
public class ServiceModingRegistration<TService> where TService : class, IUnifiedService
{
    internal ServiceModingRegistration(ServiceEntry<TService> target, long modingSeq)
    {
        Target = target;
        ModingSeq = modingSeq;
    }

    /// <summary>目标条目（机制内部使用）。</summary>
    internal ServiceEntry<TService> Target { get; }

    /// <summary>moding 注册序（注册表内唯一；机制内部使用）。</summary>
    internal long ModingSeq { get; }
}

/// <summary>
/// 通用服务注册表基类（机制）：字符串名（ordinal）→ 条目（默认服务 ＋ moding 栈）。
/// 语义见文件头；具体注册表（<c>JudicatorRegistry</c> / <c>AssetServiceRegistry</c>）经本类具体化，
/// 各自持有独立实例（名字域隔离），并各自声明其句柄类型。
/// </summary>
/// <typeparam name="TService">服务类型。</typeparam>
/// <typeparam name="THandle">条目句柄类型（派生自 <see cref="ServiceRegistration{TService}"/>）。</typeparam>
/// <typeparam name="TModingHandle">moding 句柄类型（派生自 <see cref="ServiceModingRegistration{TService}"/>）。</typeparam>
public abstract class ServiceRegistry<TService, THandle, TModingHandle>
    where TService : class, IUnifiedService
    where THandle : ServiceRegistration<TService>
    where TModingHandle : ServiceModingRegistration<TService>
{
    private readonly Dictionary<string, ServiceEntry<TService>> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<ServiceEntry<TService>, THandle> _handles = new();
    private long _modingSeq;

    /// <summary>创建条目句柄（派生类提供；每条目恰一次——注册所得＝解析所得）。</summary>
    protected abstract THandle CreateHandle(ServiceEntry<TService> entry);

    /// <summary>创建 moding 句柄（派生类提供）。</summary>
    protected abstract TModingHandle CreateModingHandle(ServiceEntry<TService> entry, long modingSeq);

    /// <summary>注册一枚服务条目（注册即配置——重复注册拒绝）。返回条目等价句柄。</summary>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">service 为 null。</exception>
    /// <exception cref="InvalidOperationException">同名条目重复注册。</exception>
    protected THandle RegisterCore(string name, TService service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(service);

        if (_entries.ContainsKey(name))
        {
            throw new InvalidOperationException($"服务名 '{name}' 已注册（重复注册被拒绝——注册即配置）。");
        }

        var entry = new ServiceEntry<TService>(this, name, service);
        var handle = CreateHandle(entry);
        _entries.Add(name, entry);
        _handles.Add(entry, handle);
        return handle;
    }

    /// <summary>按名解析条目句柄（与注册所得为同一实例）。</summary>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    /// <exception cref="KeyNotFoundException">未注册引用（配置错误——fail-fast）。</exception>
    protected THandle ResolveCore(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_entries.TryGetValue(name, out var entry))
        {
            throw new KeyNotFoundException($"服务 '{name}' 未注册（未注册引用＝配置错误——明确失败）。");
        }

        return _handles[entry];
    }

    /// <summary>条目是否存在（诊断面）。</summary>
    public bool Contains(string name)
        => !string.IsNullOrWhiteSpace(name) && _entries.ContainsKey(name);

    /// <summary>按名调用（解析栈顶并执行；一次调用＝一次解析）。</summary>
    public object[]? Invoke(string name, object[]? args) => ResolveCore(name).Invoke(args);

    /// <summary>
    /// 注册一条 moding（统一签名底层形态）：纯替换（moding 生效时不执行默认逻辑）。
    /// 无效目标（句柄不属本注册表）＝幂等无操作、不生效（返回 null、不抛错）。
    /// </summary>
    /// <exception cref="ArgumentNullException">target 或 moding 为 null。</exception>
    public TModingHandle? RegisterModing(THandle target, Func<object[]?, object[]?> moding)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(moding);

        if (!ReferenceEquals(target.Entry.Owner, this))
        {
            return null; // 跨注册表句柄：幂等无操作
        }

        target.Entry.AddModing(moding, _modingSeq++);
        return CreateModingHandle(target.Entry, _modingSeq - 1);
    }

    /// <summary>
    /// 注册一条 moding（强类型封装便捷面）：目标服务实现 <see cref="IStrongTypedService{TDelegate}"/> 时经其适配；
    /// 恰为统一签名 delegate＝直接采用；否则＝适配错误（fail-fast）。
    /// </summary>
    /// <exception cref="ArgumentNullException">target 或 moding 为 null。</exception>
    /// <exception cref="InvalidOperationException">目标服务不支持该强类型 delegate（适配错误——fail-fast）。</exception>
    public TModingHandle? RegisterModing<TDelegate>(THandle target, TDelegate moding)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(moding);

        if (!ReferenceEquals(target.Entry.Owner, this))
        {
            return null;
        }

        Func<object[]?, object[]?> unified;
        if (target.Entry.Service is IStrongTypedService<TDelegate> typed)
        {
            unified = typed.Adapt(moding);
        }
        else if (moding is Func<object[]?, object[]?> direct)
        {
            unified = direct; // 统一签名恒受支持
        }
        else
        {
            throw new InvalidOperationException(
                $"服务 '{target.Name}' 不支持强类型 delegate '{typeof(TDelegate).Name}' 的注入（适配错误——fail-fast）。");
        }

        target.Entry.AddModing(unified, _modingSeq++);
        return CreateModingHandle(target.Entry, _modingSeq - 1);
    }

    /// <summary>注销一条 moding（句柄即权限）。句柄不属本注册表/已注销＝幂等无操作、返回 false。</summary>
    /// <exception cref="ArgumentNullException">registration 为 null。</exception>
    public bool UnregisterModing(TModingHandle registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!ReferenceEquals(registration.Target.Owner, this))
        {
            return false;
        }

        return registration.Target.RemoveModing(registration.ModingSeq);
    }
}
