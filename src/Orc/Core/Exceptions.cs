namespace Orc.Core;

/// <summary>访问已失效引用时抛出（访问当刻、即抛）。</summary>
public class StaleReferenceException : Exception
{
    public StaleReferenceException(string message)
        : base(message)
    {
    }
}

/// <summary>视图权限矩阵拒绝访问时抛出（访问当刻、即抛）。</summary>
public class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string message)
        : base(message)
    {
    }
}
