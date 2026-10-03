namespace Orc.Lua;

/// <summary>
/// 结构化错误分类（第一版冻结的六类）。
/// </summary>
public enum LuaErrorKind
{
    /// <summary>脚本编译/语法错误（源初始化阶段暴露）。</summary>
    SyntaxError,

    /// <summary>脚本运行时错误（源顶层或 handler 调用阶段的未捕获错误）。</summary>
    RuntimeError,

    /// <summary>缺失函数（获取阶段：名字不存在、命中非函数、执行结果非函数）。</summary>
    MissingFunction,

    /// <summary>编组错误（跨边界值转换失败：参数入编组、返回值出编组、宿主回调双向编组）。</summary>
    MarshallingError,

    /// <summary>白名单拒绝（白名单域未注册成员的访问、或对域的任何写入）。</summary>
    WhitelistRejected,

    /// <summary>沙箱限制触发（禁用面名字的访问、string.dump 成员访问）。</summary>
    SandboxRestriction,
}
