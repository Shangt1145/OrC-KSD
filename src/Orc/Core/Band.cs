namespace Orc.Core;

/// <summary>
/// 框架内置的默认 band 方案（触发器未声明专门枚举方案时使用）：仅含一个区段成员，值 0。
/// 成员标识符 "Default" 是规避 C# 关键字 default 后的固化命名（语义即规格中的 default 区段）。
/// </summary>
public enum DefaultBands
{
    /// <summary>默认区段（值 0）。</summary>
    Default = 0,
}
