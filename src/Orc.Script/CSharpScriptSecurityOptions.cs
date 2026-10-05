namespace Orc.Script;

/// <summary>
/// 脚本安全选项（S-C6；等价于 SecureCSharpEval 的「白/黑名单 + 关键字过滤 + 超时」模型）：
/// 允许导入、允许引用的程序集、关键字屏蔽、超时。
/// 定位：**尽力而为**的弱沙箱（同进程、非进程隔离）——只提升审查/透明性，不构成安全边界；
/// 真正的强隔离须进程级（独立进程 + OS 资源限制 + 禁网 + 最小权限）。
/// </summary>
public sealed class CSharpScriptSecurityOptions
{
    /// <summary>默认允许的程序集简单名（引用白名单；实际取自已加载程序集）。</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedAssemblies = new[]
    {
        "System.Private.CoreLib",
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Text",
        "System.Threading.Tasks",
        "Orc",
    };

    /// <summary>默认允许的命名空间导入。</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedImports = new[]
    {
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text",
        "System.Threading",
        "System.Threading.Tasks",
        "Orc.Core",
        "Orc.Cards",
    };

    /// <summary>默认屏蔽的关键字/片段（文本级过滤；可被绕过，故仅作尽力而为）。</summary>
    public static readonly IReadOnlyList<string> DefaultBlockedKeywords = new[]
    {
        "unsafe",
        "fixed",
        "stackalloc",
        "Process",
        "File",
        "Directory",
        "Registry",
        "Socket",
        "WebClient",
        "HttpClient",
        "System.IO",
        "System.Net",
        "System.Reflection",
        "System.Diagnostics",
        "Assembly",
        "Activator",
        "DllImport",
        "Environment",
        "#load",
        "#r ",
    };

    /// <summary>允许导入的命名空间（空＝用默认）。</summary>
    public IReadOnlyList<string> AllowedImports { get; init; } = DefaultAllowedImports;

    /// <summary>允许引用的程序集简单名（空＝用默认）。</summary>
    public IReadOnlyList<string> AllowedAssemblies { get; init; } = DefaultAllowedAssemblies;

    /// <summary>屏蔽的关键字/片段（空＝用默认）。</summary>
    public IReadOnlyList<string> BlockedKeywords { get; init; } = DefaultBlockedKeywords;

    /// <summary>默认执行超时（毫秒）；请求可覆盖。</summary>
    public int TimeoutMs { get; init; } = 5000;
}
