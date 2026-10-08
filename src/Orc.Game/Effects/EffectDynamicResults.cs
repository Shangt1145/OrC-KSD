using Orc.Cards;

namespace Orc.Game.Effects;

/// <summary>
/// **挂载凭据**（csx 动态效果受控面·批 4）：<c>EffectRuntime.AttachPrefabAsync</c>／<c>AttachSnapshotAsync</c>／
/// <c>CompileAttachAsync</c> 成功时发放的**不透明引用**——csx 创作者仅需「持有并回传」
/// （传给 <c>EffectRuntime.DetachEffectAsync</c>）：不暴露内部字段/结构、不要求可序列化/跨会话保持、无枚举/查询面；
/// csx 可将其存入变量、闭包或经既有数据面带存**跨事件传递**；同一性＝引用相等。
/// <para>身份语义：每次挂载唯一、互不相等（复装＝新凭据；同一来源多次挂载＝各自独立凭据）。</para>
/// </summary>
public sealed class EffectAttachCredential
{
    internal EffectAttachCredential(Effect effect) => Effect = effect;

    /// <summary>凭据对应的挂载效果实例（内部引用；「不透明」契约——不对外暴露）。</summary>
    internal Effect Effect { get; }
}

/// <summary>
/// 动态效果受控操作的**状态类别**（挂载/卸载/编译挂载共用·单一枚举族）：字面稳定、测试锁定
/// （对齐批 3 预检器「类别稳定契约」先例）。
/// </summary>
public enum EffectAttachStatus
{
    /// <summary>成功（挂载＝实例化＋生效路径成功〔被动：装载成功；主动：入列表〕＋凭据有效；卸载＝实际移除命中）。</summary>
    Success = 0,

    /// <summary>来源未命中（未注册 prefabId／模板或 op 资产缺失）。</summary>
    SourceMissing = 1,

    /// <summary>数据无效（快照 JSON 反序列化失败／DSL 解析失败）。</summary>
    InvalidData = 2,

    /// <summary>编译失败（DSL→快照渲染/编译层失败——数据有效但无法编译）。</summary>
    CompileFailed = 3,

    /// <summary>实例化失败（视图类型/处理器解析等——内核实例化失败分类映射）。</summary>
    InstantiateFailed = 4,

    /// <summary>装载失败（OnMount／装载完成动作失败——已完整回滚、对卡零残留）。</summary>
    MountFailed = 5,

    /// <summary>未命中／已失效（卸载面：凭据/引用不存在或已清理、对局终局后按失效结果处理）。</summary>
    NotFound = 6,

    /// <summary>跨卡归属（卸载面：效果不在目标卡上——属于另一张卡）。</summary>
    CrossCardOwnership = 7,

    /// <summary>服务不可用／未装配（无对局上下文/机制不可用/非本对局引用/非可操作相位的挂载面拒斥）。</summary>
    ServiceUnavailable = 8,
}

/// <summary>
/// 动态效果受控操作的**统一结果结构**（单一结构——挂载/卸载/编译挂载三门面与 Detach 共用）：
/// 成功＝凭据（挂载面必需；卸载面恒为 null）＋效果名（附加诊断）；失败＝类别＋原因文本（必需）。
/// <para>语义层失败一律**结构化返回（不抛）**；参数层错误（null/空白）沿用门面既有风格（抛）。</para>
/// </summary>
public sealed class EffectAttachResult
{
    private EffectAttachResult(
        EffectAttachStatus status,
        EffectAttachCredential? credential,
        string? effectName,
        string? failureReason)
    {
        Status = status;
        Credential = credential;
        EffectName = effectName;
        FailureReason = failureReason;
    }

    /// <summary>状态类别（字面稳定、测试锁定）。</summary>
    public EffectAttachStatus Status { get; }

    /// <summary>是否成功（＝<see cref="EffectAttachStatus.Success"/>）。</summary>
    public bool Success => Status == EffectAttachStatus.Success;

    /// <summary>挂载凭据（仅成功挂载非 null；卸载面恒为 null）。</summary>
    public EffectAttachCredential? Credential { get; }

    /// <summary>效果名（附加诊断；挂载成功＝实例化名，卸载成功＝被卸载效果名）。</summary>
    public string? EffectName { get; }

    /// <summary>失败原因文本（失败时非空；类别＋原因构成可诊断失败面）。</summary>
    public string? FailureReason { get; }

    /// <summary>成功结果（挂载面带凭据；卸载面 credential 为 null）。</summary>
    internal static EffectAttachResult Ok(EffectAttachCredential? credential, string effectName)
        => new(EffectAttachStatus.Success, credential, effectName, null);

    /// <summary>失败结果（类别＋原因）。</summary>
    internal static EffectAttachResult Failed(EffectAttachStatus status, string reason)
        => new(status, null, null, reason);
}
