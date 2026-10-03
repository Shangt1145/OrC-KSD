namespace Orc.Lua;

/// <summary>
/// <see cref="LuaHandlerEngine"/> 的构造选项。
/// </summary>
public sealed class LuaHandlerEngineOptions
{
    /// <summary>默认根命名空间名（<c>"orc"</c>）。</summary>
    public const string DefaultRootNamespace = "orc";

    /// <summary>
    /// 白名单域（动词命名空间）的根名，Lua 侧访问形如 <c>{RootNamespace}.verb</c>。
    /// 默认 <c>"orc"</c>。须为合法 Lua 标识符，且不得与保留面/禁用面名字冲突。
    /// </summary>
    public string RootNamespace { get; set; } = DefaultRootNamespace;
}
