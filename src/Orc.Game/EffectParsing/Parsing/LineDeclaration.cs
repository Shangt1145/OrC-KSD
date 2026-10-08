namespace Orc.Game.EffectParsing.Parsing;

/// <summary>
/// 声明维度（词条行成分的三个可声明维度——与 <c>filters.json</c> 的 <c>dimension</c> 载荷对应）：
/// <c>keyword</c>／<c>unitType</c>／<c>attribute</c>。
/// <c>object</c> 维度（花费/行动花费/指挥点槽/指挥点）不在词条行判定内——不产声明、保持既有路径。
/// </summary>
public enum DeclarationDimension
{
    /// <summary>词条。</summary>
    Keyword,

    /// <summary>兵种。</summary>
    UnitType,

    /// <summary>属性（攻击力/防御力）。</summary>
    Attribute,
}

/// <summary>
/// 注册状态（三态——仅词条维度适用）：以**词条字面（原文）**对照词条注册面
/// （<see cref="Orc.Game.Cards.KeywordRegistry"/>）判定（词法取值不参与判定）；
/// 兵种/属性维度＝<see cref="NotApplicable"/>（注册面为词条概念）。
/// </summary>
public enum DeclarationRegistration
{
    /// <summary>不适用（兵种/属性维度）。</summary>
    NotApplicable,

    /// <summary>已注册（字面在词条注册面内）。</summary>
    Registered,

    /// <summary>未注册（字面不在词条注册面内——**产出并标注**，不丢弃、不静默）。</summary>
    Unregistered,
}

/// <summary>
/// 词条行声明（解析层产出物·词条行联动）：词条行的**一个行内成分**（按维度分类）的形态提取结果。
/// 一行 N 个词条＝N 条声明（逐词条分列）；声明序＝行序＋行内词条序；**保留重复**（层不判重）。
/// <para>最小必需集＝维度 ＋ 标识（词条字面）＋ 参值 ＋ 注册状态（四元）；来源定位＝原文 span（建议携带）。
/// 解析层只做**形态提取**——"词条是否应有参值"的交叉校验不在本层（上层另行处理）。</para>
/// <para>**命名约束**：本类型与卡定义层 <c>Orc.Game.Cards.KeywordDeclaration</c>（定义期声明）不同层、
/// 不同名（本名带 "Line" 定位语）——本类型＝解析层把「词条行」从"静默丢弃"改为"产出"的形态提取记录。</para>
/// </summary>
/// <param name="Dimension">维度（keyword／unitType／attribute）。</param>
/// <param name="Id">标识＝词条字面（原文；filters 取值不参与标识判定）。</param>
/// <param name="Value">参值（null＝空缺——无参值或参值位空；整数化形态）。</param>
/// <param name="Registration">注册状态（仅词条维度适用——见 <see cref="DeclarationRegistration"/>）。</param>
/// <param name="Span">来源定位（词条成分的原文区间）。</param>
public sealed record LineDeclaration(
    DeclarationDimension Dimension,
    string Id,
    int? Value,
    DeclarationRegistration Registration,
    TextSpan Span);
