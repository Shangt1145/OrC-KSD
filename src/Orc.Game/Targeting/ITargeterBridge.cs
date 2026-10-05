using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 前端桥接接口（后端↔前端交互通道；Manager 与桥接一对一——构造/装配注入：生产＝对局装配、测试＝mock）。
/// 两个阶段入口：
/// ① 候选收集（<see cref="CollectCandidatesAsync"/>）：执行时向流程提供"前端当前可交互的完整引用列表"；
/// ② 交互（<see cref="BeginInteraction"/>）：Begin 请求描述 → 玩家操作（拖拽/复选/取消）→ 引擎侧 Complete(refs)/Cancel()。
/// 收集与筛选在交互开始前完成（同步计算）；等待为异步、不阻塞。异常＝统一失败模式（后端捕获、失败结局、不抛、队列继续）。
/// </summary>
public interface ITargeterBridge
{
    /// <summary>
    /// 候选收集（含前端收集需求的请求每次恰调用一次；出队执行时调用——保证候选新鲜度，不在构造期/入队前收集；
    /// 全由"无收集需求槽位"〔新引用类/非引用类〕组成的请求不要求本调用）：
    /// 返回"前端当前可交互的完整引用列表"（语义＝完整列表，含不满足业务规则者；未返回项与细筛淘汰项的置黑由前端自行负责，后端不产出置黑标记）。
    /// 约定前端提交干净列表（不得含 null）；后端做最小防御（null 与非引用元素剔除、不崩溃）；重复引用不强制作去重（保持提交原样）。
    /// 元素应承载引擎引用实例（<see cref="Ref{T}"/>；object? 仅为容器型别）；无法规范化的元素剔除单项继续。
    /// 收集先于 Begin、不依赖交互期状态。
    /// </summary>
    /// <param name="context">收集上下文（请求标识；自收集阶段起稳定可引用）。</param>
    /// <returns>完整引用列表（元素允许弱类型容器；后端规范化）。收集失败（异常）＝统一失败模式。</returns>
    Task<IReadOnlyList<object?>> CollectCandidatesAsync(TargetingCollectionContext context);

    /// <summary>
    /// 交互开始（Begin）：向玩家展示允许子集与槽位描述并等待操作；一次 Begin 完成全部槽位选择（多槽位场景）。
    /// 玩家完成后经 <paramref name="responder"/> 提交终局（引擎侧 Complete/Cancel）；终局配对经请求标识校验；
    /// 内容不合规＝显式拒绝（不构成终局、请求继续等待；前端可纠正重试或 Cancel）。
    /// 本方法异常＝统一失败模式（失败结局、不抛、队列继续）；未 Begin 即失败＝无交互态、无显式结束通知义务（本批）。
    /// </summary>
    /// <param name="description">请求描述（候选数据〔允许子集〕、槽位描述、请求标识）。</param>
    /// <param name="responder">交互应答器（引擎侧；前端经此提交 Complete/Cancel）。</param>
    void BeginInteraction(TargetingRequestDescription description, ITargetingResponder responder);
}

/// <summary>
/// 交互应答器（引擎侧提供、请求级对象）：前端/玩家经此提交终局。
/// 配对契约：Complete/Cancel 须回传请求标识（Begin 描述携带）；不匹配＝违规处理（拒绝＋留痕＋继续等待）。
/// 合规性：Complete 内容经后端真实校验（逐槽位按类别——数量约束＋成员/标识合法性＋有效性〔＋域判定〕）；
/// 不合规＝显式拒绝（不构成终局、请求继续等待）。
/// 终局恰好一次：终局后的任何调用＝幂等忽略（留痕、不破坏队列与后续请求）；无请求时调用（陈旧应答器）同此。
/// </summary>
public interface ITargetingResponder
{
    /// <summary>
    /// 提交完整选择（统一提交面的引用类便捷面：每槽位一组引用；扁平产出＝缺省槽位承载）。
    /// 每槽位独立约束（各自 min/max）＋成员（∈ 该槽位允许集）＋有效性校验；默认允许同一引用被多槽位同时选中（不校验跨槽重复）。
    /// 混合请求（引用类＋非引用类同请求）请使用类别化元素重载（标识元素经本面不可承载）。
    /// </summary>
    /// <param name="requestId">请求标识（须与 Begin 描述携带的一致；不匹配＝违规处理）。</param>
    /// <param name="selectionsBySlot">按槽位组织的选择（键＝槽位名；取消场景改用 <see cref="Cancel"/>）。</param>
    /// <returns>true＝已处理（构成终局——成功；或域判定异常等失败终局）；false＝拒绝（不构成终局，请求继续等待；原因写留痕）。</returns>
    bool Complete(string requestId, IReadOnlyDictionary<string, IReadOnlyList<Ref<Entity>>> selectionsBySlot);

    /// <summary>
    /// 提交完整选择（统一提交面·类别化元素）：一次提交覆盖全部槽位（含混合请求——引用类＋非引用类同请求、按槽位名组织）；
    /// 元素形态按槽位类别承载（引用类＝引用元素；非引用类＝标识元素）。
    /// 校验逐槽位按类别：引用类＝元素 ∈ 该槽位允许集 ∧ IsAlive（∧ 域判定〔绑定则调用，如"仍在手牌"〕）；
    /// 非引用类＝标识 ∈ 声明集（选项声明条目 / 卡牌名单载荷）；数量约束逐槽位独立（缺键＝空组）。
    /// 违规＝显式拒绝（同"内容不合规"路径：不构成终局、继续等待、留痕、可纠正重试或 Cancel）；
    /// 域判定回调抛异常＝失败终局（系统原因；留痕；不归拒绝路径）；默认允许同一引用被多槽位同时选中（不校验跨槽重复）。
    /// </summary>
    /// <param name="requestId">请求标识（须与 Begin 描述携带的一致；不匹配＝违规处理）。</param>
    /// <param name="selectionsBySlot">按槽位组织的选择（键＝槽位名；元素＝<see cref="TargetSelection"/>〔引用元素或标识元素〕）。</param>
    /// <returns>true＝已处理（构成终局——成功；或域判定异常等失败终局）；false＝拒绝（不构成终局，请求继续等待；原因写留痕）。</returns>
    bool Complete(string requestId, IReadOnlyDictionary<string, IReadOnlyList<TargetSelection>> selectionsBySlot);

    /// <summary>
    /// 取消（前端主动；本批取消唯一来源——排队中〔未 Begin〕不可取消）。
    /// </summary>
    /// <param name="requestId">请求标识（须与 Begin 描述携带的一致；不匹配＝违规处理）。</param>
    /// <returns>true＝接受（构成取消终局）；false＝拒绝（不构成终局）。</returns>
    bool Cancel(string requestId);
}

/// <summary>候选收集上下文（随收集调用携带的请求上下文；语义：收集先于 Begin、不依赖交互期状态）。</summary>
public sealed class TargetingCollectionContext
{
    internal TargetingCollectionContext(string requestId)
    {
        RequestId = requestId;
    }

    /// <summary>请求标识（后端生成；自收集阶段起稳定可引用）。</summary>
    public string RequestId { get; }
}

/// <summary>
/// 交互请求描述（Begin 携带）：候选数据（分类承载）、槽位描述、请求标识（用于终局配对）。
/// 候选数据分类承载（可分别断言）：既有引用类＝<see cref="AllowedTargets"/>（收集＋筛选后的允许子集）；
/// 新引用类＝槽位描述 <see cref="TargetSlotDescription.AllowedReferences"/>（构造方允许集快照）；
/// 非引用类＝槽位描述载荷（选项条目 / 卡牌名单）。
/// </summary>
public sealed class TargetingRequestDescription
{
    internal TargetingRequestDescription(
        string requestId,
        IReadOnlyList<Ref<Entity>> allowedTargets,
        IReadOnlyList<TargetSlotDescription> slots)
    {
        RequestId = requestId;
        AllowedTargets = allowedTargets;
        Slots = slots;
    }

    /// <summary>请求标识（后端生成、随请求描述提供；前端在 Complete/Cancel 时回传）。</summary>
    public string RequestId { get; }

    /// <summary>
    /// 既有引用类的允许子集（两级筛选后的最终允许集）——既有引用类产出引用必须 ⊆ 本集合；
    /// 未返回项与淘汰项的置黑由前端负责。
    /// 无收集需求槽位的请求（纯新引用类/非引用类）＝空列表（对应数据经槽位描述分类交付）。
    /// </summary>
    public IReadOnlyList<Ref<Entity>> AllowedTargets { get; }

    /// <summary>槽位描述（声明序；未声明槽位/匿名槽位＝缺省槽位 <see cref="TargetSlot.DefaultName"/> 的 1..1 描述）。</summary>
    public IReadOnlyList<TargetSlotDescription> Slots { get; }
}

/// <summary>
/// 槽位描述（槽位名＋选择数量约束＋呈现形态标注＋按类别的可选数据；前端按其类型与标注自行维护选择策略）。
/// 呈现形态标注为呈现提示、非策略指令（不影响后端校验）。
/// </summary>
public sealed class TargetSlotDescription
{
    internal TargetSlotDescription(
        string name,
        TargetSlotKind kind,
        int min,
        int max,
        TargetSlotPresentation presentation,
        IReadOnlyList<Ref<Entity>>? allowedReferences,
        IReadOnlyList<OptionEntry>? options,
        IReadOnlyList<CardListing>? cardListings,
        bool hasParameter = false,
        object? parameter = null)
    {
        Name = name;
        Kind = kind;
        Min = min;
        Max = max;
        Presentation = presentation;
        AllowedReferences = allowedReferences;
        Options = options;
        CardListings = cardListings;
        HasParameter = hasParameter;
        Parameter = parameter;
    }

    /// <summary>槽位名（缺省槽位＝<see cref="TargetSlot.DefaultName"/>；提交键须与之一致）。</summary>
    public string Name { get; }

    /// <summary>槽位种类（单选/多选/选项/手牌选择/卡牌选择器）。</summary>
    public TargetSlotKind Kind { get; }

    /// <summary>至少须选到的个数（单选＝1；多选＝min，可为 0）。</summary>
    public int Min { get; }

    /// <summary>至多可选的个数（单选＝1；多选＝max ≥ 1）。</summary>
    public int Max { get; }

    /// <summary>呈现形态标注（点选 / 选项列表 / 手牌 / 卡牌阵列；呈现提示、非策略指令）。</summary>
    public TargetSlotPresentation Presentation { get; }

    /// <summary>
    /// 槽位级引用允许集快照（新引用类：手牌选择 / 卡牌选择器〔引用集形态〕——构造方提供的请求期快照，
    /// 照快照交付、不经域判定过滤）；其余槽位类别＝null
    /// （既有引用类经 <see cref="TargetingRequestDescription.AllowedTargets"/> 交付；非引用类经载荷交付）。
    /// </summary>
    public IReadOnlyList<Ref<Entity>>? AllowedReferences { get; }

    /// <summary>选项条目（选项槽位：标识＋文本、声明序——前端按"选项列表"渲染；其余槽位类别＝null）。</summary>
    public IReadOnlyList<OptionEntry>? Options { get; }

    /// <summary>卡牌名单条目（卡牌选择器〔名单形态〕：定义级标识＋可读名称——屏中卡牌阵列要素；其余槽位类别＝null）。</summary>
    public IReadOnlyList<CardListing>? CardListings { get; }

    /// <summary>是否携带槽位参数（请求级交付数据；<see cref="Parameter"/> 为 null 时仍可与"未绑定"区分）。</summary>
    public bool HasParameter { get; }

    /// <summary>
    /// 槽位参数（请求级交付数据；如"起始卡牌"实例）——供前端还原交互起点（呈现/引导提示、非策略指令）；
    /// 未绑定＝<see cref="HasParameter"/>＝false。纯交付数据、不参与后端校验。
    /// </summary>
    public object? Parameter { get; }
}
