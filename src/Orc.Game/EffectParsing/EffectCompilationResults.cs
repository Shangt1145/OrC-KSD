using Orc.Cards;
using Orc.Game.EffectParsing.Parsing;

namespace Orc.Game.EffectParsing;

// ─────────────────────────────────────────────────────────────────────────────
// 效果离线编译驱动的结果类型族（批 5·N2a）：
//   报告＝单一类型同体承载（五区＋汇总）——输入级区＋效果级成功区＋效果级失败区
//   ＋留痕三类区（三个独立清单）＋声明区＋汇总；单条与批量**同一报告类型**
//   （单条＝批量的 0/1 源输入特例）。
//   失败类别集与留痕类别字面**稳定**（测试锁定）：失败＝InvalidData／SourceMissing／
//   CompileFailed／InvalidIdentity／DuplicateIdentity／IoFailed；留痕＝Unresolved／
//   NeedsCsx／Csx／PlaceholderCondition。
//   序（确定性）：源文件序 → 效果声明序 → 条内出现序。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>编译模式（「模式只控制动作〔写不写盘〕，不改变判定」——差异仅 <c>IoFailed</c> 自身）。</summary>
public enum EffectCompilationMode
{
    /// <summary>单条入口（纯返回——不落盘；产物内容必携）。</summary>
    Single,

    /// <summary>目录级批量·落盘模式（默认；写出 <c>*.prefab.json</c>）。</summary>
    Disk,

    /// <summary>目录级批量·dry-run（预览/校验——输出侧零写 IO：不写盘、不创建目录、不探测输出目录）。</summary>
    DryRun,
}

/// <summary>失败类别（结构化、字面稳定——测试锁定；消费方按类分流）。</summary>
public enum EffectCompilationFailureCategory
{
    /// <summary>输入数据无效（<c>*.dsl.json</c> 的 JSON 非法／缺必填字段／模板字段为空等——契约数据失败、不产出）。</summary>
    InvalidData,

    /// <summary>来源未命中（模板/op 资产缺失——消费方处置＝补资产）。</summary>
    SourceMissing,

    /// <summary>编译失败（DSL 内容问题：槽位未填／槽位越界／op 参数非法等——消费方处置＝修文本）。</summary>
    CompileFailed,

    /// <summary>身份非法（基名/派生 id 含落盘非法字符——判定沿批 2 <c>PrefabWriter</c> 规则；逐效果分条、不占位）。</summary>
    InvalidIdentity,

    /// <summary>批内身份重复（首见占位；重复者失败——原因含首见者源文件名＋派生身份）。</summary>
    DuplicateIdentity,

    /// <summary>IO 类失败（输入读取失败／输出写入失败——落盘阶段失败并入本类）。</summary>
    IoFailed,
}

/// <summary>留痕类别（分列三类、字面稳定——测试锁定；「留痕」≠「失败」，可并存）。</summary>
public enum EffectCompilationTraceCategory
{
    /// <summary>未解析记录（文本级——原文出处＋原因；不归任何效果）。</summary>
    Unresolved,

    /// <summary>needsCsx 留痕（效果级——op 无法解析的原文保留；随条目携带原文）。</summary>
    NeedsCsx,

    /// <summary>csx 逃生舱留痕（效果级——人工 csx 脚本 op）。</summary>
    Csx,

    /// <summary>占位条件（效果级——<c>raw</c> 条件渲染为 <c>if (false /* TODO */)</c>）。</summary>
    PlaceholderCondition,
}

/// <summary>输入类型。</summary>
public enum EffectCompilationInputKind
{
    /// <summary>卡面文本（一文件＝一卡可多效果）。</summary>
    CardFaceText,

    /// <summary>DSL JSON（一文件＝一效果实例＝一产出，身份基名＝文件基名去 <c>.dsl.json</c>）。</summary>
    Dsl,
}

/// <summary>
/// 计数集（输入级与汇总**一致维度**——三层互核逐层直读）：
/// 解析效果数／成功数／语义完整数／六类失败分列／三类留痕分列／声明数。
/// </summary>
/// <param name="ParsedEffectCount">解析效果数（该输入解析出的效果数；声明不计入）。</param>
/// <param name="SuccessCount">成功数（落盘＝写成功；dry-run/单条＝编译成功）。</param>
/// <param name="SemanticallyCompleteCount">语义完整计数（成功且无占位条件、无 needsCsx/csx 留痕，递归覆盖内嵌）。</param>
/// <param name="FailureInvalidDataCount">失败计数·无效数据。</param>
/// <param name="FailureSourceMissingCount">失败计数·来源未命中。</param>
/// <param name="FailureCompileFailedCount">失败计数·编译失败。</param>
/// <param name="FailureInvalidIdentityCount">失败计数·身份非法。</param>
/// <param name="FailureDuplicateIdentityCount">失败计数·身份重复。</param>
/// <param name="FailureIoFailedCount">失败计数·IO 失败。</param>
/// <param name="TraceUnresolvedCount">留痕计数·未解析（文本级）。</param>
/// <param name="TraceNeedsCsxCount">留痕计数·needsCsx/csx（效果级，两类合计）。</param>
/// <param name="TracePlaceholderConditionCount">留痕计数·占位条件（效果级）。</param>
/// <param name="DeclarationCount">声明计数（词条行声明；不计入效果/覆盖率计）。</param>
public sealed record EffectCompilationCounts(
    int ParsedEffectCount,
    int SuccessCount,
    int SemanticallyCompleteCount,
    int FailureInvalidDataCount,
    int FailureSourceMissingCount,
    int FailureCompileFailedCount,
    int FailureInvalidIdentityCount,
    int FailureDuplicateIdentityCount,
    int FailureIoFailedCount,
    int TraceUnresolvedCount,
    int TraceNeedsCsxCount,
    int TracePlaceholderConditionCount,
    int DeclarationCount);

/// <summary>
/// 输入级条目（逐输入一条——汇总「输入数」与条目可对账；含 0 效果文件／读失败文件／DSL 解析失败文件）。
/// </summary>
/// <param name="SourceFileName">源文件名（批量＝文件名；单条＝null——调用上下文标签承载缺省位）。</param>
/// <param name="ContextLabel">调用上下文标签（单条可选；批量＝null）。</param>
/// <param name="InputKind">输入类型。</param>
/// <param name="OutputFiles">产出文件列表（事实字段；按处理序；dry-run/单条＝空）。</param>
/// <param name="Counts">该输入的计数集（五计数＋声明——与汇总同维度）。</param>
/// <param name="FileFailureCategory">文件级失败类别（读失败＝IoFailed／DSL 解析失败＝InvalidData；无＝null）。</param>
/// <param name="FileFailureReason">文件级失败原因（既有层消息原文——不吞不降级）。</param>
public sealed record EffectCompilationInputEntry(
    string? SourceFileName,
    string? ContextLabel,
    EffectCompilationInputKind InputKind,
    IReadOnlyList<string> OutputFiles,
    EffectCompilationCounts Counts,
    EffectCompilationFailureCategory? FileFailureCategory,
    string? FileFailureReason);

/// <summary>
/// 效果级成功条目（关联键＝源文件名＋效果身份〔派生 id〕＋声明序序号——可定位到留痕/产物区）。
/// </summary>
/// <param name="SourceFileName">源文件名（单条＝null）。</param>
/// <param name="EffectId">派生身份（N=1＝基名；N≥2＝<c>{基名}.{n}</c>，声明序自 1 起）。</param>
/// <param name="DeclarationOrdinal">声明序序号（1 起）。</param>
/// <param name="SemanticallyComplete">语义完整档位（无占位条件、无 needsCsx/csx 留痕——递归覆盖内嵌）。</param>
/// <param name="OutputFile">产出文件（事实字段：落盘成功＝实际写入文件；dry-run/单条＝null）。</param>
/// <param name="ExpectedTargetFile">预期目标文件路径（dry-run 且提供输出目录＝必填；其它可缺省；不得虚构）。</param>
public sealed record EffectCompilationSuccess(
    string? SourceFileName,
    string EffectId,
    int DeclarationOrdinal,
    bool SemanticallyComplete,
    string? OutputFile,
    string? ExpectedTargetFile);

/// <summary>
/// 效果级失败条目（关联键＝源文件名＋效果身份＋声明序序号）。
/// </summary>
/// <param name="SourceFileName">源文件名（单条＝null）。</param>
/// <param name="EffectId">意图身份（原始基名＋派生序号——如实呈现，不因非法/重复而改写或清空）。</param>
/// <param name="DeclarationOrdinal">声明序序号（1 起）。</param>
/// <param name="Category">失败类别。</param>
/// <param name="Reason">失败原因（完整包含既有层错误消息原文——允许前缀、不删改）。</param>
/// <param name="ExpectedTargetFile">预期目标文件路径（<c>IoFailed</c> 落盘失败＝必填；<c>InvalidIdentity</c>/<c>DuplicateIdentity</c>＝不得填；其余可缺省）。</param>
public sealed record EffectCompilationFailure(
    string? SourceFileName,
    string EffectId,
    int DeclarationOrdinal,
    EffectCompilationFailureCategory Category,
    string Reason,
    string? ExpectedTargetFile);

/// <summary>留痕·未解析（文本级——关联到源文件级；无产出文件时允许缺省）。</summary>
/// <param name="Category">类别字面（<see cref="EffectCompilationTraceCategory.Unresolved"/>）。</param>
/// <param name="SourceFileName">源文件名（单条＝null）。</param>
/// <param name="RawText">原文片段（不吞——必须携带）。</param>
/// <param name="Reason">失败原因。</param>
/// <param name="Start">原文起始偏移。</param>
/// <param name="Length">原文长度。</param>
public sealed record EffectCompilationUnresolvedTrace(
    EffectCompilationTraceCategory Category,
    string? SourceFileName,
    string RawText,
    string Reason,
    int Start,
    int Length);

/// <summary>留痕·needsCsx/csx（效果级——原文（脚本）优先；落点承载＝预期字段在适用情形承担，事实字段确有产出时填写）。</summary>
/// <param name="Category">类别字面（<see cref="EffectCompilationTraceCategory.NeedsCsx"/>／<see cref="EffectCompilationTraceCategory.Csx"/>）。</param>
/// <param name="SourceFileName">源文件名（单条＝null）。</param>
/// <param name="EffectId">命中留痕的效果身份（派生 id）。</param>
/// <param name="DeclarationOrdinal">声明序序号（1 起）。</param>
/// <param name="Location">槽位或内嵌路径（best effort——如 <c>on_deploy[0]</c>／<c>on_deploy[0].nested[0].on_event[1]</c>）。</param>
/// <param name="Script">原文（脚本）。</param>
/// <param name="OutputFile">产出文件（事实字段——确有产出时填写；无产出＝null）。</param>
/// <param name="ExpectedTargetFile">预期目标文件路径（dry-run 且提供输出目录且该效果编译成功＝必填〔与成功条目一致〕；该效果落盘失败＝必填〔同失败条目路径〕；其余可缺省；不得虚构、不与事实字段混淆）。</param>
public sealed record EffectCompilationNeedsCsxTrace(
    EffectCompilationTraceCategory Category,
    string? SourceFileName,
    string EffectId,
    int DeclarationOrdinal,
    string Location,
    string? Script,
    string? OutputFile,
    string? ExpectedTargetFile);

/// <summary>留痕·占位条件（效果级——<c>raw</c> 条件；落点承载＝预期字段在适用情形承担，事实字段确有产出时填写）。</summary>
/// <param name="Category">类别字面（<see cref="EffectCompilationTraceCategory.PlaceholderCondition"/>）。</param>
/// <param name="SourceFileName">源文件名（单条＝null）。</param>
/// <param name="EffectId">命中留痕的效果身份（派生 id）。</param>
/// <param name="DeclarationOrdinal">声明序序号（1 起）。</param>
/// <param name="Location">条件位置（best effort——如 <c>on_deploy[0].condition</c>）。</param>
/// <param name="RawText">条件原文。</param>
/// <param name="OutputFile">产出文件（事实字段——确有产出时填写；无产出＝null）。</param>
/// <param name="ExpectedTargetFile">预期目标文件路径（dry-run 且提供输出目录且该效果编译成功＝必填〔与成功条目一致〕；该效果落盘失败＝必填〔同失败条目路径〕；其余可缺省；不得虚构、不与事实字段混淆）。</param>
public sealed record EffectCompilationPlaceholderConditionTrace(
    EffectCompilationTraceCategory Category,
    string? SourceFileName,
    string EffectId,
    int DeclarationOrdinal,
    string Location,
    string? RawText,
    string? OutputFile,
    string? ExpectedTargetFile);

/// <summary>声明区条目（词条行声明——独立信息区：非留痕、非失败、不影响档位、不计入效果/覆盖率计）。</summary>
/// <param name="SourceFileName">源文件名（单条＝null）。</param>
/// <param name="Dimension">维度（keyword／unitType／attribute）。</param>
/// <param name="Id">标识（词条字面——原文）。</param>
/// <param name="Value">参值（null＝空缺）。</param>
/// <param name="Registration">注册状态（仅词条维度适用）。</param>
/// <param name="Line">行序（1 起——原文行；best effort）。</param>
/// <param name="Start">原文起始偏移。</param>
/// <param name="Length">原文长度。</param>
public sealed record EffectCompilationDeclaration(
    string? SourceFileName,
    DeclarationDimension Dimension,
    string Id,
    int? Value,
    DeclarationRegistration Registration,
    int Line,
    int Start,
    int Length);

/// <summary>
/// 汇总（计数＋运行参数快照——日志自足；单条缺省口径：文件/目录类字段缺省、不得伪造）。
/// </summary>
/// <param name="Mode">模式（单条／落盘／dry-run）。</param>
/// <param name="InputCount">输入数（批量＝匹配文件数；单条＝1；＝输入级条目数）。</param>
/// <param name="InputDirectory">输入目录（批量；单条＝null）。</param>
/// <param name="OutputDirectory">输出目录（批量落盘＝目录；dry-run＝提供时填；单条＝null）。</param>
/// <param name="AssetSource">资产来源标识（默认目录／显式目录路径／内存注入）。</param>
/// <param name="Counts">计数汇总（Σ输入级——一致维度）。</param>
public sealed record EffectCompilationSummary(
    EffectCompilationMode Mode,
    int InputCount,
    string? InputDirectory,
    string? OutputDirectory,
    string AssetSource,
    EffectCompilationCounts Counts);

/// <summary>
/// 编译报告（单一类型同体承载——「五区＋汇总」；单条与批量同一类型）：
/// 输入级区＋效果级成功区＋效果级失败区＋留痕三类区（三个独立清单）＋声明区＋汇总。
/// </summary>
/// <param name="Inputs">输入级条目区（逐输入一条；序＝源文件序）。</param>
/// <param name="Successes">效果级成功区（序＝源文件序 → 效果声明序）。</param>
/// <param name="Failures">效果级失败区（序＝源文件序 → 效果声明序）。</param>
/// <param name="UnresolvedTraces">留痕区①·未解析（文本级）。</param>
/// <param name="NeedsCsxTraces">留痕区②·needsCsx/csx（效果级）。</param>
/// <param name="PlaceholderConditionTraces">留痕区③·占位条件（效果级）。</param>
/// <param name="Declarations">声明区（词条行声明；独立信息区）。</param>
/// <param name="Summary">汇总。</param>
public sealed record EffectCompilationReport(
    IReadOnlyList<EffectCompilationInputEntry> Inputs,
    IReadOnlyList<EffectCompilationSuccess> Successes,
    IReadOnlyList<EffectCompilationFailure> Failures,
    IReadOnlyList<EffectCompilationUnresolvedTrace> UnresolvedTraces,
    IReadOnlyList<EffectCompilationNeedsCsxTrace> NeedsCsxTraces,
    IReadOnlyList<EffectCompilationPlaceholderConditionTrace> PlaceholderConditionTraces,
    IReadOnlyList<EffectCompilationDeclaration> Declarations,
    EffectCompilationSummary Summary);

/// <summary>
/// 产物条目（产物内容——快照与 JSON 文本「同列」；按处理序）。
/// JSON 文本与落盘文本契约同款（<c>PrefabWriter</c> 写出文件内容逐字符一致——单一真源）。
/// </summary>
/// <param name="EffectId">效果身份（＝快照 <c>root.id</c>；关联成功区条目的定位键）。</param>
/// <param name="Snapshot">内存快照。</param>
/// <param name="JsonText">JSON 文本（与落盘写出的文件内容逐字符一致）。</param>
public sealed record EffectCompilationArtifact(
    string EffectId,
    EffectSnapshot Snapshot,
    string JsonText);

/// <summary>
/// 编译结果（统一返回结构——同构：单条＝批量特例）。
/// 产物区（<see cref="Artifacts"/>）填充口径：单条＝必携；dry-run＝必携（每个成功效果——预览即产出）；
/// 批量落盘＝默认不携带（内容以磁盘为准——事实字段＝产出文件路径）；零产出＝空列表。
/// </summary>
/// <param name="Artifacts">产物区（快照集合＋JSON 文本；按处理序）。</param>
/// <param name="Report">报告区（五区＋汇总——与批量同一类型）。</param>
public sealed record EffectCompilationResult(
    IReadOnlyList<EffectCompilationArtifact> Artifacts,
    EffectCompilationReport Report);
