using Orc.Core;
using Orc.Game.Board;

namespace Orc.Game.Cards;

/// <summary>
/// 部署逻辑条目：名称（日志定位用）＋handler（可空——「handler 非空」检查的承载：空 handler 条目＝无效条目、触发时跳过）。
/// 本类型不持运行期状态。
/// </summary>
public sealed class DeploymentLogicEntry
{
    internal DeploymentLogicEntry(string name, Func<DeploymentLogicContext, CancellationToken, Task>? handler)
    {
        Name = name;
        Handler = handler;
    }

    /// <summary>条目名称（日志定位用）。</summary>
    public string Name { get; }

    /// <summary>部署逻辑 handler（null＝无效条目、触发时跳过）；接收被部署单位与目标槽位等上下文。</summary>
    public Func<DeploymentLogicContext, CancellationToken, Task>? Handler { get; }
}

/// <summary>
/// 部署逻辑组件（2B；部署词条效果的登记载体；每卡至多一份——经 <see cref="Orc.Cards.Card.AddData"/> 挂载）：
/// 条目按登记序维护（先登记先触发）；部署链消费＝默认检查「组件存在且存在 handler 非空的有效条目」→ 按序触发有效条目；
/// 无组件或无有效条目＝跳过词条效果（部署不因此失败、不阻断单位化与扣费）。
/// 独立于词条系统（不要求与卡上词条面〔词条管理组件〕挂钩）；「部署效果＝组件生成逻辑＋被动主效果（加载时生成部署组件）」
/// 属加载/生成链（装配机制）——A4 起生成面接通：装配方经 <see cref="DeploymentLogicRegistry"/> 程序化登记条目、
/// <see cref="CardBase"/> 加载模板「部署逻辑生成」步骤（加载期）为单位卡生成并挂载本组件；
/// 消费＝<see cref="DeploymentLogicRules.RunEffectSegmentAsync"/>（单源：部署链①段与再触发「部署重放」共享）。
/// 纯数据＋登记，无触发行为（触发编排在单位卡的部署触发器默认链中）。
/// </summary>
public sealed class DeploymentLogicData
{
    private readonly List<DeploymentLogicEntry> _entries = new();

    /// <summary>登记条目（登记序只读快照）。</summary>
    public IReadOnlyList<DeploymentLogicEntry> Entries => _entries;

    /// <summary>登记一条部署逻辑（登记序保留——触发顺序断言依据）。返回本组件（链式登记）。</summary>
    /// <param name="name">条目名称（非 null/空白）。</param>
    /// <param name="handler">部署逻辑 handler（可空——空 handler 条目＝无效、触发时跳过）。</param>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    public DeploymentLogicData Add(string name, Func<DeploymentLogicContext, CancellationToken, Task>? handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _entries.Add(new DeploymentLogicEntry(name, handler));
        return this;
    }
}

/// <summary>
/// 部署逻辑触发上下文（部署词条效果被触发时的可见信息）：
/// 被部署单位（<see cref="Unit"/>）＋本次部署的目标槽位（<see cref="Target"/>）＋引擎引用（<see cref="Engine"/>，写效果所需）。
/// 由部署链在触发每个有效条目时构造（每次触发独立实例）。
/// </summary>
public sealed class DeploymentLogicContext
{
    internal DeploymentLogicContext(UnitCard unit, Slot target, LogicEngine engine)
    {
        Unit = unit;
        Target = target;
        Engine = engine;
    }

    /// <summary>被部署的单位卡（本次触发所属）。</summary>
    public UnitCard Unit { get; }

    /// <summary>本次部署的目标槽位。</summary>
    public Slot Target { get; }

    /// <summary>引擎引用（写效果所需：发射更新 / 触发子触发器）。</summary>
    public LogicEngine Engine { get; }
}

/// <summary>
/// 部署效果段执行（A4 单源化——硬性口径）：部署链①段与再触发「部署重放」共享同一执行实现——
/// 同序（登记序）、同「handler 非空」检查、同逐条异常隔离、同上下文构造（<see cref="DeploymentLogicContext"/>）、
/// 同日志留痕形态（source「部署逻辑」）；打出路径观察面保持既有不变（以既有打出路径行为为基准）。
/// 无组件或无有效条目＝无操作（部署不因此失败、不阻断单位化与扣费；再触发＝无操作）。
/// </summary>
public static class DeploymentLogicRules
{
    /// <summary>
    /// 执行部署效果段（按登记序触发有效条目；逐条异常隔离〔记录并继续；取消类异常穿透〕）。
    /// </summary>
    /// <exception cref="ArgumentNullException">unit / target / engine 为 null。</exception>
    public static async Task RunEffectSegmentAsync(
        UnitCard unit, Slot target, LogicEngine engine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(engine);

        if (!unit.TryGetData<DeploymentLogicData>(out var logic))
        {
            return; // 无组件＝跳过词条效果（部署不因此失败）
        }

        foreach (var entry in logic.Entries)
        {
            if (entry.Handler is null)
            {
                continue; // 「handler 非空」检查：空 handler 条目＝无效、跳过
            }

            try
            {
                await entry.Handler(new DeploymentLogicContext(unit, target, engine), ct);
            }
            catch (OperationCanceledException)
            {
                throw; // 取消类异常不隔离（沿用引擎口径）
            }
            catch (Exception ex)
            {
                engine.RootStream.WriteLog(
                    "部署逻辑",
                    ex.Message,
                    LogLevel.Error,
                    new[] { $"exception:{ex.GetType().Name}" },
                    new Dictionary<string, object?>
                    {
                        ["exceptionType"] = ex.GetType().FullName,
                        ["message"] = ex.Message,
                    });
            }
        }
    }
}

/// <summary>
/// 部署逻辑注册表（A4；装配侧持有——「登记/生成面」的装配方入口）：
/// 登记「卡 id → 部署逻辑条目清单」（name＋handler——程序化提供、已构造逻辑/委托，非文本解析）；
/// 加载期由生成环节（<see cref="CardBase"/> 加载模板的「部署逻辑生成」步骤）为单位卡生成并挂载
/// <see cref="DeploymentLogicData"/> 组件（「加载时生成部署组件」愿景落实）。
/// 语义：条目按登记序追加、不做去重（名称非唯一键——日志定位用）；适用对象＝单位卡（部署链为单位路径；
/// 非单位卡不生成/不消费）；无登记卡不生成（缺省——消费端已「无组件＝跳过」）。
/// 与组件层 <see cref="DeploymentLogicData.Add"/>（手动登记）并存（手动登记继续合法——测试/特殊装配用；
/// 生成环节与手动登记相遇＝对齐容器语义：组件已存在＝生成环节跳过并申报）。
/// </summary>
public sealed class DeploymentLogicRegistry
{
    private readonly Dictionary<string, List<DeploymentLogicEntry>> _byCard = new(StringComparer.Ordinal);

    /// <summary>
    /// 登记一条部署逻辑（装配方程序化提供；同卡多条按登记序追加——触发顺序依据）。
    /// </summary>
    /// <param name="cardId">卡库注册 id（该条目挂钩的卡）。</param>
    /// <param name="name">条目名称（非 null/空白；日志定位用）。</param>
    /// <param name="handler">部署逻辑 handler（可空——空 handler 条目＝无效、触发时跳过；与组件层口径一致）。</param>
    /// <exception cref="ArgumentException">cardId 或 name 为 null/空白。</exception>
    public void Register(string cardId, string name, Func<DeploymentLogicContext, CancellationToken, Task>? handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_byCard.TryGetValue(cardId, out var list))
        {
            list = new List<DeploymentLogicEntry>();
            _byCard[cardId] = list;
        }

        list.Add(new DeploymentLogicEntry(name, handler));
    }

    /// <summary>该卡的登记条目（登记序；未登记＝空列表——加载期生成环节查询面）。</summary>
    internal IReadOnlyList<DeploymentLogicEntry> GetEntries(string cardId)
        => _byCard.TryGetValue(cardId, out var list) ? list : Array.Empty<DeploymentLogicEntry>();

    /// <summary>存在性查询（诊断/测试读面）：该卡是否有登记条目（null/空白＝false、不抛错）。</summary>
    public bool Contains(string cardId)
        => !string.IsNullOrWhiteSpace(cardId) && _byCard.ContainsKey(cardId);
}

/// <summary>
/// 部署逻辑装载语境（A4；加载时点按卡 id 构造——经卡牌库注入、<see cref="CardBase.LoadAsync"/> 的
/// 「部署逻辑生成」步骤取用）：装配源（注册表）查询面。
/// 无装配源（注册表缺省）＝条目为空（生成环节跳过、加载照常）；独立构造（不经卡库）＝提供器为 null＝步骤跳过
/// （不抛错、加载不失败、功能不可用——沿用词条/效果装载先例）。
/// </summary>
public sealed class DeploymentLogicLoadContext
{
    private readonly DeploymentLogicRegistry? _registry;
    private readonly string _cardId;

    internal DeploymentLogicLoadContext(DeploymentLogicRegistry? registry, string cardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        _registry = registry;
        _cardId = cardId;
    }

    /// <summary>本卡的登记条目（登记序；无装配源或未登记＝空列表）。</summary>
    internal IReadOnlyList<DeploymentLogicEntry> Entries
        => _registry?.GetEntries(_cardId) ?? Array.Empty<DeploymentLogicEntry>();
}
