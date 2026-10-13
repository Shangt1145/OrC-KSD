using Orc.Game.Plugins;

namespace Orc.Packages;

/// <summary>卡包生命周期变更类别（S7·P10b）。</summary>
public enum PackageLifecycleChange
{
    /// <summary>热加载新包成功（只对新建对局生效）。</summary>
    Loaded,

    /// <summary>卸载包成功。</summary>
    Unloaded,

    /// <summary>重载包成功（整包替换）。</summary>
    Reloaded,
}

/// <summary>
/// 包生命周期观察者（S7·P10b；观测者接口居宿主工程——进程级、只与宿主打交道）。
/// 宿主在**成功**的热加载/卸载/重载之后回调（失败＝无状态变更、不回调——调用方无需清理）。
/// </summary>
public interface IPackageLifecycleObserver
{
    /// <summary>包状态已变更（回调须快速、不阻塞；异常被宿主隔离并计入装载失败明细）。</summary>
    /// <param name="change">变更类别。</param>
    /// <param name="package">已装载包（含 <see cref="LoadedPackage.RootDirectory"/> 与清单）。</param>
    void OnPackageChanged(PackageLifecycleChange change, LoadedPackage package);
}

/// <summary>生命周期观察注册面。</summary>
public interface IPackageLifecycleServices
{
    /// <summary>注册观察者（可多个，按注册序回调）。</summary>
    /// <exception cref="ArgumentNullException">observer 为 null。</exception>
    void RegisterObserver(IPackageLifecycleObserver observer);
}

/// <summary>进程级插件上下文扩展（S7）：在 <see cref="IGlobalPluginContext"/> 之上暴露生命周期观察注册。</summary>
public interface IPackageLifecycleContext : IGlobalPluginContext
{
    /// <summary>生命周期观察注册面（宿主未提供＝null）。</summary>
    IPackageLifecycleServices? Lifecycle { get; }
}

/// <summary>生命周期通知器（宿主持有；进程级）。</summary>
public sealed class PackageLifecycleNotifier : IPackageLifecycleServices
{
    private readonly List<IPackageLifecycleObserver> _observers = [];

    /// <summary>已注册观察者（注册序）。</summary>
    public IReadOnlyList<IPackageLifecycleObserver> Observers => _observers;

    /// <inheritdoc />
    public void RegisterObserver(IPackageLifecycleObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
    }

    /// <summary>回调全部观察者；逐个隔离异常（返回失败明细，由宿主并入装载失败记录）。</summary>
    public IReadOnlyList<string> Notify(PackageLifecycleChange change, LoadedPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var failures = new List<string>();
        foreach (var observer in _observers.ToArray())
        {
            try
            {
                observer.OnPackageChanged(change, package);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add($"{package.Manifest.Id}@{package.Manifest.Version}（{change} 通知）：{ex.Message}");
            }
        }

        return failures;
    }
}
