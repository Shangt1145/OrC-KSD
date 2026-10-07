using Orc.Cards;

namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// S1（老兵机制——组件替换式升级／信号／数据映射／读取面）：
// ①老兵读取面（VeteranRules.IsVeteran——单位级布尔判定；「老兵单位」筛选/「若是老兵」条件的对接读口；
//   权威依据＝「老兵」标记（KeywordIds.Veteran）——实例当前内容即真源）。
// ②升级发动结果三态（成功升级/幂等无操作/拒绝——可程序化区分；拒绝原因类别化可读；S1 锁定三态语义）。
// ③发动结果捕获箱（经触发器发动的结果回传通道——对齐预打出捕获箱（CardCaptureBox）先例）。
// 升级动作的唯一标准发动入口＝卡侧具名主动触发器「老兵触发器」（UnitCard.VeteranTrigger；
// 公共发动包装面＝UnitCard.InvokeVeteranTriggerAsync——效果层/S3 的对接路径；本文件不建第二条发动路径）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 老兵机制规则助手（S1；静态便利口——对齐 <see cref="SuppressRules"/>/<see cref="InhibitRules"/> 服务先例）：
/// 读取面（「是否老兵」判定）＋对外契约常量（触发器稳定名）。
/// 升级动作的发动不经本类（唯一标准发动入口＝卡侧「老兵触发器」；公共发动包装面＝
/// <see cref="UnitCard.InvokeVeteranTriggerAsync"/>——须经触发器、不得绕过直调内部实现）。
/// </summary>
public static class VeteranRules
{
    /// <summary>
    /// 「老兵触发器」标识名（S1 定稿——**对 S3 契约**：升级动作的唯一标准发动入口；由卡牌的老兵触发效果触发；
    /// 触发它＝发动升级：全清换新＋广播 <c>unit.upgraded</c>）。风格对齐「部署触发器」「部署词条触发器」等具名先例。
    /// </summary>
    public const string TriggerName = "老兵触发器";

    /// <summary>
    /// 单位级「是否老兵」公共判定读口（S1；「老兵单位」筛选/「若是老兵」条件的对接面；权威依据＝「老兵」标记——
    /// 实例当前内容即真源，不以「是否经历过升级动作」为判据）：
    /// 当前实例内容含「老兵」标记＝true；非单位/无效输入（null 或未装载词条面的实体）＝false 降级、不抛错
    /// （对齐既有查询面惯例）；死亡后按既有词条查询面语义只读可用（登记保留）。
    /// 语义＝单位级布尔判定（列表级「老兵单位」筛选由使用方基于本读口与既有遍历/筛选面组合——本单不建筛选器基础设施）。
    /// </summary>
    public static bool IsVeteran(Card? card)
        => card is not null && KeywordRules.HasKeyword(card, KeywordIds.Veteran);
}

/// <summary>升级发动结局（三态；S1 锁定——三态必须可程序化区分）。</summary>
public enum VeteranPromotionOutcome
{
    /// <summary>成功升级（形态切换＋信号恰一次：有变更时 <c>card.stat.changed</c> 恰一次、<c>unit.upgraded</c> 恰一次）。</summary>
    Promoted,

    /// <summary>幂等无操作（已是老兵形态；零副作用、零信号、不中断触发链——随即断开「升级→监听→再升级」重入环）。</summary>
    AlreadyVeteran,

    /// <summary>拒绝（前置条件不满足；零副作用、零信号——「拒绝而非部分执行」服务先例）。</summary>
    Rejected,
}

/// <summary>升级拒绝原因（类别化可读；仅拒绝路径非 null——供测试断言与调用方分流）。</summary>
public enum VeteranPromotionRejectionReason
{
    /// <summary>不在场（未单位化未入场／已死亡已毁／已离场——「在场且存活」谓词不满足即拒绝）。</summary>
    NotOnField,

    /// <summary>无老兵版本·未声明（基础形态未声明 <c>BecomesVeteran:</c>）。</summary>
    VersionNotDeclared,

    /// <summary>无老兵版本·目标缺失（声明了但运行时查不到对应老兵定义；含无卡库环境——无法核对来源）。</summary>
    VersionMissing,

    /// <summary>无老兵版本·目标不完整（老兵定义缺「老兵来源」信息——无 <c>VeteranOf:</c>）。</summary>
    VersionIncomplete,

    /// <summary>无老兵版本·来源错配（老兵定义的来源未指向当前卡——数据错配防护）。</summary>
    VersionSourceMismatch,

    /// <summary>待授词条集不可构造（防御类别——词条标识未注册等；映射路径下实际不可达）。</summary>
    KeywordSetUnconstructable,
}

/// <summary>
/// 升级发动结果（三态承载；S1——「三态必须可程序化区分」的载体；对齐既有「结果对象」惯例）：
/// <see cref="Outcome"/>＝成功升级/幂等无操作/拒绝；拒绝时 <see cref="RejectionReason"/> 类别化可读（其余＝null）。
/// </summary>
public sealed class VeteranPromotionResult
{
    private VeteranPromotionResult(VeteranPromotionOutcome outcome, VeteranPromotionRejectionReason? rejectionReason)
    {
        Outcome = outcome;
        RejectionReason = rejectionReason;
    }

    /// <summary>结局（三态）。</summary>
    public VeteranPromotionOutcome Outcome { get; }

    /// <summary>拒绝原因（仅 <see cref="VeteranPromotionOutcome.Rejected"/> 非 null；成功/幂等＝null）。</summary>
    public VeteranPromotionRejectionReason? RejectionReason { get; }

    /// <summary>是否成功升级（便捷判读）。</summary>
    public bool IsPromoted => Outcome == VeteranPromotionOutcome.Promoted;

    /// <summary>创建「成功升级」结果（框架内部）。</summary>
    internal static VeteranPromotionResult Promoted() => new(VeteranPromotionOutcome.Promoted, null);

    /// <summary>创建「幂等无操作」结果（框架内部；已是老兵形态）。</summary>
    internal static VeteranPromotionResult AlreadyVeteran() => new(VeteranPromotionOutcome.AlreadyVeteran, null);

    /// <summary>创建「拒绝」结果（框架内部；携带类别化原因）。</summary>
    internal static VeteranPromotionResult Rejected(VeteranPromotionRejectionReason reason)
        => new(VeteranPromotionOutcome.Rejected, reason);
}

/// <summary>
/// 升级发动捕获箱（S1；结果回传通道——对齐预打出捕获箱（<see cref="CardCaptureBox"/>）先例）：
/// 由发动方（<see cref="UnitCard.InvokeVeteranTriggerAsync"/>）创建、随触发数据注入「老兵触发器」；
/// 触发器 handler（升级执行）经 <see cref="Capture"/> 提交三态结果；执行段失败（回滚＋异常上抛语义）经
/// <see cref="CaptureFailure"/> 提交原异常——发动方重抛（「异常上抛」的外部形态；触发器内部照常隔离记录）。
/// </summary>
public sealed class VeteranPromotionCapture
{
    /// <summary>触发数据键（机制内部导航——非对外订阅契约；由发动包装面与 handler 对称使用）。</summary>
    internal const string PayloadKey = "VeteranPromotionCapture";

    private VeteranPromotionResult? _result;
    private Exception? _failure;

    /// <summary>已提交的三态结果（未提交＝null）。</summary>
    public VeteranPromotionResult? Result => _result;

    /// <summary>是否已提交结果。</summary>
    public bool HasResult => _result is not null;

    /// <summary>执行段失败提交的原异常（无失败＝null）。</summary>
    public Exception? Failure => _failure;

    /// <summary>提交三态结果（升级执行调用；重复提交＝保留首个、不覆盖——防御）。</summary>
    /// <exception cref="ArgumentNullException">result 为 null。</exception>
    public void Capture(VeteranPromotionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result ??= result;
    }

    /// <summary>提交执行段失败（升级执行回滚路径调用；发动方重抛——「异常上抛」外部形态；重复提交＝保留首个）。</summary>
    /// <exception cref="ArgumentNullException">failure 为 null。</exception>
    public void CaptureFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _failure ??= failure;
    }
}
