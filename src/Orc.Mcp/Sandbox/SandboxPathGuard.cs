namespace Orc.Mcp.Sandbox;

/// <summary>
/// 沙盒路径违规（映射为 <see cref="Contracts.McpCodes.PathDenied"/>）。
/// </summary>
public sealed class SandboxPathViolationException : Exception
{
    public SandboxPathViolationException(string attemptedPath, string reason)
        : base($"路径被拒绝：{reason}（输入：'{attemptedPath}'）。")
    {
        AttemptedPath = attemptedPath;
        Reason = reason;
    }

    /// <summary>被拒绝的原始输入。</summary>
    public string AttemptedPath { get; }

    /// <summary>拒绝原因。</summary>
    public string Reason { get; }
}

/// <summary>
/// 沙盒路径守卫：把"相对沙盒根的路径"解析为规范化绝对路径，并保证结果**落在根内**。
/// <para>
/// 全防要点（默认策略，无开关）：
/// ①拒绝空白路径；②拒绝 UNC 路径（<c>\\server\share</c> / <c>//server/share</c>）；
/// ③拒绝绝对路径（一律只接受根内相对路径）；④拒绝 <c>..</c> 穿越（经规范化后复核，而非仅文本匹配）；
/// ⑤拒绝经由**符号链接/重解析点**逃逸出根；⑥Windows 下按大小写不敏感比较；
/// ⑦写前对**规范化后的最终路径**复核（防"校验后拼接"类绕过）。
/// </para>
/// </summary>
public sealed class SandboxPathGuard
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _root;

    public SandboxPathGuard(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Normalize(Path.GetFullPath(root));
    }

    /// <summary>沙盒根（规范化绝对路径；含尾分隔符）。</summary>
    public string Root => _root;

    /// <summary>解析根内相对路径为规范化绝对路径（违规＝抛 <see cref="SandboxPathViolationException"/>）。</summary>
    public string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new SandboxPathViolationException(relativePath ?? string.Empty, "路径为空");
        }

        var input = relativePath.Trim();

        if (input.StartsWith(@"\\", StringComparison.Ordinal) || input.StartsWith("//", StringComparison.Ordinal))
        {
            throw new SandboxPathViolationException(relativePath, "不允许 UNC 路径");
        }

        if (Path.IsPathRooted(input))
        {
            throw new SandboxPathViolationException(relativePath, "不允许绝对路径（只接受沙盒根内的相对路径）");
        }

        string full;
        try
        {
            full = Normalize(Path.GetFullPath(Path.Combine(_root, input)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SandboxPathViolationException(relativePath, $"路径无法解析（{ex.GetType().Name}）");
        }

        if (!IsUnder(full, _root))
        {
            throw new SandboxPathViolationException(relativePath, "路径越出沙盒根");
        }

        EnsureNoReparsePointEscape(full);

        return full.Equals(_root, PathComparison) ? _root : full.TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>尝试解析（失败＝false，不抛错）。</summary>
    public bool TryResolve(string relativePath, out string resolved, out string? error)
    {
        try
        {
            resolved = Resolve(relativePath);
            error = null;
            return true;
        }
        catch (SandboxPathViolationException ex)
        {
            resolved = string.Empty;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>解析**写**目标（在 <see cref="Resolve"/> 之上再复核一次规范化结果；写前必须调用）。</summary>
    public string ResolveWrite(string relativePath)
    {
        var full = Resolve(relativePath);

        // 写前复核：最终路径再次判归属（防"校验后拼接"类绕过）。
        if (!IsUnder(full, _root))
        {
            throw new SandboxPathViolationException(relativePath, "写目标越出沙盒根");
        }

        return full;
    }

    private static string Normalize(string fullPath)
    {
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed + Path.DirectorySeparatorChar;
    }

    private static bool IsUnder(string fullPath, string rootWithSeparator)
        => fullPath.Equals(rootWithSeparator.TrimEnd(Path.DirectorySeparatorChar), PathComparison)
           || fullPath.StartsWith(rootWithSeparator, PathComparison);

    /// <summary>逐级检查既有路径段是否为重解析点（符号链接/junction），防逃逸。</summary>
    private static void EnsureNoReparsePointEscape(string fullPath)
    {
        var current = new DirectoryInfo(fullPath);

        // 文件路径：先看自身，再看各级父目录。
        while (current is not null)
        {
            try
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new SandboxPathViolationException(fullPath, $"路径经过重解析点（符号链接/junction）：'{current.FullName}'");
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限读取属性：交给后续 IO 操作报错（不静默放行也不误判）。
            }

            current = current.Parent;
        }
    }
}
